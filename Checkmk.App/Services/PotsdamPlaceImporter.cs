using System.Net;
using System.Text.Json;
using Checkmk.Data;
using NLog;

namespace Checkmk.App.Services;

/// <summary>
/// Eine importierbare Standortliste des städtischen Kartenservers.
/// </summary>
/// <param name="Id">Wird als <c>ExternalSource</c> gespeichert und identifiziert
/// importierte Bereiche beim erneuten Abgleich. <b>Nicht ändern</b> — sonst
/// gelten alte Importe als fremd und werden dupliziert.</param>
/// <param name="MergeByAddress">true, wenn mehrere Einträge derselben Anschrift
/// ein Standort sind. Bei den Verwaltungsstandorten sitzen bis zu einem Dutzend
/// Dienststellen im selben Haus; bei Schulen ist dagegen jeder Eintrag eine
/// eigene Einrichtung, auch wenn zwei sich ein Gelände teilen.</param>
public sealed record PlaceSource(
    string Id,
    string Label,
    string Service,
    int LayerId,
    bool MergeByAddress);

/// <summary>
/// Holt Standortlisten aus den <b>FeatureServern</b> von
/// <c>geoportal.potsdam.de</c>.
///
/// Bewusst über die veröffentlichte REST-Schnittstelle und <b>nicht</b> direkt
/// aus der Datenbank, obwohl die auf demselben FOC-SQL01 liegt: Ein
/// Tabellenzugriff hinge an einem internen Schema, von dem der Fachbereich
/// Vermessung nicht weiß, dass wir es lesen — bei einem Umbau dort wäre das
/// Cockpit kaputt, ohne dass es jemand kommen sieht. Bei einer veröffentlichten
/// Schnittstelle ist die Erwartung umgekehrt.
/// </summary>
public sealed class PotsdamPlaceImporter : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Die Dienste unterscheiden sich in den Feldnamen — Verwaltungsstandorte
    /// führen <c>BEHOERDE</c>/<c>ADRESSE</c>, Schulen <c>NAME</c>/<c>STRASSE</c>.
    /// Statt je Quelle eine Zuordnung zu pflegen, probieren wir Kandidaten
    /// durch; erster Treffer gewinnt. Dasselbe Muster wie bei den
    /// OS-Attribut-Keys, und es überlebt kleine Umbenennungen beim Anbieter.
    /// </summary>
    private static readonly string[] NameFields = ["BEHOERDE", "NAME", "STANDORT"];
    private static readonly string[] StreetFields = ["ADRESSE", "STRASSE"];
    private static readonly string[] IdFields = ["GLOBALID", "OBJECTID"];

    public static readonly IReadOnlyList<PlaceSource> Sources =
    [
        new("LHP-Verwaltungsstandorte", "Verwaltungsstandorte",
            "Verwaltung_LH_Potsdam", 0, MergeByAddress: true),
        new("LHP-Schulen", "Schulen", "Schulen", 0, MergeByAddress: false),
        new("LHP-Hochschulen", "Hochschulen", "Hochschulen", 0, MergeByAddress: false),
    ];

    /// <summary>Bisheriger Standardwert — bleibt als Konstante, damit ältere
    /// Aufrufer und bereits importierte Zeilen zusammenpassen.</summary>
    public const string SourceId = "LHP-Verwaltungsstandorte";

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

    private static string QueryUrl(PlaceSource source)
        => $"https://geoportal.potsdam.de/server/rest/services/{source.Service}"
         + $"/FeatureServer/{source.LayerId}/query"
         + "?where=1%3D1&outFields=*&returnGeometry=true&outSR=4326&f=json&resultRecordCount=2000";

    /// <summary>
    /// Lädt eine Standortliste. Wirft nicht — der Aufrufer bekommt eine leere
    /// Liste und kann das melden, statt dass ein Dialog mit einer Ausnahme
    /// zuklappt.
    /// </summary>
    public async Task<IReadOnlyList<ExternalPlace>> LoadAsync(PlaceSource source,
        CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync(QueryUrl(source), ct).ConfigureAwait(false);
            var places = Parse(json, source.MergeByAddress);
            Log.Info("{Count} Standorte aus {Source} gelesen.", places.Count, source.Label);
            return places;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Standortliste {Source} konnte nicht geladen werden.", source.Label);
            return [];
        }
    }

    /// <summary>
    /// Wertet die ArcGIS-Antwort aus. Öffentlich für Tests — das Format ist die
    /// Stelle, die sich ohne unser Zutun ändern kann.
    /// </summary>
    public static IReadOnlyList<ExternalPlace> Parse(string json, bool mergeByAddress = true)
    {
        var result = new List<ExternalPlace>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out var features)
                || features.ValueKind != JsonValueKind.Array)
                return result;

            var seenPlace = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenId = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in features.EnumerateArray())
            {
                if (!f.TryGetProperty("geometry", out var g)) continue;
                if (!g.TryGetProperty("x", out var xe) || !g.TryGetProperty("y", out var ye)) continue;
                if (!xe.TryGetDouble(out var lon) || !ye.TryGetDouble(out var lat)) continue;
                if (!double.IsFinite(lon) || !double.IsFinite(lat)) continue;

                var attrs = f.TryGetProperty("attributes", out var a) ? a : default;
                var name = First(attrs, NameFields) ?? "Standort";
                var address = BuildAddress(attrs);
                var id = First(attrs, IdFields) ?? $"{lon:F6},{lat:F6}";

                if (mergeByAddress)
                {
                    var key = address ?? $"{lon:F5},{lat:F5}";
                    if (!seenPlace.Add(key)) continue;
                }

                // Doppelte Kennungen wuerden am eindeutigen Index scheitern.
                if (!seenId.Add(id)) continue;

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
        var street = First(attrs, StreetFields);
        var zip = Text(attrs, "PLZ");
        if (street is null && zip is null) return null;
        return string.Join(", ", new[] { street, zip }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static string? First(JsonElement attrs, IReadOnlyList<string> candidates)
    {
        foreach (var c in candidates)
            if (Text(attrs, c) is { } v) return v;
        return null;
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
