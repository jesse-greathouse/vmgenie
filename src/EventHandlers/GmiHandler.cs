using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VmGenie.EventHandlers;

/// <summary>
/// Handles the "gmi" command, with actions: export, help.
/// </summary>
public class GmiHandler(CoordinatorService coordinator) : IEventHandler
{
    private readonly CoordinatorService _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));

    public async Task HandleAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        string action = GetAction(evt);

        object? data;

        switch (action)
        {
            case "export":
                data = await HandleExportAsync(evt, ctx, _coordinator, token);
                if (data is null) return;
                break;

            case "import":
                data = await HandleImportAsync(evt, ctx, _coordinator, token);
                if (data is null) return;
                break;

            case "help":
            default:
                data = HandleHelp();
                break;
        }

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
            actionObj is System.Text.Json.JsonElement elem &&
            elem.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            return elem.GetString()?.Trim().ToLowerInvariant() ?? "help";
        }
        return "help";
    }
#pragma warning restore IDE0046

    private static async Task<object?> HandleExportAsync(
        Event evt,
        IWorkerContext ctx,
        CoordinatorService coordinator,
        CancellationToken token)
    {
        if (!evt.Parameters.TryGetValue("id", out var idObj) ||
            idObj is not System.Text.Json.JsonElement elem ||
            elem.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, "Missing or invalid 'id' parameter for export action."),
                token);
            return null;
        }

        var instanceId = elem.GetString();
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, "'id' parameter cannot be empty."),
                token);
            return null;
        }

        try
        {
            var gmi = coordinator.ExportGmi(instanceId);

            return new
            {
                id = instanceId,
                status = "exported",
                archive = new
                {
                    path = gmi.ArchiveUri,
                    name = gmi.ArchiveName,
                    os = gmi.Os,
                    version = gmi.Version,
                    gmiName = gmi.GmiName
                }
            };
        }
        catch (Exception ex)
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, $"GMI export failed: {ex.Message}"),
                token);
            return null;
        }
    }

    private static async Task<object?> HandleImportAsync(
    Event evt,
    IWorkerContext ctx,
    CoordinatorService coordinator,
    CancellationToken token)
    {
        if (!evt.Parameters.TryGetValue("archive", out var archiveObj) ||
            archiveObj is not System.Text.Json.JsonElement elem ||
            elem.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, "Missing or invalid 'archive' parameter for import action."),
                token);
            return null;
        }

        var archiveUri = elem.GetString();
        if (string.IsNullOrWhiteSpace(archiveUri))
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, "'archive' parameter cannot be empty."),
                token);
            return null;
        }

        try
        {
            var gmi = coordinator.ImportGmi(archiveUri);
            return new
            {
                archive = gmi.ArchiveUri,
                status = "imported",
                gmiName = gmi.GmiName
            };
        }
        catch (Exception ex)
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, $"GMI import failed: {ex.Message}"),
                token);
            return null;
        }
    }

    private static Dictionary<string, object> HandleHelp()
    {
        var help = new Dictionary<string, object>
        {
            ["description"] = "The 'gmi' command manages Genie Machine Images (GMI) as base VM artifacts.",
            ["actions"] = new[]
            {
            new Dictionary<string, string> {
                ["action"] = "export",
                ["description"] = "Exports a Genie Machine Image (GMI) by VM GUID. Parameter: id = VM GUID of the GMI."
            },
            new Dictionary<string, string> {
                ["action"] = "import",
                ["description"] = "Imports a Genie Machine Image (GMI) from a GMI .zip archive. Parameter: archive = path to the GMI archive."
            },
            new Dictionary<string, string> {
                ["action"] = "help",
                ["description"] = "Displays this help message."
            }
        },
            ["exampleRequests"] = new[]
            {
            new Dictionary<string, object> {
                ["action"] = "export",
                ["parameters"] = new { action = "export", id = "GMI-VM-GUID" }
            },
            new Dictionary<string, object> {
                ["action"] = "import",
                ["parameters"] = new { action = "import", archive = "var/gmi/GMI-Ubuntu-24.04.zip" }
            }
        }
        };

        return new Dictionary<string, object> { ["help"] = help };
    }

}
