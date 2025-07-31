using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;

using YamlDotNet.Serialization;

namespace VmGenie.Artifacts;

/// <summary>
/// Provides access to GmiPackage objects as described in gmi-repository.yml.
/// </summary>
public class GmiPackageRepository
{
    public const string RepositoryFileName = "gmi-repository.yml";
    public const string RepositoryDistFileName = "gmi-repository.dist.yml";
    private readonly string _repoPath;
    private readonly string _repoDistPath;
    private readonly ILogger<GmiPackageRepository> _logger;
    private readonly Dictionary<string, List<GmiPackage>> _repo;

    public GmiPackageRepository(Config config, ILogger<GmiPackageRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _repoPath = Path.Combine(config.ApplicationDir, RepositoryFileName);
        _repoDistPath = Path.Combine(config.ApplicationDir, RepositoryDistFileName);

        EnsureRepoFileExists();

        _repo = LoadRepoFromFile(_repoPath, _logger);
    }

    private static Dictionary<string, List<GmiPackage>> LoadRepoFromFile(string path, ILogger logger)
    {
        if (!File.Exists(path))
        {
            logger.LogError("GMI repository YAML not found at {Path}", path);
            throw new FileNotFoundException("GMI repository YAML not found", path);
        }

        logger.LogInformation("Loading GMI repository from {Path}", path);

        var deserializer = new DeserializerBuilder()
            .Build();

        try
        {
            using var reader = new StreamReader(path);
            var repo = deserializer.Deserialize<Dictionary<string, List<GmiPackage>>>(reader);
            int packageCount = repo?.Values.SelectMany(x => x).Count() ?? 0;
            logger.LogInformation("Loaded {Count} GMI package{Suffix} from repository.",
                packageCount, packageCount == 1 ? "" : "s");
            return repo ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse GMI repository file at {Path}", path);
            throw;
        }
    }

    /// <summary>
    /// Gets all available keys, e.g., "Debian-12".
    /// </summary>
    public IEnumerable<string> GetKeys()
    {
        _logger.LogDebug("Enumerating all GMI repository keys ({Count})", _repo.Keys.Count);
        return _repo.Keys;
    }

    /// <summary>
    /// Returns all packages for the given key.
    /// </summary>
    public IReadOnlyList<GmiPackage> GetPackagesByKey(string key)
    {
        if (_repo.TryGetValue(key, out var list))
        {
            _logger.LogDebug("Found {Count} package(s) for key '{Key}'", list.Count, key);
            return list;
        }
        _logger.LogWarning("No packages found for key '{Key}'", key);
        return [];
    }

    /// <summary>
    /// Returns all GMI packages in the repository.
    /// </summary>
    public IEnumerable<GmiPackage> GetAllPackages()
    {
        int count = _repo.Values.SelectMany(x => x).Count();
        _logger.LogDebug("Returning all GMI packages in the repository ({Count})", count);
        return _repo.Values.SelectMany(x => x);
    }

    /// <summary>
    /// Looks up a GmiPackage by OS and version, case-insensitive.
    /// </summary>
    public GmiPackage? FindByOsAndVersion(string os, string version)
    {
        var key = $"{os}-{version}";
        if (_repo.TryGetValue(key, out var list) && list.Count > 0)
        {
            _logger.LogInformation("Located GMI package for {OS} {Version}: {Url}", os, version, list[0].Url);
            return list[0];
        }
        _logger.LogWarning("No GMI package found for OS='{OS}', Version='{Version}' (key '{Key}')", os, version, key);
        return null;
    }

    /// <summary>
    /// Gets all packages for a given OS.
    /// </summary>
    public IEnumerable<GmiPackage> GetPackagesForOs(string os)
    {
        var results = _repo
            .Where(pair => pair.Key.StartsWith(os + "-", StringComparison.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Value)
            .ToList();
        _logger.LogDebug("Found {Count} GMI package(s) for OS '{OS}'", results.Count, os);
        return results;
    }

    /// <summary>
    /// Returns a collection of GmiPackage objects from the dist manifest that are outdated in the main repo.
    /// A package is "outdated" if the URL matches but CRC64 differs.
    /// </summary>
    public IReadOnlyList<GmiPackage> GetOutdatedPackages()
    {
        _logger.LogInformation("Checking for outdated GMI packages using dist file: {ReferenceRepo}", _repoDistPath);

        if (!File.Exists(_repoDistPath))
        {
            _logger.LogError("Reference GMI repository dist YAML not found at {Path}", _repoDistPath);
            throw new FileNotFoundException("Reference GMI repository dist YAML not found", _repoDistPath);
        }

        var deserializer = new DeserializerBuilder()
            .Build();

        Dictionary<string, List<GmiPackage>> distRepo;
        try
        {
            using var reader = new StreamReader(_repoDistPath);
            distRepo = deserializer.Deserialize<Dictionary<string, List<GmiPackage>>>(reader) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse GMI dist repository file at {Path}", _repoDistPath);
            throw;
        }

        var outdated = new List<GmiPackage>();

        // Build a lookup: Url → GmiPackage for current repo (flattened)
        var localByUrl = GetAllPackages().ToDictionary(p => p.Url, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in distRepo.Values.SelectMany(x => x))
        {
            if (string.IsNullOrWhiteSpace(entry.Url))
                continue; // Defensive: skip entries without URL

            if (localByUrl.TryGetValue(entry.Url, out var local))
            {
                if (!string.Equals(local.Crc64Nvme, entry.Crc64Nvme, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Outdated GMI package found: {Name} ({Url}) - CRC {LocalCrc} → {DistCrc}",
                        entry.Name, entry.Url, local.Crc64Nvme, entry.Crc64Nvme);
                    outdated.Add(entry);
                }
            }
        }

        _logger.LogInformation("Detected {Count} outdated GMI package(s).", outdated.Count);
        return outdated;
    }

    /// <summary>
    /// Updates all GmiPackage entries in gmi-repository.yml that match the given URL with the supplied updated object,
    /// then rewrites the file. Returns the number of replacements made.
    /// </summary>
    /// <param name="url">The URL of the package to update.</param>
    /// <param name="updated">The updated GmiPackage object (usually from dist).</param>
    public int UpdatePackageByUrl(string url, GmiPackage updated)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentNullException(nameof(url));
        if (updated == null)
            throw new ArgumentNullException(nameof(updated));

        // Load current repository (always reload for consistency)
        var deserializer = new DeserializerBuilder()
            .Build();

        Dictionary<string, List<GmiPackage>> currentRepo;
        using (var reader = new StreamReader(_repoPath))
            currentRepo = deserializer.Deserialize<Dictionary<string, List<GmiPackage>>>(reader) ?? [];

        int replacements = 0;

        // Replace all packages with matching Url
        foreach (var kvp in currentRepo)
        {
            for (int i = 0; i < kvp.Value.Count; i++)
            {
                if (string.Equals(kvp.Value[i].Url, url, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Updating GMI package for URL '{Url}' in key '{Key}'.", url, kvp.Key);
                    kvp.Value[i] = updated;
                    replacements++;
                }
            }
        }

        if (replacements > 0)
        {
            // Write the updated repo back to the file
            var serializer = new SerializerBuilder()
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
                .Build();

            using (var writer = new StreamWriter(_repoPath, false))
            {
                serializer.Serialize(writer, currentRepo);
            }
            _logger.LogInformation("Updated {Count} GMI package(s) in {RepoFile}.", replacements, _repoPath);
        }
        else
        {
            _logger.LogWarning("No GMI package found with URL '{Url}' in {RepoFile}.", url, _repoPath);
        }

        return replacements;
    }

    private void EnsureRepoFileExists()
    {
        if (!File.Exists(_repoPath))
        {
            if (File.Exists(_repoDistPath))
            {
                _logger.LogWarning("GMI repository YAML not found at {Path}. Copying default from {DistPath}.", _repoPath, _repoDistPath);
                File.Copy(_repoDistPath, _repoPath);
                _logger.LogInformation("Default GMI repository copied to {Path}.", _repoPath);
            }
            else
            {
                _logger.LogError("Neither GMI repository YAML nor default dist file found ({Path}, {DistPath}).", _repoPath, _repoDistPath);
                throw new FileNotFoundException(
                    $"Neither {RepositoryFileName} nor {RepositoryDistFileName} found in application directory.",
                    _repoPath
                );
            }
        }
    }
}
