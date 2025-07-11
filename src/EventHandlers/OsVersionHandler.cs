using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using VmGenie.Template;

namespace VmGenie.EventHandlers;

/// <summary>
/// Handles the "os-version" resource command and responds based on 'action' parameter.
/// Supported actions: list, help (default).
/// Requires parameter: 'os'.
/// </summary>
public class OsVersionHandler(Config config) : IEventHandler
{
    private readonly Config _config = config ?? throw new ArgumentNullException(nameof(config));

    public async Task HandleAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        var repository = new OperatingSystemTemplateRepository(_config);

        string action = GetAction(evt);

        object? data;

        switch (action)
        {
            case "list":
                data = await HandleListAsync(evt, repository, ctx, token);
                break;

            case "help":
                data = HandleHelp();
                break;

            default:
                await ctx.SendResponseAsync(
                    EventResponse.Error(evt, $"Unknown action: '{action}'. Valid actions are: list, help."),
                    token);
                return;
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
    // Doing this as a conditional expression is very hard to read here.
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

    private static string GetOs(Event evt)
    {
        if (evt.Parameters.TryGetValue("os", out object? osObj) &&
            osObj is System.Text.Json.JsonElement elem &&
            elem.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var os = elem.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(os))
                return os;
        }

        throw new ArgumentException("Missing or invalid required parameter: 'os'.");
    }
#pragma warning restore IDE0046

    private static async Task<object?> HandleListAsync(Event evt, OperatingSystemTemplateRepository repo, IWorkerContext ctx, CancellationToken token)
    {
        string osName;
        try
        {
            osName = GetOs(evt);
        }
        catch (ArgumentException ex)
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, ex.Message),
                token
            );
            return null;
        }

        var versions = repo.GetVersionsFor(osName);

        return new Dictionary<string, object>
            {
                { "osVersions", versions }
            };
    }

    private static Dictionary<string, object> HandleHelp()
    {
        var help = new Dictionary<string, object>
        {
            ["description"] = "The 'os-version' command allows you to query available versions for a given operating system.",
            ["actions"] = new[]
            {
                new Dictionary<string, string> { ["action"] = "list", ["description"] = "Lists all available versions of the given operating system. Requires parameter: 'os'." },
                new Dictionary<string, string> { ["action"] = "help", ["description"] = "Displays this help message." }
            },
            ["exampleRequests"] = new[]
            {
                new Dictionary<string, object> { ["action"] = "list", ["parameters"] = new { action = "list", os = "Ubuntu" } },
                new Dictionary<string, object> { ["action"] = "help", ["parameters"] = new { action = "help" } }
            }
        };

        return new Dictionary<string, object> { ["help"] = help };
    }
}
