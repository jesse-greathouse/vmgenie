using System;
using System.Collections.Generic;

using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;

namespace VmGenie.HyperV;

/// <summary>
/// Repository for querying Hyper‑V virtual switches.
/// </summary>
public class VmSwitchRepository
{
    private readonly CimSession _session;

    public VmSwitchRepository()
    {
        var options = new DComSessionOptions
        {
            Impersonation = ImpersonationType.Impersonate
        };

        _session = CimSession.Create(null, options);
    }

    public List<VmSwitch> GetAll()
    {
        var switches = new List<VmSwitch>();

        try
        {
            var instances = _session.QueryInstances(
                "root/virtualization/v2",
                "WQL",
                "SELECT * FROM Msvm_VirtualEthernetSwitch");

            foreach (var instance in instances)
            {
                switches.Add(VmSwitch.FromCimInstance(instance));
            }
        }
        catch (CimException ex)
        {
            throw new InvalidOperationException("Failed to query Hyper-V virtual switches. Ensure Hyper-V is installed and WMI is functional.", ex);
        }

        return switches;
    }
}
