namespace VmGenie;

/// <summary>
/// Defines standard status strings for EventResponse.
/// </summary>
public static class EventStatus
{
    /// <summary>
    /// Indicates a successful response.
    /// </summary>
    public const string OK = "ok";

    /// <summary>
    /// Indicates an error occurred.
    /// </summary>
    public const string ERROR = "error";

    /// <summary>
    /// Indicates a warning or partial success (optional).
    /// </summary>
    public const string WARNING = "warning";

    /// <summary>
    /// Indicates the command was not recognized.
    /// </summary>
    public const string UNKNOWN = "unknown";
}
