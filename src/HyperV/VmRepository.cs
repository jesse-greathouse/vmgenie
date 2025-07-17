using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;

using VmGenie.Artifacts;

namespace VmGenie.HyperV;

public class VmRepository
{
    private readonly CimSession _session;

    private readonly ILogger<VmRepository> _logger;

    private readonly InstanceRepository _instanceRepo;

    public VmRepository(ILogger<VmRepository> logger, InstanceRepository instanceRepo)
    {
        var options = new DComSessionOptions
        {
            Impersonation = ImpersonationType.Impersonate
        };

        _session = CimSession.Create(null, options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _instanceRepo = instanceRepo ?? throw new ArgumentNullException(nameof(instanceRepo));

        _logger.LogInformation("VmRepository initialized with DCOM session.");
    }

    public enum ProvisionedFilter
    {
        All,                // everything
        ExcludeProvisioned, // exclude provisioned
        OnlyProvisioned     // only provisioned
    }

    public List<Vm> GetAll(ProvisionedFilter filter = ProvisionedFilter.All)
    {
        var vms = new List<Vm>();
        var whereClause = "Caption = 'Virtual Machine'";

        if (filter == ProvisionedFilter.ExcludeProvisioned)
        {
            whereClause = BuildWhereClauseExcludingProvisioned(whereClause);
        }
        else if (filter == ProvisionedFilter.OnlyProvisioned)
        {
            whereClause = BuildWhereClauseIncludingProvisioned(whereClause);
        }

        var query = $"SELECT * FROM Msvm_ComputerSystem WHERE {whereClause}";
        _logger.LogDebug("WQL Query: {Query}", query);

        var systems = _session.QueryInstances(
            "root/virtualization/v2",
            "WQL",
            query
        );

        foreach (var system in systems)
        {
            var vm = Vm.FromCimInstance(_session, system, includeHostResourcePath: false);
            vms.Add(vm);
        }

        return vms;
    }

    public Vm? GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentNullException(nameof(id));

        var systems = _session.QueryInstances(
            "root/virtualization/v2",
            "WQL",
            $"SELECT * FROM Msvm_ComputerSystem WHERE Name = '{id.Replace("'", "''")}'"
        );

        foreach (var system in systems)
        {
            return Vm.FromCimInstance(_session, system);
        }

        return null;
    }

    public List<Vm> GetAllByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        var vms = new List<Vm>();

        var systems = _session.QueryInstances(
            "root/virtualization/v2",
            "WQL",
            $"SELECT * FROM Msvm_ComputerSystem WHERE ElementName = '{name.Replace("'", "''")}'"
        );

        foreach (var system in systems)
        {
            vms.Add(Vm.FromCimInstance(_session, system, includeHostResourcePath: false));
        }

        return vms;
    }

    private string BuildWhereClauseExcludingProvisioned(string baseWhereClause)
    {
        var provisionedNames = _instanceRepo.GetInstanceNames();

        if (provisionedNames.Count == 0)
        {
            _logger.LogInformation("No provisioned instances found — nothing to exclude.");
            return baseWhereClause;
        }

        _logger.LogInformation("Excluding provisioned instances from VM list: {Count} names", provisionedNames.Count);

        foreach (var name in provisionedNames)
        {
            var safeName = name.Replace("'", "''");
            baseWhereClause += $" AND ElementName != '{safeName}'";
        }

        return baseWhereClause;
    }

    private string BuildWhereClauseIncludingProvisioned(string baseWhereClause)
    {
        var provisionedNames = _instanceRepo.GetInstanceNames();

        if (provisionedNames.Count == 0)
        {
            _logger.LogInformation("No provisioned instances found — cannot include any.");
            // we return a clause that matches nothing:
            return baseWhereClause + " AND ElementName = '__none__'";
        }

        _logger.LogInformation("Including only provisioned instances: {Count} names", provisionedNames.Count);

        var nameConditions = new List<string>();

        foreach (var name in provisionedNames)
        {
            var safeName = name.Replace("'", "''");
            nameConditions.Add($"ElementName = '{safeName}'");
        }

        var orClause = string.Join(" OR ", nameConditions);

        return $"{baseWhereClause} AND ({orClause})";
    }
}
