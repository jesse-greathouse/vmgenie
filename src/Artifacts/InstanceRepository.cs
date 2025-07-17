using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.Extensions.Logging;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace VmGenie.Artifacts;

public class InstanceRepository
{
    private readonly string _varCloudPath;
    private readonly ILogger<InstanceRepository> _logger;
    private readonly IDeserializer _yamlDeserializer;

    public InstanceRepository(Config config, ILogger<InstanceRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _varCloudPath = Path.Combine(config.Var, "cloud");

        if (!Directory.Exists(_varCloudPath))
        {
            _logger.LogError("var/cloud directory not found: {Path}", _varCloudPath);
            throw new DirectoryNotFoundException($"var/cloud directory not found: {_varCloudPath}");
        }

        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        _logger.LogInformation("InstanceRepository initialized at {Path}", _varCloudPath);
    }

    public List<Instance> GetAll()
    {
        var instances = new List<Instance>();

        foreach (var instanceDir in Directory.GetDirectories(_varCloudPath))
        {
            var name = Path.GetFileName(instanceDir);
            var metadataPath = Path.Combine(instanceDir, "metadata.yml");

            if (!File.Exists(metadataPath))
            {
                _logger.LogWarning("Skipping instance {Name} — no metadata.yml found.", name);
                continue;
            }

            try
            {
                var metadataText = File.ReadAllText(metadataPath);
                var metadata = _yamlDeserializer.Deserialize<Metadata>(metadataText);

                instances.Add(new Instance(
                    name,
                    metadata.Os,
                    metadata.Version,
                    metadata.BaseVm,
                    metadata.VmSwitch,
                    metadata.MergeAvhdx,
                    instanceDir
                ));

                _logger.LogInformation("Loaded instance: {Name}", name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load metadata for instance: {Name}", name);
            }
        }

        _logger.LogInformation("Total instances found: {Count}", instances.Count);
        return instances;
    }

    public List<string> GetInstanceNames()
    {
        return [.. GetAll().ConvertAll(i => i.Name)];
    }

    public Instance? GetByName(string name)
    {
        return GetAll().Find(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public bool Exists(string name)
    {
        return GetByName(name) != null;
    }

    private class Metadata
    {
        public string Os { get; set; } = "";
        public string Version { get; set; } = "";
        public string BaseVm { get; set; } = "";
        public string VmSwitch { get; set; } = "";
        public bool MergeAvhdx { get; set; }
    }
}
