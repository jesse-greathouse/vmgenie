using System;

using Microsoft.Management.Infrastructure;

namespace VmGenie.HyperV;

/// <summary>
/// Represents a Hyper‑V virtual switch.
/// </summary>
public class VmSwitch
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Notes { get; init; }
    public string? Type { get; init; }

    public override string ToString() => $"{Name} [{Id}] ({Type ?? "Unknown"})";

    /// <summary>
    /// Factory method to create VmSwitch from CIM instance.
    /// </summary>
    public static VmSwitch FromCimInstance(CimInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        string id = instance.CimInstanceProperties["Name"]?.Value?.ToString() ?? Guid.NewGuid().ToString();
        string name = instance.CimInstanceProperties["ElementName"]?.Value?.ToString() ?? "(unnamed)";
        string? notes = instance.CimInstanceProperties["Notes"]?.Value?.ToString();
        string? type = instance.CimInstanceProperties["SwitchType"]?.Value?.ToString();

        return new VmSwitch
        {
            Id = id,
            Name = name,
            Notes = notes,
            Type = type
        };
    }
}
