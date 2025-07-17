using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using VmGenie.HyperV;

namespace VmGenie.EventHandlers;

/// <summary>
/// Handles the "vm" command and responds based on 'action' parameter.
/// Supported actions: list, details, help (default).
/// </summary>
public class VmHandler(VmRepository repository, VmProvisioningService provisioner) : IEventHandler
{
    private readonly VmRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly VmProvisioningService _provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
    public async Task HandleAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        string action = GetAction(evt);

        object? data;

        switch (action)
        {
            case "list":
                data = HandleList(evt, _repository);
                break;

            case "details":
                data = HandleDetails(evt, _repository, out var detailsError);
                if (data is null)
                {
                    await SendErrorAndReturnNull(ctx, evt, detailsError!, token);
                    return;
                }
                break;

            case "provision":
                data = await HandleProvisionAsync(evt, ctx, token);
                if (data is null) return;
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

    private static object HandleList(Event evt, VmRepository repository)
    {
        // Default to All
        var filter = VmRepository.ProvisionedFilter.All;

        if (evt.Parameters.TryGetValue("provisioned", out var provObj) &&
            provObj is System.Text.Json.JsonElement elem &&
            elem.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var provValue = elem.GetString()?.Trim().ToLowerInvariant();

            filter = provValue switch
            {
                "exclude" => VmRepository.ProvisionedFilter.ExcludeProvisioned,
                "only" => VmRepository.ProvisionedFilter.OnlyProvisioned,
                _ => VmRepository.ProvisionedFilter.All
            };
        }

        var vms = repository.GetAll(filter);

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

    private async Task<object?> HandleProvisionAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        if (!evt.Parameters.TryGetValue("baseVmGuid", out var baseVmGuidObj) ||
            !evt.Parameters.TryGetValue("instanceName", out var instanceNameObj) ||
            !evt.Parameters.TryGetValue("vmSwitchGuid", out var vmSwitchGuidObj))
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, "Missing required parameters: baseVmGuid, instanceName, vmSwitchGuid."),
                token);
            return null;
        }

        var baseVmGuid = GetStringParam(baseVmGuidObj);
        var instanceName = GetStringParam(instanceNameObj);
        var vmSwitchGuid = GetStringParam(vmSwitchGuidObj);

        if (string.IsNullOrWhiteSpace(baseVmGuid) ||
            string.IsNullOrWhiteSpace(instanceName) ||
            string.IsNullOrWhiteSpace(vmSwitchGuid))
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, "Parameters cannot be empty or whitespace."),
                token);
            return null;
        }

        bool mergeDifferencingDisk = false;
        if (evt.Parameters.TryGetValue("mergeDifferencingDisk", out var mergeObj))
        {
            if (mergeObj is System.Text.Json.JsonElement mergeElem &&
                mergeElem.ValueKind == System.Text.Json.JsonValueKind.True)
            {
                mergeDifferencingDisk = true;
            }
        }

        int generation = 2; // default
        if (evt.Parameters.TryGetValue("generation", out var genObj))
        {
            if (genObj is System.Text.Json.JsonElement genElem &&
                genElem.TryGetInt32(out var parsedGen) &&
                (parsedGen == 1 || parsedGen == 2))
            {
                generation = parsedGen;
            }
        }

        Vm vm;
        try
        {
            vm = _provisioner.ProvisionVm(baseVmGuid, instanceName, vmSwitchGuid, mergeDifferencingDisk, generation);
        }
        catch (Exception ex)
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, $"Provisioning failed: {ex.Message}"),
                token);
            return null;
        }

        return new { vm };
    }

    private static string? GetStringParam(object? obj)
    {
        return obj is System.Text.Json.JsonElement elem && elem.ValueKind == System.Text.Json.JsonValueKind.String ? elem.GetString() : null;
    }

    private static Dictionary<string, object> HandleHelp()
    {
        var help = new Dictionary<string, object>
        {
            ["description"] = "The 'vm' command allows you to query Hyper-V virtual machines.",
            ["actions"] = new[]
            {
                new Dictionary<string, string> {
                    ["action"] = "list",
                    ["description"] = "Lists Hyper-V virtual machines. Optional parameter: provisioned = all|exclude|only"
                },
                new Dictionary<string, string> { ["action"] = "details", ["description"] = "Shows details for a specific VM by its 'id'." },
                new Dictionary<string, string> { ["action"] = "provision", ["description"] = "Provisions a new VM from a base VM GUID, instance name, and switch GUID." },
                new Dictionary<string, string> { ["action"] = "help", ["description"] = "Displays this help message." }
            },
            ["exampleRequests"] = new[]
            {
                new Dictionary<string, object> {
                    ["action"] = "list",
                    ["parameters"] = new { action = "list", provisioned = "only" }
                },
                new Dictionary<string, object> { ["action"] = "details", ["parameters"] = new { action = "details", id = "SOME-VM-ID" } },
                new Dictionary<string, object> {
                    ["action"] = "provision",
                    ["parameters"] = new { action = "provision", baseVmGuid = "BASE-VM-GUID", instanceName = "my-new-vm", vmSwitchGuid = "SWITCH-GUID", mergeDifferencingDisk = false }
                }
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
