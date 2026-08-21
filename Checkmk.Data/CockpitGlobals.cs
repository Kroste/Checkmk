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
/// <summary>Ein auswaehlbarer Kartenhintergrund (WMS-Adresse + Layername).</summary>
public sealed record MapLayerDefinition(string Name, string Url, string Layer);

public sealed class CockpitGlobals
{
    public const string KeyHostDefaultDomain   = "HostDefaultDomain";
    public const string KeyUpdateChannelUrl    = "UpdateChannelUrl";
    public const string KeyHostOsAttributeKeys = "HostOsAttributeKeys";
    public const string KeyShowHostCreation    = "ShowHostCreation";
    public const string KeyMapWmsUrl           = "MapWmsUrl";
    public const string KeyMapWmsLayer         = "MapWmsLayer";
    public const string KeyMapAttribution      = "MapAttribution";
    public const string KeyMapLayers           = "MapLayers";

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
    /// WMS-Basisadresse der Kartenkacheln. Vorgabe sind die Digitalen
    /// Orthophotos 20 cm der LGB Brandenburg (Open Data, dl-de/by-2.0).
    ///
    /// Bewusst der <b>WMS</b>-Endpunkt und nicht WMTS: Das Matrix-Set
    /// <c>grid_3857</c> der LGB hat einen auf Brandenburg beschraenkten
    /// Ursprung und weist globale Slippy-Map-Kachelindizes mit
    /// <c>TileOutOfRange</c> ab. Ueber <c>GetMap</c> gibt der Client die
    /// Bounding-Box selbst vor — die rechnet <c>WebMercator</c> ohnehin aus,
    /// und MapProxy liefert trotzdem aus seinem Kachel-Cache.
    /// </summary>
    public string MapWmsUrl { get; init; } =
        "https://isk.geobasis-bb.de/mapproxy/dop20c/service/wms";

    public string MapWmsLayer { get; init; } = "bebb_dop20c";

    /// <summary>Quellenvermerk. <b>Pflicht</b> nach dl-de/by-2.0 und deshalb
    /// fest im Kartenbild, nicht in einem Menue vergraben.</summary>
    public string MapAttribution { get; init; } = "© GeoBasis-DE/LGB, dl-de/by-2-0";

    /// <summary>
    /// Auswaehlbare Kartenhintergruende. Alle vier sind gegen den Dienst der LGB
    /// geprueft (2026-08-21) und liefern echte Kacheln fuer Potsdam.
    ///
    /// Warum mehrere: Auf einem Luftbild sind eingefaerbte Flaechen schwer zu
    /// lesen, weil der Untergrund selbst bunt ist. Der Stadtplan zeigt
    /// Strassennamen zum Wiederfinden, die Graustufen-Karte laesst die Ampel am
    /// deutlichsten hervortreten. Welche passt, entscheidet die Aufgabe — also
    /// umschaltbar statt vorgeschrieben.
    /// </summary>
    public IReadOnlyList<MapLayerDefinition> MapLayers { get; init; } =
    [
        new("Luftbild",          "https://isk.geobasis-bb.de/mapproxy/dop20c/service/wms",           "bebb_dop20c"),
        new("Stadtplan",         "https://isk.geobasis-bb.de/mapproxy/basemapde-bebb/service/wms",   "basemapde_farbe"),
        new("Topographisch grau","https://isk.geobasis-bb.de/mapproxy/dtk10grau/service/wms",        "bb_dtk10_grau"),
        new("Luftbild grau",     "https://isk.geobasis-bb.de/mapproxy/dop20g/service/wms",           "bebb_dop20g")
    ];

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
            ShowHostCreation  = Bool(KeyShowHostCreation) ?? fallback.ShowHostCreation,
            MapWmsUrl         = Text(KeyMapWmsUrl)        ?? fallback.MapWmsUrl,
            MapWmsLayer       = Text(KeyMapWmsLayer)      ?? fallback.MapWmsLayer,
            MapAttribution    = Text(KeyMapAttribution)   ?? fallback.MapAttribution,
            MapLayers         = LayerList(KeyMapLayers)   ?? fallback.MapLayers
        };

        string? Text(string key)
            => rows.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        bool? Bool(string key)
            => Text(key) is { } s && bool.TryParse(s, out var b) ? b : null;

        IReadOnlyList<MapLayerDefinition>? LayerList(string key)
        {
            if (Text(key) is not { } s) return null;
            try
            {
                var parsed = JsonSerializer.Deserialize<List<MapLayerDefinition>>(
                    s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                // Eintraege ohne Adresse oder Layer waeren stumme Fehlkacheln —
                // lieber aussortieren als eine leere Karte zeigen.
                var usable = parsed?
                    .Where(l => !string.IsNullOrWhiteSpace(l.Name)
                             && !string.IsNullOrWhiteSpace(l.Url)
                             && !string.IsNullOrWhiteSpace(l.Layer))
                    .ToList();
                return usable is { Count: > 0 } ? usable : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

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
        [KeyShowHostCreation]    = ShowHostCreation.ToString(),
        [KeyMapWmsUrl]           = MapWmsUrl,
        [KeyMapWmsLayer]         = MapWmsLayer,
        [KeyMapAttribution]      = MapAttribution,
        [KeyMapLayers]           = JsonSerializer.Serialize(MapLayers)
    };
}
