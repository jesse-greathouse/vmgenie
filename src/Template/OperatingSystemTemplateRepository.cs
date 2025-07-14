namespace VmGenie.Template;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class OperatingSystemTemplateRepository
{
    private readonly string _etcCloudPath;

    public OperatingSystemTemplateRepository(Config config)
    {
        _etcCloudPath = Path.Combine(config.Etc, "cloud");

        if (!Directory.Exists(_etcCloudPath))
        {
            throw new DirectoryNotFoundException($"etc/cloud directory not found: {_etcCloudPath}");
        }
    }

    public List<OperatingSystemTemplate> GetAll()
    {
        var result = new List<OperatingSystemTemplate>();

        foreach (var osDir in Directory.GetDirectories(_etcCloudPath))
        {
            var osName = Path.GetFileName(osDir);
            var versions = new List<string>();

            foreach (var versionDir in Directory.GetDirectories(osDir))
            {
                var versionName = Path.GetFileName(versionDir);

                // Validate that seed-data exists for this version
                var seedDataPath = Path.Combine(versionDir, "seed-data");
                if (Directory.Exists(seedDataPath))
                {
                    versions.Add(versionName);
                }
            }

            result.Add(new OperatingSystemTemplate(osName, versions));
        }

        return result;
    }

    public List<string> GetOperatingSystems()
    {
        return [.. GetAll().Select(t => t.Name)];
    }

    public List<string> GetVersionsFor(string osName)
    {
        var template = GetAll()
            .FirstOrDefault(t => string.Equals(t.Name, osName, StringComparison.OrdinalIgnoreCase));

        return template?.Versions ?? [];
    }

    public bool IsValidOsVersion(string osName, string version)
    {
        var versions = GetVersionsFor(osName);
        return versions.Contains(version, StringComparer.OrdinalIgnoreCase);
    }
}
