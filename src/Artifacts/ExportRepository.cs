using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;

namespace VmGenie.Artifacts;

public class ExportRepository
{
    private readonly string _varExportPath;
    private readonly ILogger<ExportRepository> _logger;

    public ExportRepository(Config config, ILogger<ExportRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _varExportPath = Path.Combine(config.Var, "export");

        if (!Directory.Exists(_varExportPath))
        {
            _logger.LogError("var/export directory not found: {Path}", _varExportPath);
            throw new DirectoryNotFoundException($"var/export directory not found: {_varExportPath}");
        }

        _logger.LogInformation("ExportRepository initialized at {Path}", _varExportPath);
    }

    public enum ExportSortOrder
    {
        FileNameAsc,
        FileNameDesc,
        CreatedDateAsc,
        CreatedDateDesc
    }

    /// <summary>
    /// Returns all Export archives found in var/export, optionally filtering by instanceName and/or instanceId.
    /// </summary>
    public IEnumerable<Export> GetAll(
        string? instanceName = null,
        string? instanceId = null,
        ExportSortOrder sortOrder = ExportSortOrder.FileNameAsc
    )
    {
        var files = Directory.EnumerateFiles(
            _varExportPath,
            ComposeGlobPattern(instanceName, instanceId),
            SearchOption.TopDirectoryOnly);

        var exports = files
            .Select(file =>
            {
                try { return Export.FromArchiveUri(file); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipping file (not a valid Export archive): {Name}", Path.GetFileName(file));
                    return null;
                }
            })
            .Where(e => e is not null)!
            .Select(e => e!);

        return SortMap[sortOrder](exports);
    }

    /// <summary>
    /// Returns a single Export archive by its archive file name (case-insensitive).
    /// Throws if not found or invalid.
    /// </summary>
    public Export GetByName(string archiveName)
    {
        var file = Directory.EnumerateFiles(_varExportPath, "*.zip", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), archiveName, StringComparison.OrdinalIgnoreCase));

        if (file == null)
        {
            _logger.LogError("Export archive not found: {Name}", archiveName);
            throw new FileNotFoundException($"Export archive not found: {archiveName}");
        }

        try
        {
            var export = Export.FromArchiveUri(file);
            return export;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File found but could not load Export archive: {Name}", archiveName);
            throw;
        }
    }

    public static readonly Dictionary<ExportSortOrder, Func<IEnumerable<Export>, IOrderedEnumerable<Export>>> SortMap =
    new()
    {
        { ExportSortOrder.FileNameAsc, exports => exports.OrderBy(e => e.ArchiveName) },
        { ExportSortOrder.FileNameDesc, exports => exports.OrderByDescending(e => e.ArchiveName) },
        { ExportSortOrder.CreatedDateAsc, exports => exports.OrderBy(e => e.CreatedDate) },
        { ExportSortOrder.CreatedDateDesc, exports => exports.OrderByDescending(e => e.CreatedDate) },
    };

    /// <summary>
    /// Returns true if an export archive with the given file name exists.
    /// </summary>
    public bool Exists(string archiveName)
    {
        return Directory.EnumerateFiles(_varExportPath, "*.zip", SearchOption.TopDirectoryOnly)
            .Any(f => string.Equals(Path.GetFileName(f), archiveName, StringComparison.OrdinalIgnoreCase));
    }

    private static string ComposeGlobPattern(string? instanceName, string? instanceId)
    {
        return !string.IsNullOrEmpty(instanceName) && !string.IsNullOrEmpty(instanceId)
            ? $"{instanceName}_{instanceId}_*.zip"
            : !string.IsNullOrEmpty(instanceName)
            ? $"{instanceName}_*.zip"
            : !string.IsNullOrEmpty(instanceId) ? $"*_{instanceId}_*.zip" : "*.zip";
    }
}
