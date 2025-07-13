using System.IO;

using DiscUtils.Iso9660;

namespace VmGenie.Artifacts;

public class IsoCreator
{
    public static string CreateSeedIso(string seedDataDir, string outputIsoPath)
    {
        if (!Directory.Exists(seedDataDir))
            throw new DirectoryNotFoundException($"Seed data directory does not exist: {seedDataDir}");

        var builder = new CDBuilder
        {
            UseJoliet = true,
            VolumeIdentifier = "cidata"
        };

        foreach (var filePath in Directory.EnumerateFiles(seedDataDir))
        {
            var relativePath = Path.GetFileName(filePath);
            builder.AddFile(relativePath, filePath);
        }

        builder.Build(outputIsoPath);

        return outputIsoPath;
    }
}
