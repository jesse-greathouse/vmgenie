using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using VmGenie.HyperV;

namespace VmGenie.EventHandlers;

/// <summary>
/// Handles the "vmswitch" command and responds based on 'action' parameter.
/// Supported actions: list, help (default).
/// </summary>
public class VmSwitchHandler : IEventHandler
{
    public async Task HandleAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        var repository = new VmSwitchRepository();

        string action = GetAction(evt);

        object? data;

        switch (action)
        {
            case "list":
                data = HandleList(repository);
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

    private static object HandleList(VmSwitchRepository repository)
    {
        var switches = repository.GetAll();
        return new
        {
            switches
        };
    }

    private static Dictionary<string, object> HandleHelp()
    {
        var help = new Dictionary<string, object>
        {
            ["description"] = "The 'vmswitch' command allows you to query Hyper-V virtual switches.",
            ["actions"] = new[]
            {
                new Dictionary<string, string> { ["action"] = "list", ["description"] = "Lists all Hyper-V virtual switches." },
                new Dictionary<string, string> { ["action"] = "help", ["description"] = "Displays this help message." }
            },
            ["exampleRequests"] = new[]
            {
                new Dictionary<string, object> { ["action"] = "list", ["parameters"] = new { action = "list" } },
                new Dictionary<string, object> { ["action"] = "help", ["parameters"] = new { action = "help" } }
            }
        };

        return new Dictionary<string, object> { ["help"] = help };
    }
}
