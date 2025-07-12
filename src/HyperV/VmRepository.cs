using System;
using System.Collections.Generic;

using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;

namespace VmGenie.HyperV;

public class VmRepository
{
    private readonly CimSession _session;

    public VmRepository()
    {
        var options = new DComSessionOptions
        {
            Impersonation = ImpersonationType.Impersonate
        };

        _session = CimSession.Create(null, options);

    }

    public List<Vm> GetAll()
    {
        var vms = new List<Vm>();

        var systems = _session.QueryInstances(
            "root/virtualization/v2",
            "WQL",
            "SELECT * FROM Msvm_ComputerSystem WHERE Caption = 'Virtual Machine'"
        );

        foreach (var system in systems)
        {
            vms.Add(Vm.FromCimInstance(_session, system));
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
            vms.Add(Vm.FromCimInstance(_session, system));
        }

        return vms;
    }
}
