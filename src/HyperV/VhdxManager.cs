using System;
using System.IO;

using Microsoft.Extensions.Logging;

namespace VmGenie.HyperV;

public class VhdxManager(VmHelper vmHelper, ILogger<VhdxManager> logger)
{
    private readonly VmHelper _vmHelper = vmHelper ?? throw new ArgumentNullException(nameof(vmHelper));
    private readonly ILogger<VhdxManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public string CloneBaseVhdx(string baseVmGuid, string instanceName, bool mergeDifferencingDisk = false)
    {
        if (string.IsNullOrWhiteSpace(baseVmGuid))
            throw new ArgumentNullException(nameof(baseVmGuid));
        if (string.IsNullOrWhiteSpace(instanceName))
            throw new ArgumentNullException(nameof(instanceName));

        var baseVm = _vmHelper.GetVm(baseVmGuid);

        string baseVhdxPath = _vmHelper.GetVhdxPathForVm(baseVmGuid);
        _logger.LogInformation("Cloning base VM '{Name}' [{Guid}] with VHDX: {Path}", baseVm.Name, baseVm.Id, baseVhdxPath);

        string baseDir = Path.GetDirectoryName(baseVhdxPath)
            ?? throw new InvalidOperationException($"Could not determine directory of base VHDX: {baseVhdxPath}");

        string newVhdxName = $"{instanceName}.vhdx";
        string newVhdxPath = Path.Combine(baseDir, newVhdxName);

        _logger.LogInformation("Preparing to clone base VHDX '{BaseVhdx}' to '{NewVhdx}'", baseVhdxPath, newVhdxPath);

        if (IsDifferencingDisk(baseVmGuid))
        {
            _logger.LogInformation("Base VM is using a differencing disk (.avhdx)");

            if (mergeDifferencingDisk)
            {
                _logger.LogInformation("Merging differencing disk into parent before conversion.");
                string mergedVhdx = MergeAvhdx(baseVhdxPath);
                FixVmConfigurationAfterMerge(baseVmGuid, mergedVhdx);

                _logger.LogDebug("Invalidating cached VM {VmGuid} after merge and reconfiguration.", baseVmGuid);
                _vmHelper.RemoveFromCache(baseVmGuid);

                ConvertVhd(mergedVhdx, newVhdxPath);
            }
            else
            {
                _logger.LogInformation("Resolving parent VHDX and cloning from that.");
                string parentVhdx = GetParentVhdx(baseVhdxPath);
                ConvertVhd(parentVhdx, newVhdxPath);
            }
        }
        else
        {
            _logger.LogInformation("Base VHDX is a standard .vhdx. Performing file copy.");
            File.Copy(baseVhdxPath, newVhdxPath);
        }

        _logger.LogInformation("Successfully created new VHDX: {NewVhdx}", newVhdxPath);
        return newVhdxPath;
    }

    public void ConvertVhd(string baseVhdx, string newVhdx)
    {
        if (!File.Exists(baseVhdx))
            throw new InvalidOperationException($"Source VHDX does not exist: {baseVhdx}");
        if (File.Exists(newVhdx))
            throw new InvalidOperationException($"Destination file already exists: {newVhdx}");

        _logger.LogInformation("Starting VHDX conversion: {Source} → {Destination}", baseVhdx, newVhdx);

        string command =
            $"Convert-VHD -Path '{baseVhdx}' -DestinationPath '{newVhdx}' -VHDType Dynamic";

        _logger.LogInformation("Invoking PowerShell Convert-VHD on {Source} → {Destination}", baseVhdx, newVhdx);

        var (stdout, stderr, exitCode) = PowerShellHelper.RunSafe(command);

        if (exitCode != 0)
        {
            _logger.LogError("Convert-VHD failed: {Error}", stderr);
            throw new InvalidOperationException($"PowerShell Convert-VHD failed: {stderr}");
        }

        _logger.LogInformation("VHDX conversion completed successfully. Output: {Output}", stdout);
    }

    public bool IsDifferencingDisk(string vmGuid)
    {
        string vhdxPath = _vmHelper.GetVhdxPathForVm(vmGuid);
        return vhdxPath.EndsWith(".avhdx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Deletes the primary VHDX (or AVHDX) disk associated with the given VM GUID.
    /// If the disk does not exist, logs and returns gracefully.
    /// </summary>
    public void DeleteVhdx(string vmGuid)
    {
        if (string.IsNullOrWhiteSpace(vmGuid))
            throw new ArgumentNullException(nameof(vmGuid));

        var vm = _vmHelper.GetVm(vmGuid);

        if (string.IsNullOrWhiteSpace(vm.HostResourcePath))
        {
            _logger.LogWarning("VM '{Name}' [{Guid}] does not have an associated VHDX path to delete.", vm.Name, vm.Id);
            return;
        }

        string vhdxPath = vm.HostResourcePath;

        try
        {
            if (File.Exists(vhdxPath))
            {
                File.Delete(vhdxPath);
                _logger.LogInformation("Deleted VHDX for VM '{Name}' [{Guid}] at: {Path}", vm.Name, vm.Id, vhdxPath);
            }
            else
            {
                _logger.LogWarning("VHDX file not found for VM '{Name}' [{Guid}] at: {Path}. Nothing to delete.", vm.Name, vm.Id, vhdxPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete VHDX file for VM '{Name}' [{Guid}] at: {Path}", vm.Name, vm.Id, vhdxPath);
            throw;
        }
    }

    private static string GetParentVhdx(string avhdxPath)
    {
        string command = $"(Get-VHD -Path '{avhdxPath}').ParentPath";
        return PowerShellHelper.Run(command);
    }

    private string MergeAvhdx(string sourceAvhdx)
    {
        string parentVhdx = GetParentVhdx(sourceAvhdx);

        _logger.LogInformation("Merging {Avhdx} into parent {Parent}", sourceAvhdx, parentVhdx);

        if (!File.Exists(parentVhdx))
            throw new InvalidOperationException($"Parent VHDX not found for merge: {parentVhdx}");

        string command = $"Merge-VHD -Path '{sourceAvhdx}' -DestinationPath '{parentVhdx}' -Confirm:$false";
        var (stdout, stderr, exitCode) = PowerShellHelper.RunSafe(command);

        if (exitCode != 0)
        {
            _logger.LogError("Merge-VHD failed: {Error}", stderr);
            throw new InvalidOperationException($"Merge-VHD failed: {stderr}");
        }

        _logger.LogInformation("Merge-VHD succeeded: {Output}", stdout);
        return parentVhdx;
    }

    private void FixVmConfigurationAfterMerge(string vmGuid, string parentVhdx)
    {
        var vm = _vmHelper.GetVm(vmGuid);

        _logger.LogInformation(
            "Creating a checkpoint for VM '{Name}' [{Guid}] after merging to parent VHDX: {ParentVhdx}",
            vm.Name, vm.Id, parentVhdx);

        string snapshotName = string.Format("{0} - ({1})",
            vm.Name,
            DateTime.Now.ToString("M/d/yyyy - h:mm:ss tt"));

        string command =
            $"Remove-VMHardDiskDrive -VMName '{vm.Name}' -ControllerType SCSI -ControllerNumber 0 -ControllerLocation 0; " +
            $"Add-VMHardDiskDrive -VMName '{vm.Name}' -ControllerType SCSI -ControllerNumber 0 -ControllerLocation 0 -Path '{parentVhdx}'; " +
            $"Checkpoint-VM -VMName '{vm.Name}' -SnapshotName '{snapshotName}'";

        _logger.LogInformation("Executing PowerShell to set disk and create checkpoint…");

        var (stdout, stderr, exitCode) = PowerShellHelper.RunSafe(command);

        if (exitCode != 0)
        {
            _logger.LogError("Failed to create checkpoint for VM '{Name}' [{Guid}]. PowerShell error: {Error}",
                vm.Name, vm.Id, stderr);
            throw new InvalidOperationException($"PowerShell failed to reconfigure and checkpoint VM: {stderr}");
        }

        _logger.LogInformation(
            "Successfully created checkpoint for VM '{Name}' [{Guid}] after merge. Output: {Output}",
            vm.Name, vm.Id, stdout);
    }
}
