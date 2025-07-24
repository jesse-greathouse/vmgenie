using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using VmGenie.Artifacts;
using VmGenie.HyperV;

namespace VmGenie.EventHandlers;

/// <summary>
/// Handles the "vm" command and responds based on 'action' parameter.
/// Supported actions: list, details, help (default).
/// </summary>
public class VmHandler(
    VmRepository repository,
    VmNetAddressRepository netAddressRepository,
    VmProvisioningService provisioner,
    VmLifecycleService lifecycle,
    CoordinatorService coordinator
) : IEventHandler
{
    private readonly VmRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly VmNetAddressRepository _netAddressRepository = netAddressRepository ?? throw new ArgumentNullException(nameof(netAddressRepository));
    private readonly VmProvisioningService _provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
    private readonly VmLifecycleService _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    private readonly CoordinatorService _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
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

            case "start":
                data = HandleLifecycleAction(evt, _lifecycle.Start);
                break;

            case "stop":
                data = HandleLifecycleAction(evt, _lifecycle.Stop);
                break;

            case "pause":
                data = HandleLifecycleAction(evt, _lifecycle.Pause);
                break;

            case "resume":
                data = HandleLifecycleAction(evt, _lifecycle.Resume);
                break;

            case "shutdown":
                data = HandleLifecycleAction(evt, _lifecycle.Shutdown);
                break;

            case "delete":
                data = HandleDelete(evt, _coordinator);
                break;

            case "export":
                data = await HandleExportAsync(evt, ctx, _coordinator, token);
                if (data is null) return;
                break;

            case "import":
                data = await HandleImportAsync(evt, ctx, _coordinator, token);
                if (data is null) return;
                break;

            case "state-check":
                data = HandleStateCheck(evt, _lifecycle);
                break;

            case "net-address":
                data = HandleNetAddress(evt);
                break;

            case "swap-iso":
                data = HandleSwapIso(evt, _provisioner);
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

    private static object? HandleLifecycleAction(Event evt, Action<string> lifecycleAction)
    {
        if (!evt.Parameters.TryGetValue("id", out var idObj) ||
            idObj is not System.Text.Json.JsonElement elem ||
            elem.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            throw new ArgumentException("Missing or invalid 'id' parameter for lifecycle action.");
        }

        var vmId = elem.GetString();
        if (string.IsNullOrWhiteSpace(vmId))
        {
            throw new ArgumentException("'id' parameter cannot be empty.");
        }

        lifecycleAction(vmId);

        return new { id = vmId, status = "ok" };
    }

    private static object? HandleStateCheck(Event evt, VmLifecycleService lifecycle)
    {
        if (!evt.Parameters.TryGetValue("id", out var idObj) ||
            idObj is not System.Text.Json.JsonElement idElem ||
            idElem.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            throw new ArgumentException("Missing or invalid 'id' parameter for state-check action.");
        }

        var vmId = idElem.GetString();
        if (string.IsNullOrWhiteSpace(vmId))
        {
            throw new ArgumentException("'id' parameter cannot be empty.");
        }

        if (!evt.Parameters.TryGetValue("state", out var stateObj) ||
            stateObj is not System.Text.Json.JsonElement stateElem ||
            !stateElem.TryGetInt32(out var stateInt))
        {
            throw new ArgumentException("Missing or invalid 'state' parameter for state-check action.");
        }

        if (stateInt == VmLifecycleService.NetworkReadyState)
        {
            // Special handling for logical NetworkReady state
            bool isReady = VmLifecycleService.IsNetworkReady(vmId);
            return new
            {
                id = vmId,
                desiredState = VmLifecycleService.NetworkReadyState,
                currentState = isReady ? VmLifecycleService.NetworkReadyState : (int)VmLifecycleService.GetCurrentState(vmId),
                matches = isReady
            };
        }

        // Validate the real VmState
        if (!Enum.IsDefined(typeof(VmState), (ushort)stateInt))
        {
            throw new ArgumentException($"Invalid VmState value: {stateInt}");
        }

        var desiredState = (VmState)(ushort)stateInt;
        var currentState = VmLifecycleService.GetCurrentState(vmId);
        var matches = currentState == desiredState;

        return new
        {
            id = vmId,
            desiredState,
            currentState,
            matches
        };
    }

    private static object HandleList(Event evt, VmRepository repository)
    {
        // Default to All
        var provisionedFilter = VmRepository.ProvisionedFilter.All;
        VmState? stateFilter = null;

        // parse provisioned param
        if (evt.Parameters.TryGetValue("provisioned", out var provObj) &&
            provObj is System.Text.Json.JsonElement provElem &&
            provElem.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var provValue = provElem.GetString()?.Trim().ToLowerInvariant();

            provisionedFilter = provValue switch
            {
                "exclude" => VmRepository.ProvisionedFilter.ExcludeProvisioned,
                "only" => VmRepository.ProvisionedFilter.OnlyProvisioned,
                _ => VmRepository.ProvisionedFilter.All
            };
        }

        // parse state param
        if (evt.Parameters.TryGetValue("state", out var stateObj) &&
            stateObj is System.Text.Json.JsonElement stateElem &&
            stateElem.TryGetInt32(out var stateInt))
        {
            if (Enum.IsDefined(typeof(VmState), (ushort)stateInt))
            {
                stateFilter = (VmState)(ushort)stateInt;
            }
        }

        bool includeNetAddress = false;
        if (evt.Parameters.TryGetValue("includeNetAddress", out var netAddrObj) &&
            netAddrObj is System.Text.Json.JsonElement netAddrElem)
        {
            if (netAddrElem.ValueKind == System.Text.Json.JsonValueKind.True)
            {
                includeNetAddress = true;
            }
            else if (netAddrElem.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                // Accept "true"/"false" strings
                if (bool.TryParse(netAddrElem.GetString(), out var parsed))
                    includeNetAddress = parsed;
            }
        }

        var vms = repository.GetAll(provisionedFilter, stateFilter, includeNetAddress);

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

        // Parse includeNetAddress param
        bool includeNetAddress = false;
        if (evt.Parameters.TryGetValue("includeNetAddress", out var netAddrObj) &&
            netAddrObj is System.Text.Json.JsonElement netAddrElem)
        {
            if (netAddrElem.ValueKind == System.Text.Json.JsonValueKind.True)
            {
                includeNetAddress = true;
            }
            else if (netAddrElem.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                // Accept "true"/"false" strings
                if (bool.TryParse(netAddrElem.GetString(), out var parsed))
                    includeNetAddress = parsed;
            }
        }

        // Pass through to repository
        var vm = repository.GetById(vmId, includeNetAddress);
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

    private object? HandleNetAddress(Event evt)
    {
        if (!evt.Parameters.TryGetValue("id", out var idObj) ||
            idObj is not System.Text.Json.JsonElement elem ||
            elem.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            throw new ArgumentException("Missing or invalid 'id' parameter for net-address action.");
        }

        var vmId = elem.GetString();
        if (string.IsNullOrWhiteSpace(vmId))
        {
            throw new ArgumentException("'id' parameter cannot be empty.");
        }

        var result = _netAddressRepository.GetNetAddressesForVmById(vmId);

        return new
        {
            id = vmId,
            addresses = result
        };
    }

    private static object? HandleDelete(Event evt, CoordinatorService coordinator)
    {
        if (!evt.Parameters.TryGetValue("id", out var idObj) ||
            idObj is not System.Text.Json.JsonElement elem ||
            elem.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            throw new ArgumentException("Missing or invalid 'id' parameter for delete action.");
        }

        var vmId = elem.GetString();
        if (string.IsNullOrWhiteSpace(vmId))
        {
            throw new ArgumentException("'id' parameter cannot be empty.");
        }

        bool force = false;
        if (evt.Parameters.TryGetValue("force", out var forceObj) &&
            forceObj is System.Text.Json.JsonElement forceElem &&
            forceElem.ValueKind == System.Text.Json.JsonValueKind.True)
        {
            force = true;
        }

        coordinator.DeleteInstance(vmId, force);

        return new { id = vmId, status = "deleted" };
    }

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
            // Do the export orchestration (may throw).
            var export = coordinator.ExportInstance(instanceId);

            return new
            {
                id = instanceId,
                status = "exported",
                archive = new
                {
                    path = export.ArchiveUri,
                    name = export.ArchiveName,
                    createdDate = export.CreatedDate,
                    updatedDate = export.UpdatedDate,
                    instanceName = export.InstanceName,
                    instanceId = export.InstanceId
                }
            };
        }
        catch (Exception ex)
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, $"Export failed: {ex.Message}"),
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
            archiveObj is not System.Text.Json.JsonElement archiveElem ||
            archiveElem.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, "Missing or invalid 'archive' parameter for import action."),
                token);
            return null;
        }

        var archiveUri = archiveElem.GetString();
        if (string.IsNullOrWhiteSpace(archiveUri))
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, "'archive' parameter cannot be empty."),
                token);
            return null;
        }

        // Optional: mode (defaults to Restore)
        ImportMode mode = ImportMode.Restore;
        if (evt.Parameters.TryGetValue("mode", out var modeObj) &&
            modeObj is System.Text.Json.JsonElement modeElem &&
            modeElem.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var modeStr = modeElem.GetString();
            if (!string.IsNullOrWhiteSpace(modeStr) && Enum.TryParse(modeStr, true, out ImportMode parsedMode))
            {
                mode = parsedMode;
            }
        }

        // Optional: newInstanceName for copy mode
        string? newInstanceName = null;
        if (evt.Parameters.TryGetValue("newInstanceName", out var newInstanceNameObj) &&
            newInstanceNameObj is System.Text.Json.JsonElement newInstanceNameElem &&
            newInstanceNameElem.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            newInstanceName = newInstanceNameElem.GetString();
        }

        try
        {
            // Call coordinator for orchestration
            var export = coordinator.ImportInstance(archiveUri, mode, newInstanceName);

            return new
            {
                status = "imported",
                archive = new
                {
                    path = export.ArchiveUri,
                    name = export.ArchiveName,
                    createdDate = export.CreatedDate,
                    updatedDate = export.UpdatedDate,
                    instanceName = export.InstanceName,
                    instanceId = export.InstanceId
                }
            };
        }
        catch (Exception ex)
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, $"Import failed: {ex.Message}"),
                token);
            return null;
        }
    }

    private static object? HandleSwapIso(Event evt, VmProvisioningService provisioner)
    {
        // Expect "id" (VM GUID or instanceId) and "isoPath"
        if (!evt.Parameters.TryGetValue("id", out var idObj) ||
            idObj is not System.Text.Json.JsonElement idElem ||
            idElem.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            throw new ArgumentException("Missing or invalid 'id' parameter for swap-iso action.");
        }

        var vmId = idElem.GetString();
        if (string.IsNullOrWhiteSpace(vmId))
        {
            throw new ArgumentException("'id' parameter cannot be empty.");
        }

        if (!evt.Parameters.TryGetValue("isoPath", out var isoPathObj) ||
            isoPathObj is not System.Text.Json.JsonElement isoPathElem ||
            isoPathElem.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            throw new ArgumentException("Missing or invalid 'isoPath' parameter for swap-iso action.");
        }

        var isoPath = isoPathElem.GetString();
        if (string.IsNullOrWhiteSpace(isoPath))
        {
            throw new ArgumentException("'isoPath' parameter cannot be empty.");
        }

        provisioner.SwapIso(vmId, isoPath);

        return new { id = vmId, isoPath, status = "swapped" };
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
                    ["description"] = "Lists Hyper-V virtual machines. Optional parameters: provisioned = all|exclude|only, state = numeric VmState enum value, includeNetAddress = true|false"
                },
                new Dictionary<string, string> {
                    ["action"] = "details",
                    ["description"] = "Shows details for a specific VM by its 'id'. Optional: includeNetAddress = true|false"
                },
                new Dictionary<string, string> {
                    ["action"] = "provision",
                    ["description"] = "Provisions a new VM from a base VM GUID, instance name, and switch GUID."
                },
                new Dictionary<string, string> { ["action"] = "start", ["description"] = "Starts a VM by id." },
                new Dictionary<string, string> { ["action"] = "stop", ["description"] = "Stops a VM by id." },
                new Dictionary<string, string> { ["action"] = "pause", ["description"] = "Pauses a VM by id." },
                new Dictionary<string, string> { ["action"] = "resume", ["description"] = "Resumes a VM by id." },
                new Dictionary<string, string> { ["action"] = "shutdown", ["description"] = "Gracefully shuts down a VM by id." },
                new Dictionary<string, string> {
                    ["action"] = "state-check",
                    ["description"] = "Checks if a VM is currently in a specific state by id and state value."
                },
                new Dictionary<string, string> {
                    ["action"] = "net-address",
                    ["description"] = "Returns categorized network addresses for the specified VM id."
                },
                new Dictionary<string, string> { ["action"] = "delete", ["description"] = "Deletes a VM by id and removes all associated data." },
                new Dictionary<string, string> { ["action"] = "export", ["description"] = "Exports a VM and all artifacts as a .zip archive. Parameter: id = VM GUID." },
                new Dictionary<string, string> { ["action"] = "import", ["description"] = "Imports a VM from an exported archive. Parameters: archive (required), mode (copy|restore, optional), newInstanceName (required for copy)" },
                new Dictionary<string, string> {
                    ["action"] = "swap-iso",
                    ["description"] = "Swaps the attached ISO for a running/stopped VM. Parameters: id = VM GUID, isoPath = path to new ISO."
                },
                new Dictionary<string, string> { ["action"] = "help", ["description"] = "Displays this help message." }
            },
            ["exampleRequests"] = new[]
            {
                new Dictionary<string, object> {
                    ["action"] = "list",
                    ["parameters"] = new { action = "list", includeNetAddress = true }
                },
                new Dictionary<string, object> {
                    ["action"] = "details",
                    ["parameters"] = new { action = "details", id = "SOME-VM-ID", includeNetAddress = true }
                },
                new Dictionary<string, object> {
                    ["action"] = "provision",
                    ["parameters"] = new { action = "provision", baseVmGuid = "BASE-VM-GUID", instanceName = "my-new-vm", vmSwitchGuid = "SWITCH-GUID", mergeDifferencingDisk = false }
                },
                new Dictionary<string, object> {
                    ["action"] = "state-check",
                    ["parameters"] = new { action = "state-check", id = "SOME-VM-ID", state = 2 }
                },
                new Dictionary<string, object> {
                    ["action"] = "net-address",
                    ["parameters"] = new { action = "net-address", id = "SOME-VM-ID" }
                },
                new Dictionary<string, object> {
                    ["action"] = "delete",
                    ["parameters"] = new { action = "delete", id = "SOME-VM-ID" }
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
