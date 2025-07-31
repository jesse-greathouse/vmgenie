using System;

using YamlDotNet.Serialization;

namespace VmGenie.Artifacts;

/// <summary>
/// Represents a remote or downloadable Genie Machine Image (GMI) package as described in the repository manifest.
/// </summary>
public class GmiPackage
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";
    [YamlMember(Alias = "version")]
    public string Version { get; set; } = "";
    [YamlMember(Alias = "url")]
    public string Url { get; set; } = "";
    [YamlMember(Alias = "crc64nvme")]
    public string Crc64Nvme { get; set; } = "";
    [YamlMember(Alias = "last_modified")]
    public string LastModified { get; set; } = "";
    [YamlMember(Alias = "maintainer")]
    public string Maintainer { get; set; } = "";
    [YamlMember(Alias = "maintainer_email")]
    public string MaintainerEmail { get; set; } = "";
    [YamlMember(Alias = "source_url")]
    public string SourceUrl { get; set; } = "";
    [YamlMember(Alias = "description")]
    public string Description { get; set; } = "";
    [YamlMember(Alias = "hyperv_generation")]
    public int HypervGeneration { get; set; } = 2;

    /// <summary>
    /// Returns the repository key for this package (e.g., "Debian-12").
    /// </summary>
    public string GetRepoKey()
    {
        // Canonicalizes the key as "{Os}-{Version}"
        var os = GetOsNameFromName();
        return $"{os}-{Version}";
    }

    /// <summary>
    /// Heuristic extraction of the OS from the package name (e.g., "GMI Debian 12" → "Debian").
    /// </summary>
    public string GetOsNameFromName()
    {
        if (Name.StartsWith("GMI ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3) // GMI OS Version
                return parts[1];
        }
        // Fallback: try to parse from description or other means
        return "";
    }
}
