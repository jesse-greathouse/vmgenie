using System;
using System.IO;

using Microsoft.Extensions.Logging;

using VmGenie.Artifacts;

namespace VmGenie.HyperV;

public class VmProvisioningService(
    VmHelper vmHelper,
    VhdxManager vhdxManager,
    VmLifecycleService vmLifecycleService,
    ILogger<VmProvisioningService> logger,
    Config config)
{
    private readonly VmHelper _vmHelper = vmHelper ?? throw new ArgumentNullException(nameof(vmHelper));
    private readonly VhdxManager _vhdxManager = vhdxManager ?? throw new ArgumentNullException(nameof(vhdxManager));
    private readonly VmLifecycleService _vmLifecycleService = vmLifecycleService ?? throw new ArgumentNullException(nameof(vmLifecycleService));
    private readonly ILogger<VmProvisioningService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Config _config = config ?? throw new ArgumentNullException(nameof(config));

    public Vm ProvisionVm(
        string baseVmGuid,
        string instanceName,
        string vmSwitchGuid,
        bool mergeDifferencingDisk = false,
        int generation = 2,
        string cpuCount = "1",
        string memoryMb = "512")
    {
        EnsureVmDoesNotExist(instanceName);
        _logger.LogInformation(
            "Starting VM provisioning: BaseVM={BaseGuid}, Instance={InstanceName}, Switch={SwitchGuid}, MergeDiff={Merge}",
            baseVmGuid, instanceName, vmSwitchGuid, mergeDifferencingDisk);

        string artifactDir = Path.Combine(_config.CloudDir, instanceName);
        string isoPath = GetIsoPath(artifactDir);
        string clonedVhdx = _vhdxManager.CloneBaseVhdx(baseVmGuid, instanceName, mergeDifferencingDisk);
        string switchName = ResolveSwitchName(vmSwitchGuid);
        string memStr = $"{memoryMb}MB";

        _logger.LogInformation("Provisioning VM '{InstanceName}' using PowerShell.", instanceName);

        try
        {
            CreateVm(instanceName, clonedVhdx, switchName, generation, memStr);
            if (generation == 2) ConfigureSecureBoot(instanceName);
            EnsureDvdDriveExists(instanceName);
            AttachIso(instanceName, isoPath);

            SetVmCpuCount(instanceName, cpuCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PowerShell VM provisioning failed.");
            throw new InvalidOperationException("PowerShell VM provisioning failed.", ex);
        }

        _logger.LogInformation("PowerShell VM provisioning completed successfully.");

        return FetchProvisionedVm(instanceName);
    }

    public void ExportVm(string instanceId, string destinationFolder)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentNullException(nameof(instanceId));
        if (string.IsNullOrWhiteSpace(destinationFolder))
            throw new ArgumentNullException(nameof(destinationFolder));

        // Defensive: VM must exist
        var vm = _vmHelper.GetVm(instanceId) ?? throw new InvalidOperationException($"Cannot export: VM with InstanceId '{instanceId}' does not exist in Hyper-V.");
        string instanceName = vm.Name;

        _logger.LogInformation(
            "Exporting VM '{InstanceName}' (InstanceId: {InstanceId}) to '{DestinationFolder}' via Hyper-V Export-VM.",
            instanceName, instanceId, destinationFolder);

        string cmd = $@"
Export-VM -Name '{instanceName}' -Path '{destinationFolder}'| Out-Null
";
        try
        {
            RunPowershellCommand("Export-VM", cmd);

            // --- Flatten the exported directory structure ---
            string exportedVmFolder = Path.Combine(destinationFolder, instanceName);
            if (Directory.Exists(exportedVmFolder))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(exportedVmFolder))
                {
                    var dest = Path.Combine(destinationFolder, Path.GetFileName(entry));
                    // Move folder or file up one level
                    if (Directory.Exists(entry))
                        Directory.Move(entry, dest);
                    else
                        File.Move(entry, dest);
                }
                // Delete the now-empty VM-named folder
                Directory.Delete(exportedVmFolder, recursive: true);
            }

            _logger.LogInformation(
                "VM '{InstanceName}' (InstanceId: {InstanceId}) exported and flattened successfully to '{DestinationFolder}'.",
                instanceName, instanceId, destinationFolder);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to export VM '{InstanceName}' (InstanceId: {InstanceId}) to '{DestinationFolder}'.",
                instanceName, instanceId, destinationFolder);
            throw;
        }
    }

    public void SwapIso(string instanceId, string isoPath)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentNullException(nameof(instanceId));
        if (string.IsNullOrWhiteSpace(isoPath) || !File.Exists(isoPath))
            throw new ArgumentException("ISO path does not exist.", nameof(isoPath));

        var vm = _vmHelper.GetVm(instanceId) ?? throw new InvalidOperationException($"VM with GUID '{instanceId}' not found.");
        EnsureDvdDriveExists(vm.Name);

        // Detach ISO
        RunPowershellCommand("Detach old ISO", $@"Set-VMDvdDrive -VMName '{vm.Name}' -Path $null | Out-Null");

        // Wait up to 3 seconds for ISO to actually detach
        bool detached = false;
        for (int i = 0; i < 15; i++)
        {
            string query = $@"(Get-VM -Name '{vm.Name}' | Get-VMDvdDrive).Path";
            var result = PowerShellHelper.Run(query).Trim();
            if (string.IsNullOrEmpty(result) || result.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                detached = true;
                break;
            }
            _logger.LogInformation("Waiting for ISO to eject from VM: {InstanceName}...", vm.Name);
            System.Threading.Thread.Sleep(200);
        }
        if (!detached)
            _logger.LogWarning("Timeout waiting for ISO to detach for VM: {InstanceName}", vm.Name);

        // Attach new ISO
        AttachIso(vm.Name, isoPath);
        _logger.LogInformation("ISO swapped for VM: {InstanceName} ({InstanceId}) -> {IsoPath}", vm.Name, instanceId, isoPath);
    }

    public Vm ImportGmi(Gmi gmi, string tmpFolder)
    {
        ArgumentNullException.ThrowIfNull(gmi);

        if (string.IsNullOrWhiteSpace(tmpFolder) || !Directory.Exists(tmpFolder))
            throw new DirectoryNotFoundException($"Temp folder not found: {tmpFolder}");

        // Remove existing VM if it exists
        var existingVm = _vmHelper.GetVmByName(gmi.GmiName);
        if (existingVm != null)
        {
            if (VmLifecycleService.IsRunning(existingVm.Id))
                _vmLifecycleService.Stop(existingVm.Id);
            _vhdxManager.DeleteVhdx(existingVm.Id);
            _vmLifecycleService.Delete(existingVm.Id, force: true);
        }

        // Locate the .vmcx file (by GUID, not display name)
        string vmDir = Path.Combine(tmpFolder, Gmi.VirtualMachinesDir);
        if (!Directory.Exists(vmDir))
            throw new DirectoryNotFoundException($"Expected VM config directory not found: {vmDir}");

        var vmcxFiles = Directory.GetFiles(vmDir, "*.vmcx");
        if (vmcxFiles.Length == 0)
            throw new FileNotFoundException($"No .vmcx file found in {vmDir}.");
        if (vmcxFiles.Length > 1)
            throw new InvalidOperationException($"Multiple .vmcx files found in {vmDir}; cannot determine which to import.");

        string vmcxFile = vmcxFiles[0];

        string importCmd = $@"
Import-VM -Path '{vmcxFile}' -Copy -GenerateNewId:$false | Out-Null
";
        RunPowershellCommand("Import-VM (GMI)", importCmd);

        var importedVm = _vmHelper.GetVmByName(gmi.GmiName)
            ?? throw new InvalidOperationException(
                $"VM import succeeded but VM '{gmi.GmiName}' not found in Hyper-V.");

        _logger.LogInformation("GMI import completed successfully: {GmiName} (from {TmpFolder})", gmi.GmiName, tmpFolder);

        return importedVm;
    }


    /// <summary>
    /// Imports a VM using Hyper-V's Import-VM, either as a restore (same ID) or copy (new ID).
    /// - export: The Export metadata object (source of instance name, etc).
    /// - location: The path to the folder in tmp/ where the exported artifacts are staged.
    /// - copy: If true, perform a copy (new VM ID). If false, perform a restore (preserve VM ID).
    /// Returns the imported Vm object.
    /// </summary>
    public Vm ImportVm(Export export, string location, bool copy = false)
    {
        ArgumentNullException.ThrowIfNull(export);
        if (string.IsNullOrWhiteSpace(location))
            throw new ArgumentNullException(nameof(location));
        if (!Directory.Exists(location))
            throw new DirectoryNotFoundException($"Import location not found: {location}");

        if (copy)
            return ImportVmCopy(export, location);
        else
            return ImportVmRestore(export, location);
    }

    private Vm ImportVmRestore(Export export, string location)
    {
        _logger.LogInformation(
            "Beginning VM restore: InstanceName={InstanceName}, ImportLocation={Location}",
            export.InstanceName, location);

        var existingVm = _vmHelper.GetVmByName(export.InstanceName);
        if (existingVm != null)
        {
            if (VmLifecycleService.IsRunning(existingVm.Id))
            {
                throw new InvalidOperationException(
                    $"Refusing to restore: VM '{export.InstanceName}' [{existingVm.Id}] is currently running. Please shut down the VM before restoring.");
            }

            _logger.LogWarning("Deleting existing .vhdx virtual hard drive for '{InstanceName}'", export.InstanceName);
            _vhdxManager.DeleteVhdx(existingVm.Id);

            _logger.LogWarning("VM with name '{InstanceName}' already exists and is not running. Deleting before restore...", export.InstanceName);
            _vmLifecycleService.Delete(existingVm.Id, force: true);
        }

        string vmcxFile = Path.Combine(location, "Virtual Machines", $"{export.InstanceId}.vmcx");
        if (!File.Exists(vmcxFile))
            throw new FileNotFoundException($"Could not find vmcx file at {vmcxFile}");

        string importModeArgs = "-Copy -GenerateNewId:$false";
        string cmd = $@"
Import-VM -Path '{vmcxFile}' {importModeArgs} | Out-Null
";

        RunPowershellCommand("Import-VM", cmd);

        var importedVm = _vmHelper.GetVmByName(export.InstanceName)
            ?? throw new InvalidOperationException($"VM import succeeded but VM '{export.InstanceName}' not found in Hyper-V.");

        _logger.LogInformation("VM restore completed successfully: {InstanceName}", export.InstanceName);
        return importedVm;
    }

    private Vm ImportVmCopy(Export export, string location)
    {
        _logger.LogInformation(
            "Beginning VM copy import: InstanceName={InstanceName}, ImportLocation={Location}",
            export.InstanceName, location);

        string vmcxFile = Path.Combine(location, "Virtual Machines", $"{export.InstanceId}.vmcx");
        if (!File.Exists(vmcxFile))
            throw new FileNotFoundException($"Could not find vmcx file at {vmcxFile}");

        string oldVhdxPath = _vmHelper.GetVhdxPathForVm(export.InstanceId);
        string vhdxParentDir = Path.GetDirectoryName(oldVhdxPath)!;
        string copySubDir = Path.Combine(vhdxParentDir, export.InstanceName);
        Directory.CreateDirectory(copySubDir);

        // Import and get the new VM's GUID
        string importCmd = $@"
$vm = Import-VM -Path '{vmcxFile}' -Copy -GenerateNewId -VhdDestinationPath '{copySubDir.Replace("'", "''")}'; 
$vm.Id.ToString()
";
        string importedVmId = RunPowershellAndCaptureOutput("Import-VM (capture GUID)", importCmd);

        if (string.IsNullOrEmpty(importedVmId))
            throw new InvalidOperationException("Could not determine imported VM's GUID from Import-VM output.");

        // Rename using the VM object, not -Id
        string renameCmd = $@"
Rename-VM -VM (Get-VM -Id '{importedVmId}') -NewName '{export.InstanceName}' | Out-Null
";
        RunPowershellCommand("Rename-VM", renameCmd);

        // Fetch and return by GUID
        var importedVm = _vmHelper.GetVm(importedVmId)
            ?? throw new InvalidOperationException($"Imported VM with GUID '{importedVmId}' not found in Hyper-V.");

        _logger.LogInformation("VM copy import completed and VM renamed: {InstanceName} (GUID: {Guid})", export.InstanceName, importedVmId);

        return importedVm;
    }

    private void EnsureVmDoesNotExist(string instanceName)
    {
        var vmExists = _vmHelper.GetVmByName(instanceName);
        if (vmExists != null)
        {
            string msg = $"VM with name '{instanceName}' already exists in Hyper-V (Id: {vmExists.Id}). Provisioning aborted.";
            _logger.LogError(msg);
            throw new InvalidOperationException(msg);
        }
    }

    private string GetIsoPath(string artifactDir)
    {
        string isoPath = Path.Combine(artifactDir, "seed.iso");
        if (!File.Exists(isoPath))
            throw new InvalidOperationException($"cloud-init ISO not found at: {isoPath}");
        return isoPath;
    }

    private string ResolveSwitchName(string vmSwitchGuid)
    {
        string cmd = $"(Get-VMSwitch | Where-Object {{ $_.Id -eq '{vmSwitchGuid}' }}).Name";
        string switchName = PowerShellHelper.Run(cmd);
        if (string.IsNullOrWhiteSpace(switchName))
            throw new InvalidOperationException($"Failed to resolve VM switch name for Id: {vmSwitchGuid}");

        _logger.LogInformation("Resolved VM switch: {SwitchId} -> {SwitchName}", vmSwitchGuid, switchName);
        return switchName;
    }

    private void CreateVm(string instanceName, string vhdxPath, string switchName, int generation, string memoryStr)
    {
        string cmd = $@"
New-VM -Name '{instanceName}' `
    -Generation {generation} `
    -MemoryStartupBytes {memoryStr} `
    -VHDPath '{vhdxPath}' `
    -SwitchName '{switchName}' | Out-Null
";
        RunPowershellCommand("New-VM", cmd);
    }

    private void SetVmCpuCount(string instanceName, string cpuCount)
    {
        // Defensive: Only proceed if string is not null/empty and > 0
        if (string.IsNullOrWhiteSpace(cpuCount) || cpuCount == "0")
            return;

        string cmd = $@"
Set-VMProcessor -VMName '{instanceName}' -Count {cpuCount} | Out-Null
";
        RunPowershellCommand("Set-VMProcessor", cmd);
        _logger.LogInformation("Set VM '{InstanceName}' CPU count to {CpuCount}", instanceName, cpuCount);
    }

    private void ConfigureSecureBoot(string instanceName)
    {
        string cmd = $@"
Set-VMFirmware -VMName '{instanceName}' `
    -EnableSecureBoot On `
    -SecureBootTemplate 'MicrosoftUEFICertificateAuthority' | Out-Null
";
        RunPowershellCommand("Set-VMFirmware", cmd);
    }

    private void EnsureDvdDriveExists(string instanceName)
    {
        string cmd = $@"
$dvd = Get-VMDvdDrive -VMName '{instanceName}' -ErrorAction SilentlyContinue
if (-not $dvd) {{
    Add-VMDvdDrive -VMName '{instanceName}' | Out-Null
}}
";
        RunPowershellCommand("Ensure DVD drive exists", cmd);
    }

    private void AttachIso(string instanceName, string isoPath)
    {
        string cmd = $@"
Set-VMDvdDrive -VMName '{instanceName}' -Path '{isoPath}' | Out-Null
";
        RunPowershellCommand("Set-VMDvdDrive", cmd);
    }

    private Vm FetchProvisionedVm(string instanceName)
    {
        var vm = _vmHelper.GetVmByName(instanceName) ?? throw new InvalidOperationException($"Provisioned VM '{instanceName}' not found.");
        _logger.LogInformation("VM provisioning completed successfully: {InstanceName}", instanceName);
        return vm;
    }

    private void RunPowershellCommand(string description, string cmd)
    {
        _logger.LogInformation("Running PowerShell {Description} command:\n{Command}", description, cmd.Trim());
        string output = PowerShellHelper.Run(cmd);
        if (!string.IsNullOrWhiteSpace(output))
        {
            _logger.LogInformation("{Description} output:\n{Output}", description, output);
        }
    }

    private string RunPowershellAndCaptureOutput(string description, string cmd)
    {
        _logger.LogInformation("Running PowerShell (capture) {Description} command:\n{Command}", description, cmd.Trim());
        return PowerShellHelper.Run(cmd).Trim();
    }
}
