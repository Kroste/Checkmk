using System.Text.Json.Serialization;

namespace Checkmk.Core.Models;

/// <summary>
/// Live-Status eines Service (Livestatus) aus
/// /domain-types/service/collections/all mit columns=... .
/// </summary>
public sealed record ServiceStatus
{
    [JsonPropertyName("host_name")]
    public string HostName { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>0 = OK, 1 = WARN, 2 = CRIT, 3 = UNKNOWN.</summary>
    [JsonPropertyName("state")]
    public int State { get; init; }

    [JsonPropertyName("plugin_output")]
    public string? PluginOutput { get; init; }

    [JsonPropertyName("acknowledged")]
    public int Acknowledged { get; init; }

    [JsonPropertyName("scheduled_downtime_depth")]
    public int ScheduledDowntimeDepth { get; init; }

    [JsonPropertyName("last_check")]
    public long LastCheckUnix { get; init; }

    [JsonIgnore]
    public ServiceState ServiceState => State switch
    {
        0 => ServiceState.Ok,
        1 => ServiceState.Warning,
        2 => ServiceState.Critical,
        3 => ServiceState.Unknown,
        _ => ServiceState.Unknown
    };

    [JsonIgnore]
    public bool IsAcknowledged => Acknowledged != 0;

    [JsonIgnore]
    public bool InDowntime => ScheduledDowntimeDepth > 0;

    [JsonIgnore]
    public DateTimeOffset LastCheck => DateTimeOffset.FromUnixTimeSeconds(LastCheckUnix);
}

public enum ServiceState
{
    Ok = 0,
    Warning = 1,
    Critical = 2,
    Unknown = 3
}
