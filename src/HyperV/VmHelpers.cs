using System;
using System.IO;

namespace VmGenie.HyperV;

/// <summary>
/// Helper methods for VM operations that mutate state or perform provisioning tasks.
/// </summary>
public static class VmHelpers
{
    /// <summary>
    /// Creates a copy of the base VM's VHDX file with a new name for the new instance.
    /// </summary>
    /// <param name="baseVmGuid">GUID of the base VM to clone.</param>
    /// <param name="instanceName">Name of the new VM instance.</param>
    /// <returns>The GUID of the new VHDX file.</returns>
    public static Guid CloneBaseVhdx(string baseVmGuid, string instanceName)
    {
        if (string.IsNullOrWhiteSpace(baseVmGuid))
            throw new ArgumentNullException(nameof(baseVmGuid));
        if (string.IsNullOrWhiteSpace(instanceName))
            throw new ArgumentNullException(nameof(instanceName));

        // Use repository to locate the base VM
        var repo = new VmRepository();
        var baseVm = repo.GetById(baseVmGuid)
            ?? throw new InvalidOperationException($"Base VM with GUID '{baseVmGuid}' not found.");

        if (baseVm.HostResourcePath == null)
            throw new InvalidOperationException($"Base VM '{baseVm.Name}' does not have a valid HostResourcePath.");

        string baseVhdxPath = baseVm.HostResourcePath;

        if (!File.Exists(baseVhdxPath))
            throw new FileNotFoundException($"Base VHDX file not found: {baseVhdxPath}");

        // Build new file name
        string baseDir = Path.GetDirectoryName(baseVhdxPath)!;
        string newVhdxName = $"{instanceName}.vhdx";
        string newVhdxPath = Path.Combine(baseDir, newVhdxName);

        if (File.Exists(newVhdxPath))
            throw new InvalidOperationException($"Target VHDX already exists: {newVhdxPath}");

        Console.WriteLine($"Copying base VHDX '{baseVhdxPath}' to '{newVhdxPath}'…");
        File.Copy(baseVhdxPath, newVhdxPath);

        Guid newGuid = Guid.NewGuid();  // Or optionally, hash the file name deterministically

        Console.WriteLine($"Created new VHDX '{newVhdxPath}' with GUID: {newGuid}");

        return newGuid;
    }
}
