using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VmGenie;

public class EventHandlerEngine
{
    private readonly ConcurrentDictionary<string, List<EventHandler>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _frozen = false;

    public void Register(string command, EventHandler handler)
    {
        if (_frozen)
            throw new InvalidOperationException("Cannot register handlers after engine is frozen.");

        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command cannot be null or empty.", nameof(command));

        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        _handlers.AddOrUpdate(
            command,
            _ => new List<EventHandler> { handler },
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

    public async Task<List<EventResponse>> DispatchAsync(Event evt, CancellationToken token)
    {
        if (evt == null)
            throw new ArgumentNullException(nameof(evt));

        if (!_handlers.TryGetValue(evt.Command, out var handlers) || handlers.Count == 0)
        {
            return new List<EventResponse>
            {
                EventResponse.Error(evt, $"Unknown command: {evt.Command}")
            };
        }

        var tasks = handlers.Select(handler => SafeExecute(handler, evt, token)).ToArray();
        var results = await Task.WhenAll(tasks);

        return results.ToList();
    }

    private async Task<EventResponse> SafeExecute(EventHandler handler, Event evt, CancellationToken token)
    {
        try
        {
            return await handler(evt, token);
        }
        catch (Exception ex)
        {
            return EventResponse.Error(evt, ex.Message);
        }
    }
}
