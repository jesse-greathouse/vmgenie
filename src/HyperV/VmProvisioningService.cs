using System;
using System.IO;

using Microsoft.Extensions.Logging;

namespace VmGenie.HyperV;

public class VmProvisioningService(
    VmHelper vmHelper,
    VhdxManager vhdxManager,
    ILogger<VmProvisioningService> logger,
    Config config)
{
    private readonly VmHelper _vmHelper = vmHelper ?? throw new ArgumentNullException(nameof(vmHelper));
    private readonly VhdxManager _vhdxManager = vhdxManager ?? throw new ArgumentNullException(nameof(vhdxManager));
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
        var vm = _vmHelper.GetVmByName(instanceName);
        if (vm == null)
            throw new InvalidOperationException($"Provisioned VM '{instanceName}' not found.");

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
