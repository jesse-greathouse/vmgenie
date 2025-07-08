using System;
using System.Text.Json.Serialization;

namespace VmGenie;

public class EventResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; }

    [JsonPropertyName("command")]
    public string Command { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; }

    [JsonPropertyName("data")]
    public object Data { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; }

    [JsonConstructor]
    public EventResponse(string id, string command, string status, object data, DateTime timestamp)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Command = command ?? throw new ArgumentNullException(nameof(command));
        Status = status ?? throw new ArgumentNullException(nameof(status));
        Data = data;
        Timestamp = timestamp;
    }

    public EventResponse(string id, string command, string status, object data)
        : this(id, command, status, data, DateTime.UtcNow) { }

    public static EventResponse Success(Event evt, object data) =>
        new EventResponse(evt.Id, evt.Command, "ok", data);

    public static EventResponse Error(Event evt, string errorMessage) =>
        new EventResponse(evt.Id, evt.Command, "error", errorMessage);
}
