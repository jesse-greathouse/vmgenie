using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VmGenie;

[method: JsonConstructor]
public class Event(string id, string command, Dictionary<string, object> parameters, DateTime timestamp)
{
	[JsonPropertyName("id")]
	public string Id { get; init; } = id ?? throw new ArgumentNullException(nameof(id));

	[JsonPropertyName("command")]
	public string Command { get; init; } = command ?? throw new ArgumentNullException(nameof(command));

	[JsonPropertyName("parameters")]
	public Dictionary<string, object> Parameters { get; init; } = parameters ?? new();

	[JsonPropertyName("timestamp")]
	public DateTime Timestamp { get; init; } = timestamp;

	public Event(string id, string command, IDictionary<string, object> parameters)
        : this(id, command, new Dictionary<string, object>(parameters ?? new Dictionary<string, object>()), DateTime.UtcNow) { }
}
