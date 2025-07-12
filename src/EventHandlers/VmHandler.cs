using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using VmGenie.HyperV;

namespace VmGenie.EventHandlers;

/// <summary>
/// Handles the "vm" command and responds based on 'action' parameter.
/// Supported actions: list, details, help (default).
/// </summary>
public class VmHandler : IEventHandler
{
    public async Task HandleAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        var repository = new VmRepository();

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

    private static object HandleList(VmRepository repository)
    {
        var vms = repository.GetAll();
        return new
        {
            vms
        };
    }

    private static object? HandleDetails(Event evt, VmRepository repository, out string? error)
    {
        error = null;

        if (!evt.Parameters.TryGetValue("id", out var idObj) || idObj is not System.Text.Json.JsonElement elem || elem.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            error = "Missing or invalid 'id' parameter for details action.";
            return null;
        }

        var vmId = elem.GetString();
        if (string.IsNullOrWhiteSpace(vmId))
        {
            error = "'id' parameter cannot be empty.";
            return null;
        }

        var vm = repository.GetById(vmId);
        if (vm is null)
        {
            error = $"No VM found with id: {vmId}";
            return null;
        }

        return new
        {
            vm
        };
    }

    private static Dictionary<string, object> HandleHelp()
    {
        var help = new Dictionary<string, object>
        {
            ["description"] = "The 'vm' command allows you to query Hyper-V virtual machines.",
            ["actions"] = new[]
            {
                new Dictionary<string, string> { ["action"] = "list", ["description"] = "Lists all Hyper-V virtual machines." },
                new Dictionary<string, string> { ["action"] = "details", ["description"] = "Shows details for a specific VM by its 'id'." },
                new Dictionary<string, string> { ["action"] = "help", ["description"] = "Displays this help message." }
            },
            ["exampleRequests"] = new[]
            {
                new Dictionary<string, object> { ["action"] = "list", ["parameters"] = new { action = "list" } },
                new Dictionary<string, object> { ["action"] = "details", ["parameters"] = new { action = "details", id = "SOME-VM-ID" } }
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
