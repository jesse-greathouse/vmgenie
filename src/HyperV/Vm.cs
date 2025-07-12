using System;
using System.Linq;

using Microsoft.Management.Infrastructure;

namespace VmGenie.HyperV;

/// <summary>
/// Represents a Hyper‑V virtual machine.
/// </summary>
public class Vm(
    string id,
    string name,
    string state,
    string? description,
    int? cpuCount,
    ulong? memoryMb,
    ulong? uptimeSeconds,
    DateTime? creationTime,
    string? hostResourcePath,
    string? generation
)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string State { get; } = state;
    public string? Description { get; } = description;
    public int? CpuCount { get; } = cpuCount;
    public ulong? MemoryMb { get; } = memoryMb;
    public ulong? UptimeSeconds { get; } = uptimeSeconds;
    public DateTime? CreationTime { get; } = creationTime;
    public string? HostResourcePath { get; } = hostResourcePath;
    public string? Generation { get; } = generation;

    public override string ToString()
    {
        return $"{Name} [{Id}] - State: {State}, CPUs: {CpuCount}, Memory: {MemoryMb} MB, Uptime: {UptimeSeconds}s";
    }

    /// <summary>
    /// Factory method to translate a CIM instance into a Vm object.
    /// </summary>
    public static Vm FromCimInstance(CimSession session, CimInstance system)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(system);

        string id = system.CimInstanceProperties["Name"].Value?.ToString() ?? "";
        string name = system.CimInstanceProperties["ElementName"].Value?.ToString() ?? "";
        string state = GetStateAsString(system.CimInstanceProperties["EnabledState"].Value);

        var settings = GetSettingsForVm(session, id);

        // Updated description handling:
        object? notesObj = settings.CimInstanceProperties["Notes"].Value;
        string? description = notesObj switch
        {
            string s => s,
            string[] arr => string.Join("; ", arr),
            _ => "(none)"
        };

        int? cpuCount = settings.CimInstanceProperties["VirtualQuantity"]?.Value as int?;
        ulong? memoryMb = settings.CimInstanceProperties["VirtualSystemMemory"]?.Value as ulong?;
        ulong? uptimeSeconds = system.CimInstanceProperties["OnTimeInMilliseconds"]?.Value is ulong ms
            ? ms / 1000
            : null;

        DateTime? creationTime = system.CimInstanceProperties["InstallDate"].Value is DateTime dt
            ? dt
            : null;

        string? hostResourcePath = settings.CimInstanceProperties["ConfigurationID"]?.Value?.ToString();
        string? generation = settings.CimInstanceProperties["VirtualSystemSubType"]?.Value?.ToString();

        return new Vm(
            id,
            name,
            state,
            description,
            cpuCount,
            memoryMb,
            uptimeSeconds,
            creationTime,
            hostResourcePath,
            generation
        );
    }

    /// <summary>
    /// Helper to query the settings object for a VM given its ID.
    /// </summary>
    private static CimInstance GetSettingsForVm(CimSession session, string vmId)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(vmId))
            throw new ArgumentNullException(nameof(vmId));

        // First, get the VM instance
        var systems = session.QueryInstances(
            "root/virtualization/v2",
            "WQL",
            $"SELECT * FROM Msvm_ComputerSystem WHERE Name = '{vmId.Replace("'", "''")}'");

        var vm = systems.FirstOrDefault() ?? throw new InvalidOperationException($"VM with id '{vmId}' not found.");

        // Now enumerate associated settings
        var settingsInstances = session.EnumerateAssociatedInstances(
            "root/virtualization/v2",
            vm,
            "Msvm_SettingsDefineState",            // association class
            "Msvm_VirtualSystemSettingData",      // result class
            null,                                 // source role
            null);                                // result role

        var first = settingsInstances.FirstOrDefault();
        return first ?? throw new InvalidOperationException($"No settings found for VM: {vmId}");
    }

    /// <summary>
    /// Helper to translate EnabledState code to human-readable state.
    /// </summary>
    private static string GetStateAsString(object? enabledState)
    {
        return enabledState is ushort state
            ? state switch
            {
                2 => "Running",
                3 => "Off",
                32768 => "Paused",
                32769 => "Suspended",
                32770 => "Starting",
                32771 => "Snapshotting",
                32773 => "Saving",
                32774 => "Stopping",
                _ => "Unknown",
            }
            : "Unknown";
    }
}
