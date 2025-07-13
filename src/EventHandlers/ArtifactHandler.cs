using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using VmGenie.Artifacts;

namespace VmGenie.EventHandlers;

/// <summary>
/// Handles the creation of seed artifacts for the cloud-init API.
/// Supported actions: create, help.
/// </summary>
public class ArtifactHandler(Config config) : IEventHandler
{
    private readonly Config _config = config ?? throw new ArgumentNullException(nameof(config));
    public async Task HandleAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        var action = GetAction(evt);

        object? data = action switch
        {
            "create" => await HandleCreateAsync(evt, ctx, token),
            "help" or _ => HandleHelp()
        };

        if (data == null) return;

        var response = EventResponse.Success(evt, data);
        await ctx.SendResponseAsync(response, token);
    }

#pragma warning disable IDE0046 // Convert to conditional expression
    // Doing this as a conditional expression is very hard to read here.
    private static string GetAction(Event evt)
    {
        if (evt.Parameters.TryGetValue("action", out var actionObj) &&
            actionObj is JsonElement elem &&
            elem.ValueKind == JsonValueKind.String)
        {
            return elem.GetString()?.Trim().ToLowerInvariant() ?? "help";
        }

        return "help";
    }
#pragma warning restore IDE0046

    private async Task<object?> HandleCreateAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        if (!evt.Parameters.TryGetValue("instanceName", out var instanceNameObj))
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, "Parameter 'instanceName' is required."),
                token);
            return null;
        }

        var instanceName = instanceNameObj.ToString();
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, "Invalid 'instanceName'."),
                token);
            return null;
        }

        var artifactDir = Path.Combine(_config.CloudDir, instanceName);
        if (!Directory.Exists(artifactDir))
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, $"Artifact directory does not exist: {artifactDir}"),
                token);
            return null;
        }

        var seedDataDir = Path.Combine(artifactDir, "seed-data");
        if (!Directory.Exists(seedDataDir))
        {
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, $"Seed data directory does not exist: {seedDataDir}"),
                token);
            return null;
        }

        var outputIsoPath = Path.Combine(artifactDir, "seed.iso");

        try
        {
            var isoPath = IsoCreator.CreateSeedIso(seedDataDir, outputIsoPath);
            return new { isoPath };
        }
        catch (Exception ex)
        {
            await ctx.SendResponseAsync(EventResponse.Error(evt, ex.Message), token);
            return null;
        }
    }

    private static object HandleHelp()
    {
        return new
        {
            description = "The 'artifact' command generates ISO artifacts from seed data directories.",
            actions = new[]
            {
                new { action = "create", description = "Generates ISO. Requires: seedDataDir, outputIsoPath" },
                new { action = "help", description = "Displays this help message" }
            }
        };
    }
}
