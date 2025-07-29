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
    /// Zips the folder represented by the Gmi into the archive location, then deletes the temp folder.
    /// </summary>
    public void ZipGmi(Gmi gmi)
    {
        ArgumentNullException.ThrowIfNull(gmi);

        string tmpFolder = GetTmpFolder(gmi);

        if (!Directory.Exists(tmpFolder))
        {
            _logger.LogError("Temp folder does not exist for GMI export: {Folder}", tmpFolder);
            throw new DirectoryNotFoundException($"Temp folder does not exist: {tmpFolder}");
        }

        _logger.LogInformation("Creating GMI archive: {ArchiveUri} from {TmpFolder}", gmi.ArchiveUri, tmpFolder);

        if (File.Exists(gmi.ArchiveUri))
            File.Delete(gmi.ArchiveUri);

        ZipFile.CreateFromDirectory(tmpFolder, gmi.ArchiveUri, CompressionLevel.Optimal, includeBaseDirectory: false);

        _logger.LogInformation("GMI archive created: {ArchiveUri}", gmi.ArchiveUri);

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

    public Gmi UnzipGmi(string archiveUri)
    {
        if (string.IsNullOrWhiteSpace(archiveUri))
            throw new ArgumentNullException(nameof(archiveUri));

        Gmi gmi = Gmi.FromArchiveUri(archiveUri);

        string tmpFolder = GetTmpFolder(gmi);

        if (Directory.Exists(tmpFolder))
        {
            _logger.LogWarning("Temp folder already exists, deleting: {TmpFolder}", tmpFolder);
            Directory.Delete(tmpFolder, recursive: true);
        }

        _logger.LogInformation("Extracting GMI archive: {ArchiveUri} to {TmpFolder}", archiveUri, tmpFolder);
        ZipFile.ExtractToDirectory(archiveUri, tmpFolder);
        _logger.LogInformation("GMI archive extracted: {ArchiveUri} to {TmpFolder}", archiveUri, tmpFolder);

        return gmi;
    }

    public Export CopyExportAsNewInstance(string archiveUri, string newInstanceName)
    {
        // Parse original Export object from the archive
        Export oldExport = Export.FromArchiveUri(archiveUri);

        // Compose new archive name by replacing InstanceName
        string newArchiveName = archiveUri.Replace(
            oldExport.InstanceName + "_",
            newInstanceName + "_"
        );
        string newArchiveUri = Path.Combine(Path.GetDirectoryName(archiveUri)!, newArchiveName);

        // Copy archive on disk
        File.Copy(archiveUri, newArchiveUri);

        // Unzip as if it were a "fresh" export under new name
        Export newExport = UnzipExport(newArchiveUri);

        // The temp folder for the new export
        string tmpDir = Path.Combine(_config.Tmp, Path.GetFileNameWithoutExtension(newArchiveName));

        // File renames inside tmpDir
        // SSH keys
        string oldPem = Path.Combine(tmpDir, $"{oldExport.InstanceName}.pem");
        string newPem = Path.Combine(tmpDir, $"{newExport.InstanceName}.pem");
        if (File.Exists(oldPem)) File.Move(oldPem, newPem);

        string oldPemPub = Path.Combine(tmpDir, $"{oldExport.InstanceName}.pem.pub");
        string newPemPub = Path.Combine(tmpDir, $"{newExport.InstanceName}.pem.pub");
        if (File.Exists(oldPemPub)) File.Move(oldPemPub, newPemPub);

        // If metadata.yml includes the old instance name, update it
        string metadataPath = Path.Combine(tmpDir, Export.MetadataFile);
        if (File.Exists(metadataPath))
        {
            string text = File.ReadAllText(metadataPath);
            text = text.Replace(oldExport.InstanceName, newExport.InstanceName);
            File.WriteAllText(metadataPath, text);
        }

        // Return the new Export for downstream workflow
        return newExport;
    }

    /// <summary>
    /// Copies all essential artifacts for the VM instance from the temp folder to the instance directory in CloudDir.
    /// Creates the destination directory if it does not exist. Overwrites files as needed.
    /// </summary>
    public void StageCopiedInstanceArtifacts(Export export)
    {
        ArgumentNullException.ThrowIfNull(export);

        string tmpFolder = GetTmpFolder(export);
        string instanceDir = Path.Combine(_config.CloudDir, export.InstanceName);

        _logger.LogInformation("Staging instance artifacts from '{TmpFolder}' to '{InstanceDir}'", tmpFolder, instanceDir);

        // Create destination instance directory if not exists
        if (!Directory.Exists(instanceDir))
        {
            Directory.CreateDirectory(instanceDir);
            _logger.LogInformation("Created instance directory: {InstanceDir}", instanceDir);
        }

        // Copy seed-data directory recursively
        string srcSeedData = Path.Combine(tmpFolder, Export.SeedDataDir);
        string destSeedData = Path.Combine(instanceDir, Export.SeedDataDir);
        if (Directory.Exists(srcSeedData))
        {
            CopyDirectoryRecursive(srcSeedData, destSeedData, overwrite: true);
            _logger.LogInformation("Copied seed-data directory to: {Dest}", destSeedData);
        }
        else
        {
            _logger.LogWarning("No seed-data directory found at {Src}", srcSeedData);
        }

        // Copy metadata.yml
        string srcMetadata = Path.Combine(tmpFolder, Export.MetadataFile);
        string destMetadata = Path.Combine(instanceDir, Export.MetadataFile);
        if (File.Exists(srcMetadata))
        {
            File.Copy(srcMetadata, destMetadata, overwrite: true);
            _logger.LogInformation("Copied metadata.yml to: {Dest}", destMetadata);
        }

        // Copy seed.iso
        string srcIso = Path.Combine(tmpFolder, Export.SeedIso);
        string destIso = Path.Combine(instanceDir, Export.SeedIso);
        if (File.Exists(srcIso))
        {
            File.Copy(srcIso, destIso, overwrite: true);
            _logger.LogInformation("Copied seed.iso to: {Dest}", destIso);
        }

        // Copy private key
        string srcPrivKey = Path.Combine(tmpFolder, export.PrivKeyFile);
        string destPrivKey = Path.Combine(instanceDir, export.PrivKeyFile);
        if (File.Exists(srcPrivKey))
        {
            File.Copy(srcPrivKey, destPrivKey, overwrite: true);
            _logger.LogInformation("Copied private key to: {Dest}", destPrivKey);
        }

        // Copy public key
        string srcPubKey = Path.Combine(tmpFolder, export.PublicKeyFile);
        string destPubKey = Path.Combine(instanceDir, export.PublicKeyFile);
        if (File.Exists(srcPubKey))
        {
            File.Copy(srcPubKey, destPubKey, overwrite: true);
            _logger.LogInformation("Copied public key to: {Dest}", destPubKey);
        }
    }

    /// <summary>
    /// Calculates the temp folder for a given export based on config and archive name.
    /// </summary>
    public string GetTmpFolder(Export export)
    {
        ArgumentNullException.ThrowIfNull(export);

        string tmpFolderName = Path.GetFileNameWithoutExtension(export.ArchiveName);
        return Path.Combine(_config.Tmp, tmpFolderName);
    }

    /// <summary>
    /// Calculates the temp folder for a given Gmi based on config and archive name.
    /// </summary>
    public string GetTmpFolder(Gmi gmi)
    {
        ArgumentNullException.ThrowIfNull(gmi);

        string tmpFolderName = Path.GetFileNameWithoutExtension(gmi.ArchiveName);
        return Path.Combine(_config.Tmp, tmpFolderName);
    }

    /// <summary>
    /// Recursively copies the contents of a directory. Overwrites files in destination if specified.
    /// </summary>
    public static void CopyDirectoryRecursive(string sourceDir, string destDir, bool overwrite = false)
    {
        if (!Directory.Exists(destDir))
            Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: overwrite);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectoryRecursive(dir, destSubDir, overwrite);
        }
    }
}
