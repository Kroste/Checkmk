using System.Text.Json.Serialization;

namespace Checkmk.Core.Models;

/// <summary>
/// Modi fuer die Service-Discovery. Werden als String-Payload an
/// <c>service_discovery_run/actions/start/invoke</c> gesendet.
/// </summary>
public static class ServiceDiscoveryMode
{
    /// <summary>Neue Services hinzufuegen UND verschwundene entfernen. Standard.</summary>
    public const string FixAll = "fix_all";
    /// <summary>Nur neue Services hinzufuegen.</summary>
    public const string New = "new";
    /// <summary>Nur verschwundene entfernen.</summary>
    public const string Remove = "remove";
    /// <summary>Aktualisiert die Parameter bestehender Services.</summary>
    public const string Refresh = "refresh";
    /// <summary>Nur Host-Labels neu einlesen, keine Services.</summary>
    public const string OnlyHostLabels = "only_host_labels";
    /// <summary>„Tabula rasa" — Reset aller Services, kompletter Neuscan.</summary>
    public const string TabulaRasa = "tabula_rasa";
}

/// <summary>
/// Live-Status eines Service-Discovery-Runs. Wird ueber
/// <c>GET /objects/service_discovery_run/{host_name}</c> gepollt.
/// </summary>
public sealed record ServiceDiscoveryRunState
{
    [JsonPropertyName("active")]
    public bool Active { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }
}
