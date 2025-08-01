using System;
using System.Linq;
using System.Threading;

using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;

namespace VmGenie.HyperV;

public class VmLifecycleService(VmHelper vmHelper, ILogger<VmLifecycleService> logger)
{
    private readonly VmHelper _vmHelper = vmHelper ?? throw new ArgumentNullException(nameof(vmHelper));
    private readonly ILogger<VmLifecycleService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    public const int NetworkReadyState = 9999;

    public static bool IsRunning(string vmGuid) =>
        GetCurrentState(vmGuid) == VmState.Running;

    public static bool IsOff(string vmGuid) =>
        GetCurrentState(vmGuid) == VmState.Off;

    public static bool IsPaused(string vmGuid) =>
        GetCurrentState(vmGuid) == VmState.Paused;

    public static bool IsSuspended(string vmGuid) =>
        GetCurrentState(vmGuid) == VmState.Suspended;

    public static bool IsShuttingDown(string vmGuid) =>
        GetCurrentState(vmGuid) == VmState.ShuttingDown;

    public static bool IsStarting(string vmGuid) =>
        GetCurrentState(vmGuid) == VmState.Starting;

    public static bool IsSnapshotting(string vmGuid) =>
        GetCurrentState(vmGuid) == VmState.Snapshotting;

    public static bool IsSaving(string vmGuid) =>
        GetCurrentState(vmGuid) == VmState.Saving;

    public static bool IsStopping(string vmGuid) =>
        GetCurrentState(vmGuid) == VmState.Stopping;

    public void Start(string vmGuid) =>
        ChangeVmState(vmGuid, VmState.Running);

    public void Stop(string vmGuid) =>
        ChangeVmState(vmGuid, VmState.Off);

    public void Pause(string vmGuid) =>
        ChangeVmState(vmGuid, VmState.Paused);

    public void Resume(string vmGuid) =>
        ChangeVmState(vmGuid, VmState.Running);

    public void Shutdown(string vmGuid)
    {
        const string ns = @"root\virtualization\v2";
        using var session = CimSession.Create(null);

        // find the VM
        var vm = session.QueryInstances(ns, "WQL",
            $"SELECT * FROM Msvm_ComputerSystem WHERE Name='{vmGuid}'")
            .FirstOrDefault() ?? throw new InvalidOperationException($"VM with GUID {vmGuid} not found.");

        // find the shutdown component associated with the VM
        var shutdownComponent = session.QueryInstances(ns, "WQL",
            $"SELECT * FROM Msvm_ShutdownComponent WHERE SystemName='{vmGuid}'")
            .FirstOrDefault();

        if (shutdownComponent is null)
        {
            _logger.LogWarning("Graceful shutdown requested, but VM {VmGuid} has no shutdown component (integration services missing?). Falling back to hard stop.", vmGuid);
            ChangeVmState(vmGuid, VmState.Off); // fallback to hard stop
            return;
        }

        var result = session.InvokeMethod(shutdownComponent, "InitiateShutdown", new CimMethodParametersCollection
        {
            CimMethodParameter.Create("Force", false, CimType.Boolean, CimFlags.None),
            CimMethodParameter.Create("Reason", "Shutdown requested by VmGenie", CimType.String, CimFlags.None)
        });

        if (result.ReturnValue?.Value is uint ret)
        {
            if (ret == 0)
            {
                _logger.LogInformation("Graceful shutdown initiated successfully for VM {VmGuid}.", vmGuid);
                return;
            }
            else
            {
                _logger.LogWarning("Graceful shutdown returned code {Code} for VM {VmGuid}.", ret, vmGuid);
            }
        }

        // if it reaches here, fallback
        _logger.LogWarning("Graceful shutdown failed for VM {VmGuid}. Falling back to hard stop.", vmGuid);
        ChangeVmState(vmGuid, VmState.Off);
    }

    public static bool IsNetworkReady(string vmGuid)
    {
        if (string.IsNullOrWhiteSpace(vmGuid))
            throw new ArgumentNullException(nameof(vmGuid));

        string psCommand = $@"
$vm = Get-VM | Where-Object {{ $_.Id -eq '{vmGuid}' }}
if (-not $vm) {{ exit 1 }}

$net = Get-VMNetworkAdapter -VM $vm
if (-not $net) {{ exit 1 }}

$ipv4 = $net.IPAddresses | Where-Object {{ $_ -and ($_ -match '^\d{{1,3}}(\.\d{{1,3}}){{3}}$') -and -not $_.StartsWith('169.') }}
if ($ipv4) {{ exit 0 }} else {{ exit 2 }}
";

        var (_, stderr, exitCode) = PowerShellHelper.RunSafe(psCommand);

        return exitCode switch
        {
            0 => true, // IP assigned
            1 => throw new InvalidOperationException($"VM with GUID {vmGuid} not found or has no network adapter. Details: {stderr}"),
            2 => false, // no IP assigned yet
            _ => throw new InvalidOperationException($"Unexpected PowerShell error (exit {exitCode}): {stderr}"),
        };
    }

    public static VmState GetCurrentState(string vmGuid)
    {
        const string ns = @"root\virtualization\v2";
        using var session = CimSession.Create(null);

        var vm = session.QueryInstances(ns, "WQL",
            $"SELECT * FROM Msvm_ComputerSystem WHERE Name='{vmGuid}'")
            .FirstOrDefault() ?? throw new InvalidOperationException($"VM with GUID {vmGuid} not found.");

        return (VmState)(ushort)(vm.CimInstanceProperties["EnabledState"].Value ?? 0);
    }

    /// <summary>
    /// Deletes a VM from Hyper-V by GUID. Powers off the VM if necessary.
    /// </summary>
    public void Delete(string vmGuid, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(vmGuid))
            throw new ArgumentNullException(nameof(vmGuid));

        // Lookup VM (throws if not found)
        var vm = _vmHelper.GetVm(vmGuid);

        // 1. If the VM is running (or not off), stop it first.
        var state = GetCurrentState(vmGuid);
        if (state != VmState.Off)
        {
            _logger.LogInformation("Stopping VM {Name} [{Guid}] before deletion. Current state: {State}", vm.Name, vmGuid, state);
            try
            {
                // Try graceful shutdown, then fallback to Stop if not Off
                Shutdown(vmGuid);

                // Wait for shutdown (polling, or immediate if state changes quickly)
                var timeout = TimeSpan.FromSeconds(20);
                var stopTime = DateTime.Now + timeout;
                while (GetCurrentState(vmGuid) != VmState.Off && DateTime.Now < stopTime)
                {
                    Thread.Sleep(500);
                }

                if (GetCurrentState(vmGuid) != VmState.Off)
                {
                    _logger.LogWarning("VM {Name} [{Guid}] did not shut down gracefully. Forcing power off.", vm.Name, vmGuid);
                    Stop(vmGuid);

                    // Wait again for power off
                    stopTime = DateTime.Now + timeout;
                    while (GetCurrentState(vmGuid) != VmState.Off && DateTime.Now < stopTime)
                    {
                        Thread.Sleep(500);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while stopping VM {Name} [{Guid}] before deletion.", vm.Name, vmGuid);
                if (!force)
                    throw;
            }
        }

        // 2. Remove the VM from Hyper-V using PowerShell (correct usage: Get-VM -Id ... | Remove-VM -Force)
        try
        {
            string removeVmCmd = $"Get-VM -Id '{vmGuid}' | Remove-VM -Force";
            PowerShellHelper.Run(removeVmCmd);
            _logger.LogInformation("Deleted VM {Name} [{Guid}] from Hyper-V.", vm.Name, vmGuid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove VM {Name} [{Guid}] from Hyper-V.", vm.Name, vmGuid);
            if (!force)
                throw;
        }
    }

    private void ChangeVmState(string vmGuid, VmState requestedState)
    {
        const string ns = @"root\virtualization\v2";
        using var session = CimSession.Create(null);

        var vm = session.QueryInstances(ns, "WQL",
            $"SELECT * FROM Msvm_ComputerSystem WHERE Name='{vmGuid}'")
            .FirstOrDefault() ?? throw new InvalidOperationException($"VM with GUID {vmGuid} not found.");

        var inParams = new CimMethodParametersCollection
        {
            CimMethodParameter.Create("RequestedState", (ushort)requestedState, CimType.UInt16, CimFlags.None)
        };

        var result = session.InvokeMethod(vm, "RequestStateChange", inParams);

        if (result.ReturnValue?.Value is uint ret)
        {
            if (ret == 0)
            {
                _logger.LogInformation("VM {VmGuid} transitioned to {State} synchronously.", vmGuid, requestedState);
                return;
            }
            else if (ret == 4096)
            {
                _logger.LogInformation("VM {VmGuid} state change to {State} started asynchronously.", vmGuid, requestedState);
                _vmHelper.WaitForJobCompletion(session, (CimInstance)result.OutParameters["Job"].Value);
                return;
            }
            else
            {
                throw new InvalidOperationException($"Failed to change VM state. Error code: {ret}");
            }
        }

        throw new InvalidOperationException("Failed to get return code from RequestStateChange.");
    }
}
