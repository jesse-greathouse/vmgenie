using System;
using System.IO;
using System.Text.RegularExpressions;

using VmGenie.HyperV;

namespace VmGenie.Artifacts;

/// <summary>
/// Represents a Genie Machine Image (GMI) archive: an OS+version base VM package
/// with all supporting cloud-init seed-data, manifest, and Hyper-V export.
/// </summary>
public partial class Gmi(
    string archiveUri,
    string archiveName,
    string os,
    string version,
    string gmiName
)
{
    public string ArchiveUri { get; } = archiveUri ?? throw new ArgumentNullException(nameof(archiveUri));
    public string ArchiveName { get; } = archiveName ?? throw new ArgumentNullException(nameof(archiveName));
    public string Os { get; } = os ?? throw new ArgumentNullException(nameof(os));
    public string Version { get; } = version ?? throw new ArgumentNullException(nameof(version));
    public string GmiName { get; } = gmiName ?? throw new ArgumentNullException(nameof(gmiName));

    public const string SnapshotsDir = "Snapshots";
    public const string VirtualHardDisksDir = "Virtual Hard Disks";
    public const string VirtualMachinesDir = "Virtual Machines";
    public string ManifestFile => $"{Path.GetFileNameWithoutExtension(ArchiveName)}.yml";
    public string ReadmeFile => $"{Path.GetFileNameWithoutExtension(ArchiveName)}.md";

    public static string GetDefaultArchiveName(Vm vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        // Replace spaces with dash for the filename
        string safeName = vm.Name.Replace(' ', '-');
        return $"{safeName}.zip";
    }

    public static Gmi FromArchiveUri(string archiveUri)
    {
        if (string.IsNullOrWhiteSpace(archiveUri))
            throw new ArgumentNullException(nameof(archiveUri));
        string archiveName = Path.GetFileName(archiveUri);
        var match = GmiArchiveRegex().Match(archiveName);

        if (!match.Success)
            throw new FormatException($"Archive file name '{archiveName}' does not match GMI pattern.");

        string os = match.Groups["os"].Value;
        string ver = match.Groups["ver"].Value;

        string gmiName = $"GMI {os} {ver}";
        return new Gmi(archiveUri, archiveName, os, ver, gmiName);
    }

    // "GMI-{os}-{version}.zip"
    [GeneratedRegex(@"^GMI\-(?<os>.+)\-(?<ver>.+)\.zip$", RegexOptions.Compiled)]
    private static partial Regex GmiArchiveRegex();
}
