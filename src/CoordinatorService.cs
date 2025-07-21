using System;
using Microsoft.Extensions.Logging;
using VmGenie.HyperV;
using VmGenie.Artifacts;

namespace VmGenie;

public class CoordinatorService
{
    private readonly VmLifecycleService _vmLifecycle;
    private readonly VhdxManager _vhdxManager;
    private readonly InstanceRepository _instanceRepo;
    private readonly VmHelper _vmHelper;
    private readonly ILogger<CoordinatorService> _logger;

    public CoordinatorService(
        VmLifecycleService vmLifecycle,
        VhdxManager vhdxManager,
        InstanceRepository instanceRepo,
        VmHelper vmHelper,
        ILogger<CoordinatorService> logger)
    {
        _vmLifecycle = vmLifecycle ?? throw new ArgumentNullException(nameof(vmLifecycle));
        _vhdxManager = vhdxManager ?? throw new ArgumentNullException(nameof(vhdxManager));
        _instanceRepo = instanceRepo ?? throw new ArgumentNullException(nameof(instanceRepo));
        _vmHelper = vmHelper ?? throw new ArgumentNullException(nameof(vmHelper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Deletes the VM, VHDX disk, and all related artifacts for the given VM GUID.
    /// </summary>
    public void DeleteInstance(string instanceId, bool force = false)
    {
        // 1. Lookup VM and instance by GUID.
        var vm = _vmHelper.GetVm(instanceId); // Throws if not found.
        var instance = _instanceRepo.GetByName(vm.Name);
        if (instance == null)
            throw new InvalidOperationException($"No instance artifact found for VM: {vm.Name} [{instanceId}]");

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

    // ...future orchestration methods
}
