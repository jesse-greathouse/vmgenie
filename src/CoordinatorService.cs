using System;
using System.IO;
using Microsoft.Extensions.Logging;
using VmGenie.HyperV;
using VmGenie.Artifacts;

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
        // 1. Lookup VM and instance by GUID.
        var vm = _vmHelper.GetVm(instanceId); // Throws if not found.
        var instance = _instanceRepo.GetByName(vm.Name) ?? throw new InvalidOperationException($"No instance artifact found for VM: {vm.Name} [{instanceId}]");

        // 2. Ensure VM is off, delete VM from Hyper-V by GUID.
        try
        {
            _vmLifecycle.Delete(instanceId, force: force); // Change VmLifecycleService.Delete to take GUID.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete VM from Hyper-V for {Name} [{Id}]", vm.Name, instanceId);
        }

        // 3. Delete VHDX disk(s) using VhdxManager by GUID.
        try
        {
            _vhdxManager.DeleteVhdx(instanceId); // Change VhdxManager.DeleteVhdx to take GUID.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete VHDX for {Name} [{Id}]", vm.Name, instanceId);
        }

        // 4. Delete instance artifacts (cloud-init ISO, metadata, dir).
        try
        {
            if (System.IO.Directory.Exists(instance.Path))
                System.IO.Directory.Delete(instance.Path, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete artifact directory: {Path}", instance.Path);
        }

        // 5. Remove from VM helper cache (by GUID).
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
        // 1. Lookup VM and instance artifact by GUID.
        var vm = _vmHelper.GetVm(instanceId) ?? throw new InvalidOperationException($"VM not found for InstanceId: {instanceId}");
        var instance = _instanceRepo.GetByName(vm.Name) ?? throw new InvalidOperationException($"No instance artifact found for VM: {vm.Name} [{instanceId}]");

        // 2. Generate unique temp folder for export.
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm-ss");
        string archiveName = $"{instance.Name}_{instanceId}_{timestamp}.zip";
        string archiveDir = Path.Combine(_config.Var, "export");
        string archiveUri = Path.Combine(archiveDir, archiveName);
        string tmpFolder = Path.Combine(_config.Tmp, Path.GetFileNameWithoutExtension(archiveName));

        if (Directory.Exists(tmpFolder))
            throw new InvalidOperationException($"Temp folder already exists: {tmpFolder}");

        try
        {
            // 3. Copy instance artifacts to temp.
            _instanceRepo.StageInstanceForExport(instance.Name, tmpFolder);

            // 4. Export VM (Hyper-V) to the same temp folder.
            _provisioningService.ExportVm(instanceId, tmpFolder);

            // 5. Use FromArchiveUri for export metadata.
            var export = Export.FromArchiveUri(archiveUri);

            // 6. Create archive from temp.
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
            // 7. Clean up temp directory if it still exists.
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

}
