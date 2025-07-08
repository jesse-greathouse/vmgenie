using System;
using System.Text.Json.Serialization;

namespace VmGenie;

[method: JsonConstructor]
public class EventResponse(string id, string command, string status, object data, DateTime timestamp)
{
	[JsonPropertyName("id")]
	public string Id { get; init; } = id ?? throw new ArgumentNullException(nameof(id));

	[JsonPropertyName("command")]
	public string Command { get; init; } = command ?? throw new ArgumentNullException(nameof(command));

	[JsonPropertyName("status")]
	public string Status { get; init; } = status ?? throw new ArgumentNullException(nameof(status));

	[JsonPropertyName("data")]
	public object Data { get; init; } = data;

	[JsonPropertyName("timestamp")]
	public DateTime Timestamp { get; init; } = timestamp;

	public EventResponse(string id, string command, string status, object data)
        : this(id, command, status, data, DateTime.UtcNow) { }

    public static EventResponse Success(Event evt, object data) =>
        new EventResponse(evt.Id, evt.Command, "ok", data);

    public static EventResponse Error(Event evt, string errorMessage) =>
        new EventResponse(evt.Id, evt.Command, "error", errorMessage);
}
