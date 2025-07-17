using System;

namespace VmGenie.Artifacts;

public class Instance(
    string name,
    string os,
    string version,
    string baseVm,
    string vmSwitch,
    bool mergeAvhdx,
    string path)
{
    public string Name { get; } = name;
    public string OperatingSystem { get; } = os;
    public string Version { get; } = version;
    public string BaseVm { get; } = baseVm;
    public string VmSwitch { get; } = vmSwitch;
    public bool MergeAvhdx { get; } = mergeAvhdx;
    public string Path { get; } = path; // path to instance folder
}
