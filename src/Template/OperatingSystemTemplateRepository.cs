using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;

namespace VmGenie.Template;

public class OperatingSystemTemplateRepository
{
    private readonly string _etcCloudPath;
    private readonly ILogger<OperatingSystemTemplateRepository> _logger;

    public OperatingSystemTemplateRepository(Config config, ILogger<OperatingSystemTemplateRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _etcCloudPath = Path.Combine(config.Etc, "cloud");

        if (!Directory.Exists(_etcCloudPath))
        {
            _logger.LogError("etc/cloud directory not found: {Path}", _etcCloudPath);
            throw new DirectoryNotFoundException($"etc/cloud directory not found: {_etcCloudPath}");
        }

        _logger.LogInformation("OperatingSystemTemplateRepository initialized at {Path}", _etcCloudPath);
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
                    _logger.LogInformation("Found valid OS version: {OS} {Version}", osName, versionName);
                }
                else
                {
                    _logger.LogWarning("Skipping version {Version} of OS {OS} — no seed-data.", versionName, osName);
                }
            }

            result.Add(new OperatingSystemTemplate(osName, versions));
        }

        _logger.LogInformation("Total operating systems found: {Count}", result.Count);
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
        var valid = versions.Contains(version, StringComparer.OrdinalIgnoreCase);
        if (!valid)
        {
            _logger.LogWarning("Invalid OS version requested: {OS} {Version}", osName, version);
        }

        return valid;
    }
}
