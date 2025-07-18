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
    private readonly Dictionary<string, Instance> _artifactInstances = new(StringComparer.OrdinalIgnoreCase);

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

    public List<Vm> GetAll(
        ProvisionedFilter provisionedFilter = ProvisionedFilter.All,
        VmState? stateFilter = null)
    {
        var vms = new List<Vm>();
        var whereClause = "Caption = 'Virtual Machine'";

        if (provisionedFilter == ProvisionedFilter.ExcludeProvisioned)
        {
            whereClause = BuildWhereClauseExcludingProvisioned(whereClause);
        }
        else if (provisionedFilter == ProvisionedFilter.OnlyProvisioned)
        {
            whereClause = BuildWhereClauseIncludingProvisioned(whereClause);
        }

        if (stateFilter != null)
        {
            whereClause += $" AND EnabledState = {(ushort)stateFilter.Value}";
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
            var artifactInstance = GetArtifactInstance(
                system.CimInstanceProperties["ElementName"].Value?.ToString() ?? string.Empty);

            var vm = Vm.FromCimInstance(_session, system, includeHostResourcePath: false, artifactInstance: artifactInstance);
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
            var artifactInstance = GetArtifactInstance(
                system.CimInstanceProperties["ElementName"].Value?.ToString() ?? string.Empty);

            return Vm.FromCimInstance(_session, system, artifactInstance: artifactInstance);
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
            var artifactInstance = GetArtifactInstance(
                system.CimInstanceProperties["ElementName"].Value?.ToString() ?? string.Empty);

            vms.Add(Vm.FromCimInstance(_session, system, includeHostResourcePath: false, artifactInstance: artifactInstance));
        }

        return vms;
    }

    private string BuildWhereClauseExcludingProvisioned(string baseWhereClause)
    {
        var provisionedNames = PopulateArtifactInstances();

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
        var provisionedNames = PopulateArtifactInstances();

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

    private List<string> PopulateArtifactInstances()
    {
        _artifactInstances.Clear();
        var instances = _instanceRepo.GetAll();

        foreach (var instance in instances)
        {
            _artifactInstances[instance.Name] = instance;
        }

        return [.. _artifactInstances.Keys];
    }

    private Instance? GetArtifactInstance(string name) =>
        _artifactInstances.TryGetValue(name, out var instance) ? instance : null;
}
