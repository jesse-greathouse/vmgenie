namespace VmGenie.Template;

using System.Collections.Generic;

public class OperatingSystemTemplate(string name, List<string> versions)
{
    public string Name { get; } = name;
    public List<string> Versions { get; } = versions ?? [];
}
