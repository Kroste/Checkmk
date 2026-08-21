using System.Net;
using System.Text.Json;
using Checkmk.Data;
using NLog;

namespace Checkmk.App.Services;

/// <summary>
/// Holt die Verwaltungsstandorte der Landeshauptstadt aus dem
/// <b>FeatureServer</b> von <c>geoportal.potsdam.de</c>.
///
/// Bewusst über die veröffentlichte REST-Schnittstelle und <b>nicht</b> direkt
/// aus der Datenbank, obwohl die auf demselben Server liegt: Ein direkter
/// Tabellenzugriff hinge an einem internen Schema, von dem der Fachbereich
/// Vermessung nicht weiß, dass wir es lesen — bei einem Umbau dort wäre unsere
/// Anwendung kaputt, ohne dass es jemand kommen sieht. Bei einer
/// veröffentlichten Schnittstelle ist die Erwartung umgekehrt.
/// </summary>
public sealed class PotsdamPlaceImporter : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Wird als <c>ExternalSource</c> gespeichert und identifiziert
    /// importierte Bereiche beim erneuten Abgleich.</summary>
    public const string SourceId = "LHP-Verwaltungsstandorte";

    private const string QueryUrl =
        "https://geoportal.potsdam.de/server/rest/services/Verwaltung_LH_Potsdam/FeatureServer/0/query"
        + "?where=1%3D1&outFields=*&returnGeometry=true&outSR=4326&f=json&resultRecordCount=2000";

    private readonly HttpClient _http;

    public PotsdamPlaceImporter()
    {
        var handler = new HttpClientHandler
        {
            UseProxy = true,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CheckmkCockpit/1.9 (+internes Monitoring)");
    }

    /// <summary>
    /// Lädt die Standortliste. Wirft nicht — der Aufrufer bekommt eine leere
    /// Liste und kann das melden, statt dass ein Dialog mit einer Ausnahme
    /// zuklappt.
    /// </summary>
    public async Task<IReadOnlyList<ExternalPlace>> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync(QueryUrl, ct).ConfigureAwait(false);
            var places = Parse(json);
            Log.Info("{Count} Verwaltungsstandorte aus dem Kartenserver gelesen.", places.Count);
            return places;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Verwaltungsstandorte konnten nicht geladen werden.");
            return [];
        }
    }

    /// <summary>
    /// Wertet die ArcGIS-Antwort aus. Öffentlich für Tests — das Format ist die
    /// Stelle, die sich ohne unser Zutun ändern kann.
    /// </summary>
    public static IReadOnlyList<ExternalPlace> Parse(string json)
    {
        var result = new List<ExternalPlace>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out var features)
                || features.ValueKind != JsonValueKind.Array)
                return result;

            // Mehrere Dienststellen teilen sich oft eine Anschrift (dasselbe
            // Haus). Als Standort ist das EIN Ort — sonst stapeln sich Marker
            // uebereinander und die Karte wird unlesbar.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in features.EnumerateArray())
            {
                if (!f.TryGetProperty("geometry", out var g)) continue;
                if (!g.TryGetProperty("x", out var xe) || !g.TryGetProperty("y", out var ye)) continue;
                if (!xe.TryGetDouble(out var lon) || !ye.TryGetDouble(out var lat)) continue;
                if (!double.IsFinite(lon) || !double.IsFinite(lat)) continue;

                var attrs = f.TryGetProperty("attributes", out var a) ? a : default;
                var name = Text(attrs, "BEHOERDE") ?? Text(attrs, "STANDORT") ?? "Standort";
                var address = BuildAddress(attrs);

                // Kennung: die GLOBALID ist stabil, OBJECTID nur der Notnagel.
                var id = Text(attrs, "GLOBALID")
                         ?? Text(attrs, "OBJECTID")
                         ?? $"{lon:F6},{lat:F6}";

                var key = address ?? $"{lon:F5},{lat:F5}";
                if (!seen.Add(key)) continue;

                result.Add(new ExternalPlace(id, name.Trim(), lat, lon, address));
            }
        }
        catch (JsonException ex)
        {
            Log.Warn(ex, "Antwort des Kartenservers war kein erwartetes JSON.");
        }

        return result;
    }

    private static string? BuildAddress(JsonElement attrs)
    {
        var street = Text(attrs, "ADRESSE");
        var zip = Text(attrs, "PLZ");
        if (street is null && zip is null) return null;
        return string.Join(", ", new[] { street, zip }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static string? Text(JsonElement attrs, string name)
    {
        if (attrs.ValueKind != JsonValueKind.Object) return null;
        if (!attrs.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString(),
            JsonValueKind.Number => v.ToString(),
            _ => null
        };
    }

    public void Dispose() => _http.Dispose();
}
