using System;
using System.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;

namespace VmGenie.HyperV;

public class VmLifecycleService(VmHelper vmHelper, ILogger<VmLifecycleService> logger)
{
    private readonly VmHelper _vmHelper = vmHelper ?? throw new ArgumentNullException(nameof(vmHelper));
    private readonly ILogger<VmLifecycleService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public bool IsRunning(string vmGuid)
    {
        const string ns = @"root\virtualization\v2";
        using var session = CimSession.Create(null);

        var vm = session.QueryInstances(ns, "WQL",
            $"SELECT * FROM Msvm_ComputerSystem WHERE Name='{vmGuid}'")
            .FirstOrDefault() ?? throw new InvalidOperationException($"VM with GUID {vmGuid} not found.");

        var state = (VmState)(ushort)(vm.CimInstanceProperties["EnabledState"].Value ?? 0);

        _logger.LogInformation("VM {VmGuid} is currently in state: {State}", vmGuid, state);

        return state == VmState.Running;
    }

    public void Start(string vmGuid)
    {
        ChangeVmState(vmGuid, VmState.Running);
    }

    public void Stop(string vmGuid)
    {
        ChangeVmState(vmGuid, VmState.Off);
    }

    private void ChangeVmState(string vmGuid, VmState requestedState)
    {
        const string ns = @"root\virtualization\v2";
        using var session = CimSession.Create(null);

        var vm = session.QueryInstances(ns, "WQL",
            $"SELECT * FROM Msvm_ComputerSystem WHERE Name='{vmGuid}'")
            .FirstOrDefault() ?? throw new InvalidOperationException($"VM with GUID {vmGuid} not found.");

        var mgmtService = session.QueryInstances(ns, "WQL", "SELECT * FROM Msvm_VirtualSystemManagementService")
            .FirstOrDefault() ?? throw new InvalidOperationException("Failed to locate VirtualSystemManagementService.");

        var inParams = new CimMethodParametersCollection
    {
        CimMethodParameter.Create("RequestedState", (ushort)requestedState, CimType.UInt16, CimFlags.None),
        CimMethodParameter.Create("ComputerSystem", vm, CimType.Reference, CimFlags.None)
    };

        var result = session.InvokeMethod(mgmtService, "RequestStateChange", inParams);

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
