using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;

namespace VmGenie.HyperV;

/// <summary>
/// Repository for querying Hyper‑V virtual switches.
/// </summary>
public class VmSwitchRepository
{
    private readonly CimSession _session;
    private readonly ILogger<VmSwitchRepository> _logger;

    public VmSwitchRepository(ILogger<VmSwitchRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var options = new DComSessionOptions
        {
            Impersonation = ImpersonationType.Impersonate
        };

        _session = CimSession.Create(null, options);
        _logger.LogInformation("VmSwitchRepository initialized with DCOM session.");
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
                var sw = VmSwitch.FromCimInstance(instance);
                _logger.LogInformation("Found switch: {Name} [{Id}]", sw.Name, sw.Id);
                switches.Add(sw);
            }
        }
        catch (CimException ex)
        {
            _logger.LogError(ex, "Failed to query Hyper-V virtual switches.");
            throw new InvalidOperationException(
                "Failed to query Hyper-V virtual switches. Ensure Hyper-V is installed and WMI is functional.", ex);
        }

        _logger.LogInformation("Total switches found: {Count}", switches.Count);
        return switches;
    }
}
