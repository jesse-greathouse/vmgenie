using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;

namespace VmGenie.HyperV;

public class VmHelpers(VmRepository repo, ILogger<VmHelpers> logger)
{
    private readonly VmRepository _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    private readonly ILogger<VmHelpers> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Dictionary<string, Vm> _vmCache = [];

    private enum JobState : ushort
    {
        New = 2,
        Starting = 3,
        Running = 4,
        Suspended = 5,
        ShuttingDown = 6,
        Completed = 7,
        Terminated = 8,
        Killed = 9,
        Exception = 10,
        Service = 11
    }

    public string CloneBaseVhdx(string baseVmGuid, string instanceName, bool mergeDifferencingDisk = false)
    {
        if (string.IsNullOrWhiteSpace(baseVmGuid))
            throw new ArgumentNullException(nameof(baseVmGuid));
        if (string.IsNullOrWhiteSpace(instanceName))
            throw new ArgumentNullException(nameof(instanceName));

        var baseVm = GetVm(baseVmGuid);

        string baseVhdxPath = GetVhdxPathForVm(baseVmGuid);
        _logger.LogInformation("Cloning base VM '{Name}' [{Guid}] with VHDX: {Path}", baseVm.Name, baseVm.Id, baseVhdxPath);

        string baseDir = System.IO.Path.GetDirectoryName(baseVhdxPath)
            ?? throw new InvalidOperationException($"Could not determine directory of base VHDX: {baseVhdxPath}");

        string newVhdxName = $"{instanceName}.vhdx";
        string newVhdxPath = System.IO.Path.Combine(baseDir, newVhdxName);

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
                _vmCache.Remove(baseVmGuid);

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
            System.IO.File.Copy(baseVhdxPath, newVhdxPath);
        }

        _logger.LogInformation("Successfully created new VHDX: {NewVhdx}", newVhdxPath);
        return newVhdxPath;
    }

    public bool IsDifferencingDisk(string vmGuid)
    {
        string vhdxPath = GetVhdxPathForVm(vmGuid);
        return vhdxPath.EndsWith(".avhdx", StringComparison.OrdinalIgnoreCase);
    }

    private string MergeAvhdx(string sourceAvhdx)
    {
        string parentVhdx = GetParentVhdx(sourceAvhdx);

        _logger.LogInformation("Merging {Avhdx} into parent {Parent}", sourceAvhdx, parentVhdx);

        if (!System.IO.File.Exists(parentVhdx))
            throw new InvalidOperationException($"Parent VHDX not found for merge: {parentVhdx}");

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"Merge-VHD -Path '{sourceAvhdx}' -DestinationPath '{parentVhdx}' -Confirm:$false\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start PowerShell process for Merge-VHD.");

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            _logger.LogError("Merge-VHD failed: {Error}", error);
            throw new InvalidOperationException($"Merge-VHD failed: {error}");
        }

        _logger.LogInformation("Merge-VHD succeeded: {Output}", output);
        return parentVhdx;
    }

    private void FixVmConfigurationAfterMerge(string vmGuid, string parentVhdx)
    {
        const string ns = @"root\virtualization\v2";
        var vm = GetVm(vmGuid);

        using var session = CimSession.Create(null);

        _logger.LogInformation("Reconfiguring VM '{Name}' [{Guid}] to point at merged parent VHDX: {ParentVhdx}",
            vm.Name, vm.Id, parentVhdx);

        // Get the VM’s settings
        var vmSettings = session.QueryInstances(ns, "WQL",
            $"SELECT * FROM Msvm_VirtualSystemSettingData WHERE InstanceID LIKE '%{vm.Id}%' AND SettingType = 3")
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Failed to retrieve VM settings for {vm.Id}");

        // Find the disk drive resource
        var diskResource = session.EnumerateAssociatedInstances(
            ns, vmSettings,
            "Msvm_VirtualSystemSettingData",
            "Msvm_ResourceAllocationSettingData", "Dependent", "Antecedent")
            .FirstOrDefault(r =>
                r.CimInstanceProperties["ResourceSubType"].Value?.ToString()?.Contains("Disk") == true &&
                r.CimInstanceProperties["HostResource"] is CimProperty hr &&
                hr.Value is string[] paths &&
                paths.Length > 0 &&
                paths[0].EndsWith(".avhdx", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Failed to locate VM disk resource referencing .avhdx");

        diskResource.CimInstanceProperties["HostResource"].Value = new[] { parentVhdx };

        var service = session.QueryInstances(ns, "WQL", "SELECT * FROM Msvm_VirtualSystemManagementService")
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Failed to locate VirtualSystemManagementService");

        var inParams = new CimMethodParametersCollection
        {
            CimMethodParameter.Create("ResourceSettings", new[] { diskResource.ToString() }, CimType.StringArray, CimFlags.None)
        };

        var result = session.InvokeMethod(service, "ModifyResourceSettings", inParams);

        uint returnValue = result.ReturnValue?.Value as uint?
            ?? throw new InvalidOperationException("Failed to obtain return value from ModifyResourceSettings.");

        if (returnValue != 0)
        {
            throw new InvalidOperationException($"Failed to modify VM disk resource. Error code: {returnValue}");
        }

        _logger.LogInformation("VM '{Name}' [{Guid}] successfully reconfigured to use parent VHDX.", vm.Name, vm.Id);
    }

    public void ConvertVhd(string baseVhdx, string newVhdx)
    {
        const string ns = @"root\virtualization\v2";

        if (!System.IO.File.Exists(baseVhdx))
            throw new InvalidOperationException($"Source VHDX does not exist: {baseVhdx}");
        if (System.IO.File.Exists(newVhdx))
            throw new InvalidOperationException($"Destination file already exists: {newVhdx}");

        _logger.LogInformation("Starting VHDX conversion: {Source} → {Destination}", baseVhdx, newVhdx);

        using var session = CimSession.Create(null);
        var service = session.QueryInstances(ns, "WQL", "SELECT * FROM Msvm_ImageManagementService").FirstOrDefault()
            ?? throw new InvalidOperationException("Failed to locate Msvm_ImageManagementService.");

        var parameters = new CimMethodParametersCollection
        {
            CimMethodParameter.Create("SourcePath", baseVhdx, CimType.String, CimFlags.None),
            CimMethodParameter.Create("DestinationPath", newVhdx, CimType.String, CimFlags.None),
            CimMethodParameter.Create("Type", (ushort)3, CimType.UInt16, CimFlags.None)
        };

        _logger.LogInformation("Invoking ConvertVirtualHardDisk on {Source} → {Destination}", baseVhdx, newVhdx);

        var result = session.InvokeMethod(service, "ConvertVirtualHardDisk", parameters);
        if (result.ReturnValue?.Value is not uint returnValue)
            throw new InvalidOperationException("Failed to obtain return value from ConvertVirtualHardDisk.");

        _logger.LogDebug("ConvertVirtualHardDisk returned code: {Code}", returnValue);

        if (returnValue == 0)
        {
            _logger.LogInformation("VHDX conversion completed synchronously.");
            return;
        }
        else if (returnValue == 4096)
        {
            var jobProp = result.OutParameters["Job"];
            if (jobProp?.Value is not CimInstance job)
                throw new InvalidOperationException("Job object was null despite return code 4096.");

            _logger.LogInformation("Conversion started as asynchronous job: {JobPath}", job.CimSystemProperties.Path);
            WaitForJobCompletion(session, job);
        }
        else
        {
            throw new InvalidOperationException($"ConvertVirtualHardDisk failed immediately with error code {returnValue}");
        }
    }

    private void WaitForJobCompletion(CimSession session, CimInstance job)
    {
        const string ns = @"root\virtualization\v2";

        while (true)
        {
            var currentJob = session.GetInstance(ns, job);
            var jobState = (JobState)(ushort)currentJob.CimInstanceProperties["JobState"].Value;

            if (jobState == JobState.Completed)
            {
                _logger.LogInformation("Job completed successfully.");
                return;
            }
            else if (jobState == JobState.Terminated || jobState == JobState.Exception)
            {
                throw new InvalidOperationException($"Job failed with state {jobState}");
            }

            _logger.LogDebug("Job state: {JobState}, waiting…", jobState);
            Thread.Sleep(500);
        }
    }

    private string GetVhdxPathForVm(string vmGuid)
    {
        _logger.LogInformation("Resolving VHDX path for VM GUID: {VmGuid}", vmGuid);

        var vm = GetVm(vmGuid);

        if (string.IsNullOrWhiteSpace(vm.HostResourcePath))
            throw new InvalidOperationException($"VM '{vm.Name}' has no associated VHDX path.");

        _logger.LogInformation("Resolved VHDX path: {Path}", vm.HostResourcePath);

        return vm.HostResourcePath;
    }

    private Vm GetVm(string vmGuid)
    {
        if (_vmCache.TryGetValue(vmGuid, out var vm))
        {
            _logger.LogDebug("VM {VmGuid} retrieved from cache.", vmGuid);
            return vm;
        }

        vm = _repo.GetById(vmGuid)
            ?? throw new InvalidOperationException($"VM with GUID '{vmGuid}' not found in Hyper-V.");

        _vmCache[vmGuid] = vm;
        _logger.LogDebug("VM {VmGuid} retrieved from repository and cached.", vmGuid);

        return vm;
    }

    static string GetParentVhdx(string avhdxPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"(Get-VHD -Path '{avhdxPath}').ParentPath\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start PowerShell to query parent path.");
        string output = process.StandardOutput.ReadToEnd().Trim();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException($"Failed to retrieve ParentPath of {avhdxPath}: {error}");
        }

        return output;
    }
}
