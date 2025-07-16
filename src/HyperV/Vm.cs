using System;
using System.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;

namespace VmGenie.HyperV;

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
    private const string Namespace = @"root\virtualization\v2";

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

    public override string ToString() =>
        $"{Name} [{Id}] - State: {State}, CPUs: {CpuCount}, Memory: {MemoryMb} MB, Uptime: {UptimeSeconds}s";

    public static Vm FromCimInstance(
        CimSession session,
        CimInstance system,
        bool includeHostResourcePath = true
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(system);

        string id = system.CimInstanceProperties["Name"].Value?.ToString()
            ?? throw new InvalidOperationException("VM ID missing");
        string name = system.CimInstanceProperties["ElementName"].Value?.ToString() ?? "(Unnamed)";
        string state = StateToString(system.CimInstanceProperties["EnabledState"].Value);

        var settings = GetSettingsForVm(session, id);
        string? description = settings.CimInstanceProperties["Notes"].Value switch
        {
            string s => s,
            string[] arr => string.Join("; ", arr),
            _ => null
        };

        int? cpuCount = settings.CimInstanceProperties["VirtualQuantity"]?.Value as int?;
        ulong? memoryMb = settings.CimInstanceProperties["VirtualSystemMemory"]?.Value as ulong?;
        ulong? uptimeSeconds = system.CimInstanceProperties["OnTimeInMilliseconds"]?.Value is ulong ms ? ms / 1000 : null;
        DateTime? creationTime = system.CimInstanceProperties["InstallDate"].Value as DateTime?;
        string? generation = settings.CimInstanceProperties["VirtualSystemSubType"]?.Value?.ToString();

        string? hostResourcePath = null;
        if (includeHostResourcePath)
        {
            hostResourcePath = ResolveVhdxPath(name)
                ?? throw new InvalidOperationException($"Could not resolve VHDX path for VM '{name}' ({id}).");
        }

        return new Vm(id, name, state, description, cpuCount, memoryMb, uptimeSeconds, creationTime, hostResourcePath, generation);
    }

    /// <summary>
    /// Retrieves the active Msvm_VirtualSystemSettingData for a given VM.
    /// </summary>
    public static CimInstance GetSettingsForVm(CimSession session, string vmId)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(vmId);

        var vm = session.QueryInstances(Namespace, "WQL",
            $"SELECT * FROM Msvm_ComputerSystem WHERE Name = '{vmId.Replace("'", "''")}'")
            .FirstOrDefault() ?? throw new InvalidOperationException($"VM with id '{vmId}' not found.");

        var allSettings = session.EnumerateAssociatedInstances(
            Namespace, vm,
            "Msvm_SettingsDefineState",
            "Msvm_VirtualSystemSettingData",
            null, null).ToList();

        if (allSettings.Count == 0)
        {
            throw new InvalidOperationException($"No settings found for VM {vmId}");
        }

        var activeSetting = allSettings.FirstOrDefault(s =>
        {
            var desc = s.CimInstanceProperties["Description"]?.Value?.ToString();
            var type = s.CimInstanceProperties["VirtualSystemType"]?.Value?.ToString();
            var cfgId = s.CimInstanceProperties["ConfigurationID"]?.Value?.ToString();

            bool isActive =
                (desc?.Contains("active", StringComparison.OrdinalIgnoreCase) == true) ||
                (type?.EndsWith("Realized", StringComparison.OrdinalIgnoreCase) == true) ||
                (cfgId?.Equals(vmId, StringComparison.OrdinalIgnoreCase) == true);

            return isActive;
        }) ?? throw new InvalidOperationException($"No active settings found for VM {vmId}");

        return activeSetting;
    }

    /// <summary>
    /// Resolves the VHDX path by calling pwsh.exe and running Get-VMHardDiskDrive.
    /// </summary>
    private static string? ResolveVhdxPath(string vmName)
    {
        try
        {
            string command = $"Get-VMHardDiskDrive -VMName '{vmName}' | Select-Object -ExpandProperty Path";
            string output = PowerShellHelper.Run(command);

            return !string.IsNullOrWhiteSpace(output) ? output : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string StateToString(VmState state) =>
        Enum.IsDefined(typeof(VmState), state) ? state.ToString() : $"Unknown({(ushort)state})";

    public static string StateToString(object? enabledState)
    {
        return enabledState is ushort stateValue ? StateToString((VmState)stateValue) : "Unknown";
    }
}
