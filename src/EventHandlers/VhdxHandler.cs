using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using VmGenie.HyperV;

namespace VmGenie.EventHandlers;

/// <summary>
/// Handles the "vhdx" command with actions: clone, help.
/// </summary>
public class VhdxHandler(VmHelpers vmHelpers) : IEventHandler
{
    private readonly VmHelpers _vmHelpers = vmHelpers ?? throw new ArgumentNullException(nameof(vmHelpers));

    public async Task HandleAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        string action = GetAction(evt);

        object? data;

        switch (action)
        {
            case "clone":
                data = await HandleCloneAsync(evt, ctx, token);
                break;

            case "is-differencing-disk":
                data = await HandleIsDifferencingDiskAsync(evt, ctx, token);
                break;

            case "help":
            default:
                data = HandleHelp();
                break;
        }

        if (data is null) return;

        var response = EventResponse.Success(evt, data);
        await ctx.SendResponseAsync(response, token);
    }

#pragma warning disable IDE0046
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

    private async Task<object?> HandleIsDifferencingDiskAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        string? guid = null;

        if (evt.Parameters.TryGetValue("guid", out var guidObj) &&
            guidObj is JsonElement guidElem &&
            guidElem.ValueKind == JsonValueKind.String)
        {
            guid = guidElem.GetString()?.Trim();
        }

        if (string.IsNullOrWhiteSpace(guid))
        {
            await ctx.SendResponseAsync(EventResponse.Error(evt, "Missing or invalid 'guid' parameter."), token);
            return null;
        }

        try
        {
            bool isDifferencing = _vmHelpers.IsDifferencingDisk(guid);

            return new
            {
                guid,
                isDifferencing
            };
        }
        catch (Exception ex)
        {
            await ctx.SendResponseAsync(EventResponse.Error(evt, ex.Message), token);
            return null;
        }
    }

    private async Task<object?> HandleCloneAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        string? guid = null;
        string? instanceName = null;

        if (evt.Parameters.TryGetValue("guid", out var guidObj) &&
            guidObj is JsonElement guidElem &&
            guidElem.ValueKind == JsonValueKind.String)
        {
            guid = guidElem.GetString()?.Trim();
        }

        if (evt.Parameters.TryGetValue("instance_name", out var nameObj) &&
            nameObj is JsonElement nameElem &&
            nameElem.ValueKind == JsonValueKind.String)
        {
            instanceName = nameElem.GetString()?.Trim();
        }

        if (string.IsNullOrWhiteSpace(guid))
        {
            await ctx.SendResponseAsync(EventResponse.Error(evt, "Missing or invalid 'guid' parameter."), token);
            return null;
        }

        if (string.IsNullOrWhiteSpace(instanceName))
        {
            await ctx.SendResponseAsync(EventResponse.Error(evt, "Missing or invalid 'instance_name' parameter."), token);
            return null;
        }

        try
        {
            string newVhdxPath = _vmHelpers.CloneBaseVhdx(guid, instanceName);

            return new
            {
                message = "VHDX cloned successfully.",
                path = newVhdxPath
            };
        }
        catch (Exception ex)
        {
            await ctx.SendResponseAsync(EventResponse.Error(evt, ex.Message), token);
            return null;
        }
    }

    private static Dictionary<string, object> HandleHelp()
    {
        var help = new Dictionary<string, object>
        {
            ["description"] = "The 'vhdx' command allows you to manage a base VM's VHDX disk.",
            ["actions"] = new[]
            {
            new Dictionary<string, string>
            {
                ["action"] = "clone",
                ["description"] = "Clones a base VM's VHDX file. Requires: 'guid', 'instance_name'"
            },
            new Dictionary<string, string>
            {
                ["action"] = "is-differencing-disk",
                ["description"] = "Checks if the VM's VHDX is a differencing disk (.avhdx). Requires: 'guid'"
            },
            new Dictionary<string, string>
            {
                ["action"] = "help",
                ["description"] = "Displays this help message."
            }
        },
            ["exampleRequests"] = new[]
            {
            new Dictionary<string, object>
            {
                ["action"] = "clone",
                ["parameters"] = new Dictionary<string, object>
                {
                    ["action"] = "clone",
                    ["guid"] = "YOUR-VM-GUID",
                    ["instance_name"] = "new-instance-name"
                }
            },
            new Dictionary<string, object>
            {
                ["action"] = "is-differencing-disk",
                ["parameters"] = new Dictionary<string, object>
                {
                    ["action"] = "is-differencing-disk",
                    ["guid"] = "YOUR-VM-GUID"
                }
            }
        }
        };

        return new Dictionary<string, object> { ["help"] = help };
    }
}
