using System;
using System.IO;
using System.IO.Compression;

using Microsoft.Extensions.Logging;

namespace VmGenie.Artifacts;

public class ArchiveManager(Config config, ILogger<ArchiveManager> logger)
{
    private readonly Config _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly ILogger<ArchiveManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Zips the folder represented by the Export into the archive location, then deletes the temp folder.
    /// </summary>
    public void ZipExport(Export export)
    {
        ArgumentNullException.ThrowIfNull(export);

        string tmpFolder = GetTmpFolder(export);

        if (!Directory.Exists(tmpFolder))
        {
            _logger.LogError("Temp folder does not exist for export: {Folder}", tmpFolder);
            throw new DirectoryNotFoundException($"Temp folder does not exist: {tmpFolder}");
        }

        _logger.LogInformation("Creating archive: {ArchiveUri} from {TmpFolder}", export.ArchiveUri, tmpFolder);

        if (File.Exists(export.ArchiveUri))
            File.Delete(export.ArchiveUri);

        ZipFile.CreateFromDirectory(tmpFolder, export.ArchiveUri, CompressionLevel.Optimal, includeBaseDirectory: false);

        _logger.LogInformation("Archive created: {ArchiveUri}", export.ArchiveUri);

        Directory.Delete(tmpFolder, recursive: true);
        _logger.LogInformation("Temporary folder deleted: {TmpFolder}", tmpFolder);
    }

    /// <summary>
    /// Unzips the archive file to the temp folder location.
    /// </summary>
    public Export UnzipExport(string archiveUri)
    {
        if (string.IsNullOrWhiteSpace(archiveUri))
            throw new ArgumentNullException(nameof(archiveUri));

        Export export = Export.FromArchiveUri(archiveUri);

        string tmpFolder = GetTmpFolder(export);

        if (Directory.Exists(tmpFolder))
        {
            _logger.LogWarning("Temp folder already exists, deleting: {TmpFolder}", tmpFolder);
            Directory.Delete(tmpFolder, recursive: true);
        }

        _logger.LogInformation("Extracting archive: {ArchiveUri} to {TmpFolder}", archiveUri, tmpFolder);
        ZipFile.ExtractToDirectory(archiveUri, tmpFolder);

        _logger.LogInformation("Archive extracted: {ArchiveUri} to {TmpFolder}", archiveUri, tmpFolder);

        return export;
    }

    /// <summary>
    /// Calculates the temp folder for a given export based on config and archive name.
    /// </summary>
    private string GetTmpFolder(Export export)
    {
        ArgumentNullException.ThrowIfNull(export);

        string tmpFolderName = Path.GetFileNameWithoutExtension(export.ArchiveName);
        return Path.Combine(_config.Tmp, tmpFolderName);
    }
}
