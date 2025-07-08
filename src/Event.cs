using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VmGenie;

public class Event
{
    [JsonPropertyName("id")]
    public string Id { get; init; }

    [JsonPropertyName("command")]
    public string Command { get; init; }

    [JsonPropertyName("parameters")]
    public Dictionary<string, object> Parameters { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; }

    [JsonConstructor]
    public Event(string id, string command, Dictionary<string, object> parameters, DateTime timestamp)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Command = command ?? throw new ArgumentNullException(nameof(command));
        Parameters = parameters ?? new();
        Timestamp = timestamp;
    }

    public Event(string id, string command, IDictionary<string, object> parameters)
        : this(id, command, new Dictionary<string, object>(parameters ?? new Dictionary<string, object>()), DateTime.UtcNow) { }
}
