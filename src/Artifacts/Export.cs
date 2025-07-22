using System;
using System.IO;
using System.Text.RegularExpressions;

namespace VmGenie.Artifacts;

public partial class Export(
    DateTime createdDate,
    DateTime updatedDate,
    string archiveUri,
    string archiveName,
    string instanceName,
    string instanceId
    )
{
    public DateTime CreatedDate { get; } = createdDate;
    public DateTime UpdatedDate { get; } = updatedDate;
    public string ArchiveUri { get; } = archiveUri ?? throw new ArgumentNullException(nameof(archiveUri));
    public string ArchiveName { get; } = archiveName ?? throw new ArgumentNullException(nameof(archiveName));
    public string InstanceName { get; } = instanceName ?? throw new ArgumentNullException(nameof(instanceName));
    public string InstanceId { get; } = instanceId ?? throw new ArgumentNullException(nameof(instanceId));

    // Default conventions as computed properties:
    public const string SnapshotsDir = "Snapshots";
    public const string VirtualHardDisksDir = "Virtual Hard Disks";
    public const string VirtualMachinesDir = "Virtual Machines";
    public const string SeedDataDir = "seed-data";
    public const string MetadataFile = "metadata.yml";
    public const string SeedIso = "seed.iso";
    public string PrivKeyFile => $"{InstanceName}.pem";
    public string PublicKeyFile => $"{InstanceName}.pem.pub";

    public string GetArchiveFileName()
    {
        var timestamp = CreatedDate.ToString("yyyy-MM-dd-HH-mm-ss");
        return $"{InstanceName}_{InstanceId}_{timestamp}.zip";
    }

    // Factory method to construct Export from a file path (absolute or just file name)
    public static Export FromArchiveUri(string archiveUri)
    {
        if (string.IsNullOrWhiteSpace(archiveUri))
            throw new ArgumentNullException(nameof(archiveUri));

        // Get just the file name
        string archiveName = Path.GetFileName(archiveUri);

        // Pattern: {{InstanceName}}_{{InstanceId}}_{{CreatedDate:yyyy-MM-dd-HH-mm-ss}}.zip
        // InstanceId is assumed to be a GUID (allowing dashes)
        var match = ArchiveFileNameRegex().Match(archiveName);

        if (!match.Success)
            throw new FormatException(
                $"Archive file name '{archiveName}' does not match expected format.");

        string instanceName = match.Groups["name"].Value;
        string instanceId = match.Groups["id"].Value;
        string dateStr = match.Groups["date"].Value;

        if (!DateTime.TryParseExact(
                dateStr,
                "yyyy-MM-dd-HH-mm-ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out DateTime createdDate))
        {
            throw new FormatException(
                $"Could not parse CreatedDate '{dateStr}' from archive name.");
        }

        // Resolve UpdatedDate
        DateTime updatedDate = createdDate;
        try
        {
            if (File.Exists(archiveUri))
            {
                updatedDate = File.GetLastWriteTime(archiveUri);
            }
        }
        catch
        {
            // Swallow any exceptions—fall back to createdDate
            updatedDate = createdDate;
        }

        return new Export(
            createdDate,
            updatedDate,
            archiveUri,
            archiveName,
            instanceName,
            instanceId
        );
    }

    [GeneratedRegex(@"^(?<name>.+)_(?<id>[a-fA-F0-9\-]{36})_(?<date>\d{4}-\d{2}-\d{2}-\d{2}-\d{2}-\d{2})\.zip$", RegexOptions.Compiled)]
    private static partial Regex ArchiveFileNameRegex();
}
