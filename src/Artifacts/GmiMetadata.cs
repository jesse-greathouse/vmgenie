using System;
using System.IO;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace VmGenie.Artifacts;

public class GmiMetadata
{
    public string GmiVersion { get; set; } = "1.0";
    public string Os { get; set; } = "";
    public string Version { get; set; } = "";
    public string Created { get; set; } = "";
    public string Updated { get; set; } = "";
    public string VmName { get; set; } = "";
    public int HypervGeneration { get; set; } = 2;
    public string Maintainer { get; set; } = "";
    public string MaintainerEmail { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string Description { get; set; } = "";
    public string ChecksumSha256 { get; set; } = "";

    // Load from YAML file
    public static GmiMetadata Load(string path)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        using var reader = new StreamReader(path);
        var metadata = deserializer.Deserialize<GmiMetadata>(reader);
        metadata.EnsureDefaults();
        return metadata;
    }

    public void EnsureDefaults()
    {
        if (string.IsNullOrWhiteSpace(GmiVersion)) GmiVersion = "1.0";
        if (string.IsNullOrWhiteSpace(Created)) Created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        if (string.IsNullOrWhiteSpace(Updated)) Updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        if (HypervGeneration == 0) HypervGeneration = 2;
        if (string.IsNullOrWhiteSpace(VmName) && !string.IsNullOrWhiteSpace(Os) && !string.IsNullOrWhiteSpace(Version))
            VmName = $"GMI {Os} {Version}";
    }

    // Write to YAML file
    public void Save(string path)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        using var writer = new StreamWriter(path);
        serializer.Serialize(writer, this);
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha256.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
