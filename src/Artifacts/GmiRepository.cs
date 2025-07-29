using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;

namespace VmGenie.Artifacts;

public class GmiRepository
{
    private readonly string _gmiDir;
    private readonly ILogger<GmiRepository> _logger;

    public GmiRepository(Config config, ILogger<GmiRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gmiDir = config.GmiDir ?? throw new ArgumentNullException(nameof(config.GmiDir));

        if (!Directory.Exists(_gmiDir))
        {
            _logger.LogError("GMI directory not found: {Path}", _gmiDir);
            throw new DirectoryNotFoundException($"GMI directory not found: {_gmiDir}");
        }

        _logger.LogInformation("GmiRepository initialized at {Path}", _gmiDir);
    }

    public IEnumerable<Gmi> GetAll()
    {
        var files = Directory.EnumerateFiles(_gmiDir, "GMI-*.zip", SearchOption.TopDirectoryOnly);

        foreach (var file in files)
        {
            Gmi? gmi = null;
            try { gmi = Gmi.FromArchiveUri(file); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping file (not a valid GMI archive): {Name}", Path.GetFileName(file));
            }
            if (gmi != null)
                yield return gmi;
        }
    }

    public Gmi GetByName(string archiveName)
    {
        var file = Directory.EnumerateFiles(_gmiDir, "GMI-*.zip", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), archiveName, StringComparison.OrdinalIgnoreCase));

        if (file == null)
        {
            _logger.LogError("GMI archive not found: {Name}", archiveName);
            throw new FileNotFoundException($"GMI archive not found: {archiveName}");
        }
        try
        {
            var gmi = Gmi.FromArchiveUri(file);
            return gmi;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File found but could not load GMI archive: {Name}", archiveName);
            throw;
        }
    }

    public bool Exists(string archiveName)
    {
        return Directory.EnumerateFiles(_gmiDir, "GMI-*.zip", SearchOption.TopDirectoryOnly)
            .Any(f => string.Equals(Path.GetFileName(f), archiveName, StringComparison.OrdinalIgnoreCase));
    }
}
