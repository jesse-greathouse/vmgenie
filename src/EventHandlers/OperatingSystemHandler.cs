namespace VmGenie.EventHandlers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using VmGenie.Template;

/// <summary>
/// Handles the "operating-system" resource command and responds based on 'action' parameter.
/// Supported actions: list, details, help (default).
/// </summary>
public class OperatingSystemHandler(Config config) : IEventHandler
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
                data = HandleList(repository);
                break;

            case "details":
                data = HandleDetails(evt, repository, out var detailsError);
                if (data is null)
                {
                    await SendErrorAndReturnNull(ctx, evt, detailsError!, token);
                    return;
                }
                break;

            case "help":
                data = HandleHelp();
                break;

            default:
                await ctx.SendResponseAsync(
                    EventResponse.Error(evt, $"Unknown action: '{action}'. Valid actions are: list, details, help."),
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
#pragma warning restore IDE0046

    private static object HandleList(OperatingSystemTemplateRepository repository)
    {
        return new
        {
            operatingSystems = repository.GetOperatingSystems()
        };
    }

    private static object? HandleDetails(Event evt, OperatingSystemTemplateRepository repository, out string? error)
    {
        error = null;

        if (!evt.Parameters.TryGetValue("name", out var nameObj) || nameObj is not string osName)
        {
            error = "Missing or invalid 'name' parameter for details action.";
            return null;
        }

        var versions = repository.GetVersionsFor(osName);

        return new
        {
            name = osName,
            versions
        };
    }

    private static Dictionary<string, object> HandleHelp()
    {
        var help = new Dictionary<string, object>
        {
            ["description"] = "The 'operating-system' command allows you to query available operating systems and their versions.",
            ["actions"] = new[]
            {
                new Dictionary<string, string> { ["action"] = "list", ["description"] = "Lists all available operating systems." },
                new Dictionary<string, string> { ["action"] = "details", ["description"] = "Shows available versions for a given OS. Requires parameter: 'name'." },
                new Dictionary<string, string> { ["action"] = "help", ["description"] = "Displays this help message." }
            },
            ["exampleRequests"] = new[]
            {
                new Dictionary<string, object> { ["action"] = "list", ["parameters"] = new { action = "list" } },
                new Dictionary<string, object> { ["action"] = "details", ["parameters"] = new { action = "details", name = "Ubuntu" } }
            }
        };

        return new Dictionary<string, object> { ["help"] = help };
    }

    private static async Task<object?> SendErrorAndReturnNull(IWorkerContext ctx, Event evt, string errorMsg, CancellationToken token)
    {
        await ctx.SendResponseAsync(
            EventResponse.Error(evt, errorMsg),
            token
        );

        return null;
    }
}
