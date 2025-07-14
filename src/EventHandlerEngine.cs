using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace VmGenie;

public class EventHandlerEngine(ILogger<EventHandlerEngine> logger)
{
    private readonly ILogger<EventHandlerEngine> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly ConcurrentDictionary<string, List<IEventHandler>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _frozen = false;

    public void Register(string command, IEventHandler handler)
    {
        if (_frozen)
            throw new InvalidOperationException("Cannot register handlers after engine is frozen.");

        ArgumentNullException.ThrowIfNull(handler);
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command cannot be null or empty.", nameof(command));

        _handlers.AddOrUpdate(
            command,
            _ => [handler],
            (_, list) =>
            {
                list.Add(handler);
                return list;
            });
    }

    public void Freeze() => _frozen = true;

    public List<string> GetRegisteredCommands() => [.. _handlers.Keys];

    public async Task DispatchAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(ctx);

        if (!_handlers.TryGetValue(evt.Command, out var handlers) || handlers.Count == 0)
        {
            _logger.LogWarning("Unknown command: {Command}", evt.Command);
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, $"Unknown command: {evt.Command}"),
                token
            );
            return;
        }

        var tasks = handlers.Select(handler => SafeExecute(handler, evt, ctx, token)).ToArray();
        await Task.WhenAll(tasks);
    }

    private async Task SafeExecute(IEventHandler handler, Event evt, IWorkerContext ctx, CancellationToken token)
    {
        try
        {
            await handler.HandleAsync(evt, ctx, token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handler for command '{Command}' threw: {Message}", evt.Command, ex.Message);
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, ex.Message),
                token
            );
        }
    }
}
