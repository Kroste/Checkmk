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

    [JsonPropertyName("host_alias")]
    public string? HostAlias { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Anzeigename des Service (Livestatus <c>display_name</c>). Ist normalerweise
    /// identisch mit <see cref="Description"/>, weicht aber ab, sobald in Checkmk
    /// ein „Alias" fuer den Service gesetzt ist. Kann leer sein, wenn die Site die
    /// Spalte nicht liefert — dafuer gibt es <see cref="DisplayNameOrDescription"/>.
    /// </summary>
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

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

    [JsonPropertyName("last_state_change")]
    public long LastStateChangeUnix { get; init; }

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

    [JsonIgnore]
    public DateTimeOffset LastStateChange => DateTimeOffset.FromUnixTimeSeconds(LastStateChangeUnix);

    /// <summary>Faellt auf <see cref="Description"/> zurueck, wenn die Site keinen
    /// abweichenden Anzeigenamen liefert. Das ist der Wert, den eine Spalte
    /// „service_display_name" zeigen soll.</summary>
    [JsonIgnore]
    public string DisplayNameOrDescription
        => string.IsNullOrWhiteSpace(DisplayName) ? Description : DisplayName;

    /// <summary>Zeit seit der letzten Statusaenderung, kompakt (z. B. "2 d", "3 h", "15 m").</summary>
    [JsonIgnore]
    public string Age => FormatAge(LastStateChangeUnix);

    /// <summary>Zeit seit dem letzten Check, kompakt — Checkmk-Sicht „svc_check_age".
    /// Ein grosser Wert heisst: der Check laeuft nicht mehr, unabhaengig vom State.</summary>
    [JsonIgnore]
    public string CheckAge => FormatAge(LastCheckUnix);

    /// <summary>Kompakte Altersangabe zu einem Unix-Zeitstempel. 0/negativ =&gt; "-",
    /// weil Livestatus fuer „nie passiert" eine 0 liefert (und nicht null).</summary>
    private static string FormatAge(long unixSeconds)
    {
        if (unixSeconds <= 0)
            return "-";

        var span = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        if (span.TotalDays >= 1)
            return $"{(int)span.TotalDays} d {span.Hours} h";
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours} h {span.Minutes} m";
        if (span.TotalMinutes >= 1)
            return $"{(int)span.TotalMinutes} m";
        return $"{(int)span.TotalSeconds} s";
    }
}

public enum ServiceState
{
    Ok = 0,
    Warning = 1,
    Critical = 2,
    Unknown = 3
}
