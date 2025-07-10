using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VmGenie;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly string _applicationDir;
    private readonly Config _cfg;
    private readonly EventHandlerEngine _engine;
    private readonly List<string> _commands;

    public Worker(ILogger<Worker> logger, EventHandlerEngine engine, Config cfg)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));

        _applicationDir = cfg.ApplicationDir;
        _logger.LogInformation("APPLICATION_DIR resolved to: {dir}", _applicationDir);

        _logger.LogInformation("Loaded configuration: {@Config}", _cfg);

        _commands = [.. _engine.GetRegisteredCommands()];
    }

    private class WorkerContext(NamedPipeServerStream pipe, ILogger logger) : IWorkerContext
    {
        private readonly NamedPipeServerStream _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public async Task SendResponseAsync(EventResponse response, CancellationToken token)
        {
            string responseJson = JsonSerializer.Serialize(response);
            byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson + "\n");

            await _pipe.WriteAsync(responseBytes, 0, responseBytes.Length, token);

            _logger.LogInformation("Response sent: {response}", responseJson);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VmGenie Service started at: {time}", DateTimeOffset.Now);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(
                    "vmgenie",
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                _logger.LogInformation("Waiting for client connection on named pipe...");

                await pipeServer.WaitForConnectionAsync(stoppingToken);

                _logger.LogInformation("Client connected.");

                await HandleClient(pipeServer, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in named pipe listener.");
            }
        }

        _logger.LogInformation("VmGenie Service stopped.");
    }

    private async Task HandleClient(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        var buffer = new byte[4096];
        int bytesRead = await pipe.ReadAsync(buffer, 0, buffer.Length, stoppingToken);

        string requestJson = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
        _logger.LogInformation("Received: {request}", requestJson);

        var ctx = new WorkerContext(pipe, _logger);

        Event? evt;
        try
        {
            evt = JsonSerializer.Deserialize<Event>(requestJson);
        }
        catch (Exception jsonEx)
        {
            _logger.LogError(jsonEx, "Failed to deserialize request JSON: {request}", requestJson);
            var err = HandleInvalidJson(requestJson);
            await ctx.SendResponseAsync(err, stoppingToken);
            return;
        }

        if (evt == null)
        {
            await ctx.SendResponseAsync(HandleInvalidJson(requestJson), stoppingToken);
            return;
        }
        if (!IsValidEvent(evt))
        {
            await ctx.SendResponseAsync(HandleInvalidRequest(evt), stoppingToken);
            return;
        }
        if (!IsValidCommand(evt))
        {
            await ctx.SendResponseAsync(HandleInvalidCommand(evt), stoppingToken);
            return;
        }

        await _engine.DispatchAsync(evt, ctx, stoppingToken);
    }

    private EventResponse HandleInvalidJson(string badJson)
    {
        string id = Guid.NewGuid().ToString(); // fallback

        try
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                badJson,
                @"""id""\s*:\s*""([^""]+)""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            if (match.Success && match.Groups.Count > 1)
            {
                id = match.Groups[1].Value;
                _logger.LogInformation("Extracted id '{id}' from invalid JSON.", id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Regex extraction of id failed.");
        }

        return new EventResponse(
            id: id,
            command: "unknown",
            status: EventStatus.ERROR,
            data: new
            {
                details = $"Invalid JSON string received: {badJson}"
            }
        );
    }

    /// <summary>
    /// Verifies that the Event has non-empty Id, Command, and Parameters (even if empty dictionary).
    /// </summary>
    private bool IsValidEvent(Event evt)
    {
        if (evt == null) return false;

        if (string.IsNullOrWhiteSpace(evt.Id))
        {
            _logger.LogWarning("Event validation failed: Id is missing or empty.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(evt.Command))
        {
            _logger.LogWarning("Event validation failed: Command is missing or empty.");
            return false;
        }

        if (evt.Parameters == null)
        {
            _logger.LogWarning("Event validation failed: Parameters is null.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns true if the event's command is in the list of known commands.
    /// </summary>
    private bool IsValidCommand(Event evt)
    {
        if (evt == null || string.IsNullOrWhiteSpace(evt.Command))
            return false;

        bool valid = _commands.Any(cmd => string.Equals(cmd, evt.Command, StringComparison.OrdinalIgnoreCase));

        if (!valid)
        {
            _logger.LogWarning("Invalid command received: {command}", evt.Command);
        }

        return valid;
    }

    /// <summary>
    /// Generates a generic EventResponse for an invalid request.
    /// </summary>
    private EventResponse HandleInvalidRequest(Event evt)
    {
        string id = evt?.Id ?? Guid.NewGuid().ToString();

        return new EventResponse(
            id: id,
            command: evt?.Command ?? "unknown",
            status: EventStatus.ERROR,
            data: new
            {
                details = "Request did not conform to expected protocol. Ensure 'id', 'command', and 'parameters' are present."
            }
        );
    }

    /// <summary>
    /// Returns an error EventResponse explaining the command is invalid.
    /// </summary>
    private EventResponse HandleInvalidCommand(Event evt)
    {
        string id = evt?.Id ?? Guid.NewGuid().ToString();
        string cmd = evt?.Command ?? "unknown";

        return new EventResponse(
            id: id,
            command: cmd,
            status: EventStatus.ERROR,
            data: new
            {
                details = $"Command '{cmd}' is not a registered command."
            }
        );
    }
}
