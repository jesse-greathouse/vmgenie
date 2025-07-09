using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VmGenie;

public class EventHandlerEngine
{
    private readonly ConcurrentDictionary<string, List<IEventHandler>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _frozen = false;

    public void Register(string command, IEventHandler handler)
    {
        if (_frozen)
            throw new InvalidOperationException("Cannot register handlers after engine is frozen.");

        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command cannot be null or empty.", nameof(command));

        ArgumentNullException.ThrowIfNull(handler);

        _handlers.AddOrUpdate(
            command,
            _ => [handler],
            (_, list) =>
            {
                list.Add(handler);
                return list;
            });
    }

    /// Call this when starting the server to make the map immutable.
    public void Freeze()
    {
        _frozen = true;
    }

    /// Returns the list of registered command keys.
    public List<string> GetRegisteredCommands()
    {
        return [.. _handlers.Keys];
    }

    public async Task DispatchAsync(Event evt, IWorkerContext ctx, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(ctx);

        if (!_handlers.TryGetValue(evt.Command, out var handlers) || handlers.Count == 0)
        {
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
            await ctx.SendResponseAsync(
                EventResponse.Error(evt, ex.Message),
                token
            );
        }
    }
}
