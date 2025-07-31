using System;
using System.IO;

using Microsoft.Extensions.Logging;

using VmGenie.Artifacts;
using VmGenie.HyperV;

namespace VmGenie;

public class CoordinatorService(
    VmLifecycleService vmLifecycle,
    VhdxManager vhdxManager,
    InstanceRepository instanceRepo,
    VmHelper vmHelper,
    VmProvisioningService provisioningService,
    ArchiveManager archiveManager,
    Config config,
    ILogger<CoordinatorService> logger)
{
    private readonly VmLifecycleService _vmLifecycle = vmLifecycle ?? throw new ArgumentNullException(nameof(vmLifecycle));
    private readonly VhdxManager _vhdxManager = vhdxManager ?? throw new ArgumentNullException(nameof(vhdxManager));
    private readonly InstanceRepository _instanceRepo = instanceRepo ?? throw new ArgumentNullException(nameof(instanceRepo));
    private readonly VmHelper _vmHelper = vmHelper ?? throw new ArgumentNullException(nameof(vmHelper));
    private readonly VmProvisioningService _provisioningService = provisioningService ?? throw new ArgumentNullException(nameof(provisioningService));
    private readonly ArchiveManager _archiveManager = archiveManager ?? throw new ArgumentNullException(nameof(archiveManager));
    private readonly ILogger<CoordinatorService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Config _config = config ?? throw new ArgumentNullException(nameof(config));

    /// <summary>
    /// Deletes the VM, VHDX disk, and all related artifacts for the given VM GUID.
    /// </summary>
    public void DeleteInstance(string instanceId, bool force = false)
    {
        // Lookup VM and instance by GUID.
        var vm = _vmHelper.GetVm(instanceId); // Throws if not found.
        var instance = _instanceRepo.GetByName(vm.Name) ?? throw new InvalidOperationException($"No instance artifact found for VM: {vm.Name} [{instanceId}]");

        // Ensure VM is off, delete VM from Hyper-V by GUID.
        try
        {
            _vmLifecycle.Delete(instanceId, force: force); // Change VmLifecycleService.Delete to take GUID.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete VM from Hyper-V for {Name} [{Id}]", vm.Name, instanceId);
        }

        // Delete VHDX disk(s) using VhdxManager by GUID.
        try
        {
            _vhdxManager.DeleteVhdx(instanceId); // Change VhdxManager.DeleteVhdx to take GUID.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete VHDX for {Name} [{Id}]", vm.Name, instanceId);
        }

        // Delete instance artifacts (cloud-init ISO, metadata, dir).
        try
        {
            if (System.IO.Directory.Exists(instance.Path))
                System.IO.Directory.Delete(instance.Path, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete artifact directory: {Path}", instance.Path);
        }

        // Remove from VM helper cache (by GUID).
        try
        {
            _vmHelper.RemoveFromCache(instanceId);
            _logger.LogInformation("Removed VM [{Id}] from VmHelper cache.", instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove VM [{Id}] from VmHelper cache.", instanceId);
        }

        _logger.LogInformation("CoordinatorService: Complete delete for {Name} [{Id}].", vm.Name, instanceId);
    }

    public Export ExportInstance(string instanceId)
    {
        // Lookup VM and instance artifact by GUID.
        var vm = _vmHelper.GetVm(instanceId) ?? throw new InvalidOperationException($"VM not found for InstanceId: {instanceId}");
        var instance = _instanceRepo.GetByName(vm.Name) ?? throw new InvalidOperationException($"No instance artifact found for VM: {vm.Name} [{instanceId}]");

        // Generate unique temp folder for export.
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm-ss");
        string archiveName = $"{instance.Name}_{instanceId}_{timestamp}.zip";
        string archiveDir = Path.Combine(_config.Var, "export");
        string archiveUri = Path.Combine(archiveDir, archiveName);
        string tmpFolder = Path.Combine(_config.Tmp, Path.GetFileNameWithoutExtension(archiveName));

        if (Directory.Exists(tmpFolder))
            throw new InvalidOperationException($"Temp folder already exists: {tmpFolder}");

        try
        {
            // Copy instance artifacts to temp.
            _instanceRepo.StageInstanceForExport(instance.Name, tmpFolder);

            // Export VM (Hyper-V) to the same temp folder.
            _provisioningService.ExportVm(instanceId, tmpFolder);

            // Use FromArchiveUri for export metadata.
            var export = Export.FromArchiveUri(archiveUri);

            // Create archive from temp.
            _archiveManager.ZipExport(export);

            _logger.LogInformation("Export completed: {Archive} ({Timestamp})", export.ArchiveUri, timestamp);

            return export;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export instance: {Name} [{Id}]", instance.Name, instanceId);
            throw;
        }
        finally
        {
            // Clean up temp directory if it still exists.
            try
            {
                if (Directory.Exists(tmpFolder))
                    Directory.Delete(tmpFolder, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up temp export directory: {TmpFolder}", tmpFolder);
            }
        }
    }

    /// <summary>
    /// Imports a VM instance from an export archive. Supports 'copy' (new instance name/ID) and 'restore' (original identity) modes.
    /// </summary>
    /// <param name="archiveUri">Path to the exported archive (zip).</param>
    /// <param name="mode">'copy' for new instance, 'restore' for backup restore.</param>
    /// <param name="newInstanceName">Required if mode is 'copy'.</param>
    /// <returns>The new Export metadata object for the imported VM.</returns>
    public Export ImportInstance(
        string archiveUri,
        ImportMode mode = ImportMode.Restore,
        string? newInstanceName = null)
    {
        if (string.IsNullOrWhiteSpace(archiveUri))
            throw new ArgumentNullException(nameof(archiveUri));
        if (!File.Exists(archiveUri))
            throw new FileNotFoundException("Export archive not found.", archiveUri);

        Export export;
        string tmpFolder;

        // Handle Copy or Restore Mode
        if (mode == ImportMode.Copy)
        {
            if (string.IsNullOrWhiteSpace(newInstanceName))
                throw new ArgumentNullException(nameof(newInstanceName), "New instance name must be provided for copy mode.");

            export = _archiveManager.CopyExportAsNewInstance(archiveUri, newInstanceName);
            tmpFolder = _archiveManager.GetTmpFolder(export);
        }
        else if (mode == ImportMode.Restore)
        {
            export = _archiveManager.UnzipExport(archiveUri);
            tmpFolder = _archiveManager.GetTmpFolder(export);
        }
        else
        {
            throw new ArgumentException($"Unknown import mode '{mode}'.", nameof(mode));
        }

        // Import into Hyper-V
        try
        {
            // Capture the returned VM object from ImportVm
            var importedVm = _provisioningService.ImportVm(export, tmpFolder, copy: mode == ImportMode.Copy);

            // Stage artifacts to canonical instance directory
            _archiveManager.StageCopiedInstanceArtifacts(export);

            if (mode == ImportMode.Copy)
            {
                // Compute new iso path based on how you stage the copy
                var newIsoPath = Path.Combine(_config.CloudDir, export.InstanceName, "seed.iso");
                // Use the actual imported VM's GUID
                _provisioningService.SwapIso(importedVm.Id, newIsoPath);
            }

            _logger.LogInformation("Import completed successfully: {InstanceName} (Mode: {Mode})", export.InstanceName, mode);
            return export;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import instance from archive: {Archive} (Mode: {Mode})", archiveUri, mode);
            throw;
        }
        finally
        {
            // Cleanup temp folder
            try
            {
                if (Directory.Exists(tmpFolder))
                    Directory.Delete(tmpFolder, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up temp import directory: {TmpFolder}", tmpFolder);
            }
        }
    }

    public Gmi ExportGmi(string instanceId)
    {
        var vm = _vmHelper.GetVm(instanceId) ?? throw new InvalidOperationException($"VM not found for InstanceId: {instanceId}");

        // Archive/file naming
        string archiveName = Gmi.GetDefaultArchiveName(vm);
        string archiveDir = _config.GmiDir;
        string archiveUri = Path.Combine(archiveDir, archiveName);

        // Manifest/readme paths
        string manifestPath = Path.Combine(archiveDir, Path.GetFileNameWithoutExtension(archiveName) + ".yml");
        string readmePath = Path.Combine(archiveDir, Path.GetFileNameWithoutExtension(archiveName) + ".md");

        string tmpFolder = Path.Combine(_config.Tmp, Path.GetFileNameWithoutExtension(archiveName));
        if (Directory.Exists(tmpFolder))
            throw new InvalidOperationException($"Temp folder already exists: {tmpFolder}");

        try
        {
            // Export the GMI VM to the tmp folder
            _provisioningService.ExportVm(instanceId, tmpFolder);

            // Manifest MUST exist now; throw if not
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException($"GMI manifest does not exist: {manifestPath}");

            // Load GMI metadata from YAML
            var metadata = GmiMetadata.Load(manifestPath);

            // Compose VHDX path: tmpFolder/Virtual Hard Disks/{gmi.gmiName}.vhdx
            var gmiName = vm.Name; // Assumes GMI.Name is the VM name
            var vhdxFile = Path.Combine(tmpFolder, Gmi.VirtualHardDisksDir, $"{gmiName}.vhdx");
            if (!File.Exists(vhdxFile))
                throw new FileNotFoundException($"Expected VHDX not found: {vhdxFile}");

            // Compute new checksum
            metadata.ChecksumSha256 = GmiMetadata.ComputeSha256(vhdxFile);

            // Save the updated metadata back to manifestPath
            metadata.Save(manifestPath);

            if (!File.Exists(readmePath))
                File.WriteAllText(readmePath, "");

            // Copy them into tmpFolder for inclusion in zip
            File.Copy(manifestPath, Path.Combine(tmpFolder, Path.GetFileName(manifestPath)), overwrite: true);
            File.Copy(readmePath, Path.Combine(tmpFolder, Path.GetFileName(readmePath)), overwrite: true);

            // Zip it up
            var gmi = Gmi.FromArchiveUri(archiveUri);
            _archiveManager.ZipGmi(gmi);

            _logger.LogInformation("GMI export completed: {Archive} (VM: {VmName})", archiveUri, vm.Name);

            return gmi;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export GMI from InstanceId: {InstanceId}", instanceId);
            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tmpFolder))
                    Directory.Delete(tmpFolder, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up temp GMI export dir: {TmpFolder}", tmpFolder);
            }
        }
    }

    public Gmi ImportGmi(string archiveUri)
    {
        if (string.IsNullOrWhiteSpace(archiveUri))
            throw new ArgumentNullException(nameof(archiveUri));
        if (!File.Exists(archiveUri))
            throw new FileNotFoundException("GMI archive not found.", archiveUri);

        Gmi gmi = _archiveManager.UnzipGmi(archiveUri);
        string tmpFolder = _archiveManager.GetTmpFolder(gmi);

        string manifestFileName = Path.GetFileNameWithoutExtension(gmi.ArchiveName) + ".yml";
        string readmeFileName = Path.GetFileNameWithoutExtension(gmi.ArchiveName) + ".md";
        string manifestSource = Path.Combine(tmpFolder, manifestFileName);
        string readmeSource = Path.Combine(tmpFolder, readmeFileName);

        var metadata = GmiMetadata.Load(manifestSource);
        string vhdxPath = Path.Combine(
            tmpFolder,
            Gmi.VirtualHardDisksDir,
            $"{metadata.VmName}.vhdx"
        );

        if (!metadata.MatchesVhdx(vhdxPath))
            throw new InvalidOperationException(
                $"Checksum mismatch: GMI VHDX may be corrupted or tampered with. " +
                $"Manifest: {metadata.ChecksumSha256}, Actual: {(File.Exists(vhdxPath) ? GmiMetadata.ComputeSha256(vhdxPath) : "MISSING")}"
            );

        string manifestDest = Path.Combine(_config.GmiDir, manifestFileName);
        string readmeDest = Path.Combine(_config.GmiDir, readmeFileName);

        try
        {
            // Overwrite manifest and readme in var/gmi
            if (File.Exists(manifestSource))
                File.Copy(manifestSource, manifestDest, overwrite: true);
            if (File.Exists(readmeSource))
                File.Copy(readmeSource, readmeDest, overwrite: true);

            _provisioningService.ImportGmi(gmi, tmpFolder);

            _logger.LogInformation("GMI import completed successfully: {GmiName} ({Archive})", gmi.GmiName, gmi.ArchiveUri);

            return gmi;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import GMI from archive: {ArchiveUri}", archiveUri);
            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tmpFolder))
                    Directory.Delete(tmpFolder, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up temp GMI import dir: {TmpFolder}", tmpFolder);
            }
        }
    }

}
