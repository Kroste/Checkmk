using System.Text.Json;

namespace Checkmk.Data;

/// <summary>
/// Die geteilten Vorgaben, die bisher in <c>bootstrap.json</c> auf dem
/// Fileshare standen. In <c>bootstrap.json</c> bleibt nur noch, wo die
/// Datenbank steht — den Rest holt sich jeder Client hier.
///
/// Nicht enthalten und niemals hier: das Verbindungs-Secret und die
/// SSH-Passwoerter. Die bleiben user-lokal und DPAPI-gebunden.
/// </summary>
public sealed class CockpitGlobals
{
    public const string KeyHostDefaultDomain   = "HostDefaultDomain";
    public const string KeyUpdateChannelUrl    = "UpdateChannelUrl";
    public const string KeyHostOsAttributeKeys = "HostOsAttributeKeys";
    public const string KeyShowHostCreation    = "ShowHostCreation";

    public string HostDefaultDomain { get; init; } = "lhp.intern";

    public string UpdateChannelUrl { get; init; } =
        "https://api.github.com/repos/Kroste/Checkmk/releases/latest";

    /// <summary>Kandidaten-Keys fuer die OS-Familie im Host-Config-Dict, erster
    /// Treffer gewinnt.</summary>
    public IReadOnlyList<string> HostOsAttributeKeys { get; init; } =
    [
        "tag_operation_system",
        "operation_system",
        "operating_system",
        "os_family"
    ];

    /// <summary>Blendet das „Host anlegen"-Formular ein. Default false.</summary>
    public bool ShowHostCreation { get; init; }

    /// <summary>
    /// Baut die Vorgaben aus den Schluessel/Wert-Zeilen. Unbekannte Schluessel
    /// werden ignoriert, fehlende behalten ihren Default — ein halb gepflegter
    /// Datenbestand darf die Anwendung nicht lahmlegen.
    /// </summary>
    public static CockpitGlobals FromRows(IReadOnlyDictionary<string, string?> rows)
    {
        var fallback = new CockpitGlobals();

        return new CockpitGlobals
        {
            HostDefaultDomain = Text(KeyHostDefaultDomain) ?? fallback.HostDefaultDomain,
            UpdateChannelUrl  = Text(KeyUpdateChannelUrl)  ?? fallback.UpdateChannelUrl,
            HostOsAttributeKeys = StringList(KeyHostOsAttributeKeys) ?? fallback.HostOsAttributeKeys,
            ShowHostCreation  = Bool(KeyShowHostCreation) ?? fallback.ShowHostCreation
        };

        string? Text(string key)
            => rows.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        bool? Bool(string key)
            => Text(key) is { } s && bool.TryParse(s, out var b) ? b : null;

        IReadOnlyList<string>? StringList(string key)
        {
            if (Text(key) is not { } s) return null;
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(s);
                return parsed is { Count: > 0 } ? parsed : null;
            }
            catch (JsonException)
            {
                // Kaputte Liste = Default nehmen. Wer sie verstellt hat, sieht es
                // daran, dass sich nichts aendert; die Alternative waere eine
                // Anwendung, die wegen eines Kommas nicht startet.
                return null;
            }
        }
    }

    public IReadOnlyDictionary<string, string?> ToRows() => new Dictionary<string, string?>
    {
        [KeyHostDefaultDomain]   = HostDefaultDomain,
        [KeyUpdateChannelUrl]    = UpdateChannelUrl,
        [KeyHostOsAttributeKeys] = JsonSerializer.Serialize(HostOsAttributeKeys),
        [KeyShowHostCreation]    = ShowHostCreation.ToString()
    };
}
