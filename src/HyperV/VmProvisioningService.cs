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

    public Vm ProvisionVm(string baseVmGuid, string instanceName, string vmSwitchGuid, bool mergeDifferencingDisk = false, int generation = 2)
    {
        EnsureVmDoesNotExist(instanceName);
        _logger.LogInformation(
            "Starting VM provisioning: BaseVM={BaseGuid}, Instance={InstanceName}, Switch={SwitchGuid}, MergeDiff={Merge}",
            baseVmGuid, instanceName, vmSwitchGuid, mergeDifferencingDisk);

        string artifactDir = Path.Combine(_config.CloudDir, instanceName);
        string isoPath = GetIsoPath(artifactDir);
        string clonedVhdx = _vhdxManager.CloneBaseVhdx(baseVmGuid, instanceName, mergeDifferencingDisk);
        string switchName = ResolveSwitchName(vmSwitchGuid);

        _logger.LogInformation("Provisioning VM '{InstanceName}' using PowerShell.", instanceName);

        try
        {
            CreateVm(instanceName, clonedVhdx, switchName, generation);
            if (generation == 2) ConfigureSecureBoot(instanceName);
            EnsureDvdDriveExists(instanceName);
            AttachIso(instanceName, isoPath);
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

        _logger.LogInformation(
            "Beginning VM import: InstanceName={InstanceName}, ImportLocation={Location}, Mode={Mode}",
            export.InstanceName, location, copy ? "Copy (new ID)" : "Restore (existing ID)");

        // Defensive: If VM with same name exists and we're not copying, enforce safety checks before deletion
        if (!copy)
        {
            var existingVm = _vmHelper.GetVmByName(export.InstanceName);
            if (existingVm != null)
            {
                if (VmLifecycleService.IsRunning(existingVm.Id))
                {
                    throw new InvalidOperationException($"Refusing to restore: VM '{export.InstanceName}' [{existingVm.Id}] is currently running. Please shut down the VM before restoring.");
                }

                _logger.LogWarning("Deleting existing .vhdx virtual hard drive for '{InstanceName}'", export.InstanceName);
                _vhdxManager.DeleteVhdx(existingVm.Id);

                _logger.LogWarning("VM with name '{InstanceName}' already exists and is not running. Deleting before restore...", export.InstanceName);
                _vmLifecycleService.Delete(existingVm.Id, force: true);
            }
        }

        string vmcxFile = Path.Combine(location, "Virtual Machines", $"{export.InstanceId}.vmcx");
        if (!File.Exists(vmcxFile))
            throw new FileNotFoundException($"Could not find vmcx file at {vmcxFile}");

        // Pass the extracted folder (with Virtual Machines, etc) as the path
        string importModeArgs = copy
            ? "-Copy -GenerateNewId"
            : "-Copy -GenerateNewId:$false";
        string cmd = $@"
Import-VM -Path '{vmcxFile}' {importModeArgs} | Out-Null
";

        try
        {
            RunPowershellCommand("Import-VM", cmd);

            var importedVm = _vmHelper.GetVmByName(export.InstanceName)
                ?? throw new InvalidOperationException($"VM import succeeded but VM '{export.InstanceName}' not found in Hyper-V.");

            _logger.LogInformation(
                "VM import completed successfully: {InstanceName} (Mode: {Mode})",
                export.InstanceName, copy ? "Copy" : "Restore");

            return importedVm;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to import VM '{InstanceName}' from '{Location}'.",
                export.InstanceName, location);
            throw;
        }
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

    private void CreateVm(string instanceName, string vhdxPath, string switchName, int generation)
    {
        string cmd = $@"
New-VM -Name '{instanceName}' `
    -Generation {generation} `
    -MemoryStartupBytes 512MB `
    -VHDPath '{vhdxPath}' `
    -SwitchName '{switchName}' | Out-Null
";
        RunPowershellCommand("New-VM", cmd);
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
}
