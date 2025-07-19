using System;
using System.Collections.Generic;
using System.Threading;

using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;

namespace VmGenie.HyperV;

public class VmHelper(VmRepository repo, ILogger<VmHelper> logger)
{
    private readonly VmRepository _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    private readonly ILogger<VmHelper> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Dictionary<string, Vm> _vmCache = [];

    public Vm GetVm(string vmGuid)
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

    public Vm? GetVmByName(string name)
    {
        var vms = _repo.GetAllByName(name);

        if (vms.Count == 0)
        {
            _logger.LogDebug("No VM found with name '{Name}'.", name);
            return null;
        }

        if (vms.Count > 1)
            _logger.LogWarning("Multiple VMs found with name '{Name}'. Returning the first. Count={Count}", name, vms.Count);

        var vm = vms[0];
        _logger.LogDebug("VM {VmName} retrieved from repository.", name);

        return vm;
    }

    public string GetVhdxPathForVm(string vmGuid)
    {
        var vm = GetVm(vmGuid);

        if (string.IsNullOrWhiteSpace(vm.HostResourcePath))
            throw new InvalidOperationException($"VM '{vm.Name}' has no associated VHDX path.");

        _logger.LogInformation("Resolved VHDX path for VM {VmGuid}: {Path}", vmGuid, vm.HostResourcePath);

        return vm.HostResourcePath;
    }

    /// <summary>
    /// Removes a VM from the internal cache, if present.
    /// </summary>
    public void RemoveFromCache(string vmGuid)
    {
        if (_vmCache.Remove(vmGuid))
        {
            _logger.LogDebug("VM {VmGuid} removed from cache.", vmGuid);
        }
        else
        {
            _logger.LogDebug("VM {VmGuid} not found in cache (nothing to remove).", vmGuid);
        }
    }

    public void WaitForJobCompletion(CimSession session, CimInstance job)
    {
        const string ns = @"root\virtualization\v2";

        if (job == null)
            throw new ArgumentNullException(nameof(job), "Job instance is null.");

        var jobPath = job.CimSystemProperties.Path?.ToString() ?? "(null)";
        _logger.LogDebug("Waiting for job: {JobPath}", jobPath);

        while (true)
        {
            var currentJob = session.GetInstance(ns, job);
            var jobState = (JobState)(ushort)currentJob.CimInstanceProperties["JobState"].Value;

            _logger.LogDebug("Job state: {JobState}", jobState);

            if (jobState == JobState.Completed)
            {
                _logger.LogInformation("Job completed successfully.");
                return;
            }

            if (jobState == JobState.Terminated || jobState == JobState.Exception)
            {
                string errorDescription = currentJob.CimInstanceProperties["ErrorDescription"]?.Value?.ToString() ?? "(none)";
                uint? errorCode = currentJob.CimInstanceProperties["ErrorCode"]?.Value as uint?;

                _logger.LogError("Job failed: State={State}, ErrorDescription={Description}, ErrorCode={Code}, JobPath={JobPath}",
                    jobState, errorDescription, errorCode?.ToString() ?? "(none)", jobPath);

                // Optionally: dump all properties for inspection
                foreach (var prop in currentJob.CimInstanceProperties)
                {
                    _logger.LogError("Job Property: {Name} = {Value}", prop.Name, prop.Value);
                }

                throw new InvalidOperationException(
                    $"Job failed with state {jobState}. ErrorDescription: {errorDescription}, ErrorCode: {errorCode}");
            }

            Thread.Sleep(500);
        }
    }
}
