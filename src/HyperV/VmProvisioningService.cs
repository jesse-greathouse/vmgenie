using System;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;

namespace VmGenie.HyperV;

public class VmProvisioningService(VmHelper vmHelper, VhdxManager vhdxManager, ILogger<VmProvisioningService> logger, Config config)
{
    private readonly VmHelper _vmHelper = vmHelper ?? throw new ArgumentNullException(nameof(vmHelper));
    private readonly VhdxManager _vhdxManager = vhdxManager ?? throw new ArgumentNullException(nameof(vhdxManager));
    private readonly ILogger<VmProvisioningService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Config _config = config ?? throw new ArgumentNullException(nameof(config));

    public Vm ProvisionVm(string baseVmGuid, string instanceName, string vmSwitchGuid, bool mergeDifferencingDisk = false)
    {
        const string ns = @"root\virtualization\v2";
        using var session = CimSession.Create(null);

        _logger.LogInformation("Starting VM provisioning: BaseVM={BaseGuid}, Instance={InstanceName}, Switch={SwitchGuid}, MergeDiff={Merge}",
            baseVmGuid, instanceName, vmSwitchGuid, mergeDifferencingDisk);

        string artifactDir = Path.Combine(_config.CloudDir, instanceName);
        string isoPath = Path.Combine(artifactDir, "seed.iso");

        if (!File.Exists(isoPath))
            throw new InvalidOperationException($"cloud-init ISO not found at: {isoPath}");

        string clonedVhdx = _vhdxManager.CloneBaseVhdx(baseVmGuid, instanceName, mergeDifferencingDisk);

        // Create new VM
        var mgmtService = session.QueryInstances(ns, "WQL", "SELECT * FROM Msvm_VirtualSystemManagementService")
            .FirstOrDefault() ?? throw new InvalidOperationException("Failed to locate VirtualSystemManagementService.");

        var vmSettings = new CimMethodParametersCollection
        {
            CimMethodParameter.Create("ElementName", instanceName, CimType.String, CimFlags.None)
        };

        var createVmResult = session.InvokeMethod(mgmtService, "DefineVirtualSystem", vmSettings);
        var vmInstance = createVmResult.OutParameters["ResultingSystem"]?.Value as CimInstance
            ?? throw new InvalidOperationException("Failed to create new VM.");

        _logger.LogInformation("Created VM: {VmPath}", vmInstance.CimSystemProperties.Path);

        // Attach cloned VHDX
        AttachVhdx(session, vmInstance, clonedVhdx);

        // Attach ISO
        AttachIso(session, vmInstance, isoPath);

        // Connect to VM Switch
        ConnectVmToSwitch(session, vmInstance, vmSwitchGuid);

        _logger.LogInformation("VM provisioning completed successfully: {InstanceName}", instanceName);

        // Convert to Vm DTO
        var vmDto = Vm.FromCimInstance(session, vmInstance);

        return vmDto;
    }

    private void AttachVhdx(CimSession session, CimInstance vmInstance, string vhdxPath)
    {
        const string ns = @"root\virtualization\v2";

        var disk = CreateResourceAllocationSettingData(session, ns,
            "Microsoft:Hyper-V:Synthetic Disk Drive", 17, vhdxPath);

        ApplyResource(session, vmInstance, disk);
        _logger.LogInformation("Attached VHDX to VM: {VhdxPath}", vhdxPath);
    }

    private void AttachIso(CimSession session, CimInstance vmInstance, string isoPath)
    {
        const string ns = @"root\virtualization\v2";

        var cdrom = CreateResourceAllocationSettingData(session, ns,
            "Microsoft:Hyper-V:CD/DVD Drive", null, isoPath);

        ApplyResource(session, vmInstance, cdrom);
        _logger.LogInformation("Attached cloud-init ISO to VM: {IsoPath}", isoPath);
    }

    private void ConnectVmToSwitch(CimSession session, CimInstance vmInstance, string vmSwitchGuid)
    {
        const string ns = @"root\virtualization\v2";

        var nic = CreateResourceAllocationSettingData(session, ns,
            "Microsoft:Hyper-V:Synthetic Ethernet Port", null, $"\\\\\\\\?\\root\\virtualization\\v2\\Msvm_VirtualSwitch.Id=\"{vmSwitchGuid}\"");

        ApplyResource(session, vmInstance, nic);
        _logger.LogInformation("Connected VM to switch: {SwitchGuid}", vmSwitchGuid);
    }

    private CimInstance CreateResourceAllocationSettingData(CimSession session, string ns, string resourceType, ushort? deviceType, string hostResource)
    {
        var rasd = session.QueryInstances(ns, "WQL",
            $"SELECT * FROM Msvm_ResourceAllocationSettingData WHERE ResourceSubType='{resourceType}' AND InstanceID LIKE '%Default%'")
            .FirstOrDefault() ?? throw new InvalidOperationException($"Failed to locate RASD template for {resourceType}");

        if (deviceType.HasValue)
            rasd.CimInstanceProperties["AddressOnParent"].Value = deviceType.Value.ToString();

        rasd.CimInstanceProperties["HostResource"].Value = new[] { hostResource };

        return rasd;
    }

    private void ApplyResource(CimSession session, CimInstance vmPath, CimInstance resource)
    {
        const string ns = @"root\virtualization\v2";

        var mgmtService = session.QueryInstances(ns, "WQL", "SELECT * FROM Msvm_VirtualSystemManagementService")
            .FirstOrDefault() ?? throw new InvalidOperationException("Failed to locate VirtualSystemManagementService.");

        var inParams = new CimMethodParametersCollection
    {
        CimMethodParameter.Create("TargetVirtualSystem", vmPath, CimType.Reference, CimFlags.None),
        CimMethodParameter.Create("ResourceSettingData", new[] { resource.ToString() }, CimType.StringArray, CimFlags.None)
    };

        var result = session.InvokeMethod(mgmtService, "AddResourceSettings", inParams);

        if (result.ReturnValue?.Value is not uint returnValue)
        {
            throw new InvalidOperationException("Failed to retrieve return value from AddResourceSettings.");
        }

        if (returnValue == 0)
        {
            _logger.LogInformation("Resource added synchronously.");
        }
        else if (returnValue == 4096)
        {
            var job = result.OutParameters["Job"]?.Value as CimInstance;
            if (job == null)
            {
                throw new InvalidOperationException("AddResourceSettings returned 4096 but no job instance was provided.");
            }

            _logger.LogInformation("Resource addition started asynchronously: {JobPath}", job.CimSystemProperties.Path);
            _vmHelper.WaitForJobCompletion(session, job);
        }
        else
        {
            throw new InvalidOperationException($"AddResourceSettings failed with return code: {returnValue}");
        }
    }
}
