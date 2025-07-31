using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using VmGenie.Artifacts;

namespace VmGenie.EventHandlers;

/// <summary>
/// Handles the "gmi-package" command, for remote GMI package operations.
/// </summary>
public class GmiPackageHandler(GmiPackageRepository packageRepo) : IEventHandler
{
    private readonly GmiPackageRepository _repo = packageRepo ?? throw new ArgumentNullException(nameof(packageRepo));

    public async Task HandleAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        string action = GetAction(evt);
        object? data = action switch
        {
            "list" => HandleList(evt),
            "update" => await HandleUpdateAsync(evt, ctx, token),
            "outdated" => HandleOutdated(),
            _ => HandleHelp(),
        };
        if (data is null) return;

        var response = new EventResponse(
            evt.Id,
            evt.Command,
            EventStatus.OK,
            data
        );
        await ctx.SendResponseAsync(response, token);
    }

#pragma warning disable IDE0046 // Convert to conditional expression
    // Doing this as a conditional expression is very hard to read.
    private static string GetAction(Event evt)
    {
        if (evt.Parameters.TryGetValue("action", out object? actionObj) &&
            actionObj is JsonElement elem &&
            elem.ValueKind == JsonValueKind.String)
        {
            return elem.GetString()?.Trim().ToLowerInvariant() ?? "help";
        }
        return "help";
    }
#pragma warning restore IDE0046

    private object HandleList(Event evt)
    {
        // Parameter precedence: key → (os+version) → os → all
        if (evt.Parameters.TryGetValue("key", out var keyObj) &&
            keyObj is JsonElement keyElem &&
            keyElem.ValueKind == JsonValueKind.String)
        {
            string key = keyElem.GetString()!;
            return new { key, packages = _repo.GetPackagesByKey(key) };
        }

        if (evt.Parameters.TryGetValue("os", out var osObj) &&
            osObj is JsonElement osElem &&
            osElem.ValueKind == JsonValueKind.String)
        {
            string os = osElem.GetString()!;
            if (evt.Parameters.TryGetValue("version", out var verObj) &&
                verObj is JsonElement verElem &&
                verElem.ValueKind == JsonValueKind.String)
            {
                string version = verElem.GetString()!;
                var pkg = _repo.FindByOsAndVersion(os, version);
                return new { os, version, package = pkg };
            }
            else
            {
                return new { os, packages = _repo.GetPackagesForOs(os) };
            }
        }

        // Default: all packages
        return new { packages = _repo.GetAllPackages() };
    }

    private async Task<object?> HandleUpdateAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        if (!evt.Parameters.TryGetValue("url", out var urlObj) ||
            urlObj is not JsonElement urlElem ||
            urlElem.ValueKind != JsonValueKind.String)
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, "Missing or invalid 'url' parameter for update action."),
                token);
            return null;
        }
        string url = urlElem.GetString()!;
        if (string.IsNullOrWhiteSpace(url))
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, "'url' parameter cannot be empty."),
                token);
            return null;
        }

        if (!evt.Parameters.TryGetValue("package", out var pkgObj) ||
            pkgObj is not JsonElement pkgElem ||
            pkgElem.ValueKind != JsonValueKind.Object)
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, "Missing or invalid 'package' parameter for update action."),
                token);
            return null;
        }

        // Deserialize GmiPackage from the supplied object
        GmiPackage? updated;
        try
        {
            updated = pkgElem.Deserialize<GmiPackage>();
        }
        catch (Exception ex)
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, $"Failed to parse 'package' parameter as GmiPackage: {ex.Message}"),
                token);
            return null;
        }

        if (updated is null)
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, "'package' parameter could not be deserialized as a GmiPackage."),
                token);
            return null;
        }

        try
        {
            _repo.UpdatePackageByUrl(url, updated); // Assume this is now void
        }
        catch (Exception ex)
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, $"Failed to update package: {ex.Message}"),
                token);
            return null;
        }

        return new
        {
            url,
            updated,
            status = "updated"
        };
    }

    private object HandleOutdated()
    {
        var outdated = _repo.GetOutdatedPackages();
        return new { outdated, count = outdated.Count };
    }

    private static object HandleHelp()
    {
        var help = new Dictionary<string, object>
        {
            ["description"] = "The 'gmi-package' command manages the remote GMI package repository (gmi-repository.yml).",
            ["actions"] = new[]
            {
                new Dictionary<string, string> {
                    ["action"] = "list",
                    ["description"] = "Lists available GMI packages. Parameters: key (optional), os (optional), version (optional)."
                },
                new Dictionary<string, string> {
                    ["action"] = "update",
                    ["description"] = "Updates a GMI package in the repository by URL. Parameters: url (required), package (required, GmiPackage object)."
                },
                new Dictionary<string, string> {
                    ["action"] = "outdated",
                    ["description"] = "Lists GMI packages that are outdated compared to the dist manifest."
                },
                new Dictionary<string, string> {
                    ["action"] = "help",
                    ["description"] = "Displays this help message."
                }
            },
            ["exampleRequests"] = new[]
            {
                new Dictionary<string, object> {
                    ["action"] = "list",
                    ["parameters"] = new { action = "list" }
                },
                new Dictionary<string, object> {
                    ["action"] = "list-by-os",
                    ["parameters"] = new { action = "list", os = "Ubuntu" }
                },
                new Dictionary<string, object> {
                    ["action"] = "list-by-key",
                    ["parameters"] = new { action = "list", key = "Ubuntu-25.04" }
                },
                new Dictionary<string, object> {
                    ["action"] = "list-by-os-version",
                    ["parameters"] = new { action = "list", os = "Ubuntu", version = "25.04" }
                },
                new Dictionary<string, object> {
                    ["action"] = "update",
                    ["parameters"] = new { action = "update", url = "https://vmgenie-gmi.s3.us-east-2.amazonaws.com/GMI-Ubuntu-25.04.zip", package = new { /* GmiPackage JSON */ } }
                },
                new Dictionary<string, object> {
                    ["action"] = "outdated",
                    ["parameters"] = new { action = "outdated" }
                }
            }
        };
        return new Dictionary<string, object> { ["help"] = help };
    }
}
