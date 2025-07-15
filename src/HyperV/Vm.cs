using System;
using System.Diagnostics;
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
        ILogger<VmRepository> logger,
        bool includeHostResourcePath = true
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(system);

        string id = system.CimInstanceProperties["Name"].Value?.ToString()
            ?? throw new InvalidOperationException("VM ID missing");
        string name = system.CimInstanceProperties["ElementName"].Value?.ToString() ?? "(Unnamed)";
        string state = GetStateAsString(system.CimInstanceProperties["EnabledState"].Value);

        var settings = GetSettingsForVm(session, id, logger);
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
            hostResourcePath = ResolveVhdxPath(name, logger)
                ?? throw new InvalidOperationException($"Could not resolve VHDX path for VM '{name}' ({id}).");
        }

        return new Vm(id, name, state, description, cpuCount, memoryMb, uptimeSeconds, creationTime, hostResourcePath, generation);
    }

    private static CimInstance GetSettingsForVm(CimSession session, string vmId, ILogger logger)
    {
        var vm = session.QueryInstances(Namespace, "WQL",
            $"SELECT * FROM Msvm_ComputerSystem WHERE Name = '{vmId.Replace("'", "''")}'")
            .FirstOrDefault() ?? throw new InvalidOperationException($"VM with id '{vmId}' not found.");

        var settings = session.EnumerateAssociatedInstances(
            Namespace, vm,
            "Msvm_SettingsDefineState", "Msvm_VirtualSystemSettingData", null, null)
            .FirstOrDefault() ?? throw new InvalidOperationException($"No settings found for VM: {vmId}");

        return settings;
    }

    /// <summary>
    /// Resolves the VHDX path by calling pwsh.exe and running Get-VMHardDiskDrive.
    /// </summary>
    private static string? ResolveVhdxPath(string vmName, ILogger logger)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = $"-Command \"Get-VMHardDiskDrive -VMName '{vmName}' | Select-Object -ExpandProperty Path\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start PowerShell process (Process.Start returned null).");

            string output = process.StandardOutput.ReadToEnd().Trim();
            string error = process.StandardError.ReadToEnd().Trim();
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(error))
            {
                logger.LogError("PowerShell error while resolving VHDX path: {Error}", error);
                return null;
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                logger.LogInformation("Resolved VHDX path via PowerShell: {Path}", output);
                return output;
            }

            logger.LogWarning("PowerShell returned no VHDX path for VM: {VmName}", vmName);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception while resolving VHDX path via PowerShell for VM: {VmName}", vmName);
            return null;
        }
    }

    private static string GetStateAsString(object? enabledState) =>
        enabledState is ushort state ? state switch
        {
            2 => "Running",
            3 => "Off",
            32768 => "Paused",
            32769 => "Suspended",
            32770 => "Starting",
            32771 => "Snapshotting",
            32773 => "Saving",
            32774 => "Stopping",
            _ => "Unknown"
        } : "Unknown";
}
