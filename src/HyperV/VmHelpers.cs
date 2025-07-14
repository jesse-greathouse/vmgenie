using System;
using System.IO;

using Microsoft.Extensions.Logging;

namespace VmGenie.HyperV;

/// <summary>
/// Helper service for VM operations that mutate state or perform provisioning tasks.
/// Registered as a singleton in DI.
/// </summary>
public class VmHelpers(VmRepository repo, ILogger<VmHelpers> logger)
{
    private readonly VmRepository _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    private readonly ILogger<VmHelpers> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Creates a copy of the base VM's VHDX file with a new name for the new instance.
    /// </summary>
    /// <param name="baseVmGuid">GUID of the base VM to clone.</param>
    /// <param name="instanceName">Name of the new VM instance.</param>
    /// <returns>Path to the new VHDX file.</returns>
    public string CloneBaseVhdx(string baseVmGuid, string instanceName)
    {
        if (string.IsNullOrWhiteSpace(baseVmGuid))
            throw new ArgumentNullException(nameof(baseVmGuid));
        if (string.IsNullOrWhiteSpace(instanceName))
            throw new ArgumentNullException(nameof(instanceName));

        var baseVm = _repo.GetById(baseVmGuid)
            ?? throw new InvalidOperationException($"Base VM with GUID '{baseVmGuid}' not found.");

        if (string.IsNullOrWhiteSpace(baseVm.HostResourcePath))
            throw new InvalidOperationException($"Base VM '{baseVm.Name}' does not have a valid HostResourcePath.");

        string baseVhdxPath = baseVm.HostResourcePath;

        if (!File.Exists(baseVhdxPath))
            throw new FileNotFoundException($"Base VHDX file not found: {baseVhdxPath}");

        string baseDir = Path.GetDirectoryName(baseVhdxPath)!;
        string newVhdxName = $"{instanceName}.vhdx";
        string newVhdxPath = Path.Combine(baseDir, newVhdxName);

        if (File.Exists(newVhdxPath))
            throw new InvalidOperationException($"Target VHDX already exists: {newVhdxPath}");

        _logger.LogInformation("Copying base VHDX '{BaseVhdxPath}' to '{NewVhdxPath}'…", baseVhdxPath, newVhdxPath);

        File.Copy(baseVhdxPath, newVhdxPath);

        _logger.LogInformation("Created new VHDX at: {NewVhdxPath}", newVhdxPath);

        return newVhdxPath;
    }
}
