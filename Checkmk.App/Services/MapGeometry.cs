using System.Globalization;
using System.Text.Json;

namespace Checkmk.App.Services;

/// <summary>Ein Punkt in WGS84 — so, wie GeoJSON ihn schreibt (Länge zuerst).</summary>
public readonly record struct GeoPoint(double Lon, double Lat);

/// <summary>
/// Polygon eines Bereichs in WGS84. Bewusst <b>geografische</b> Koordinaten und
/// keine Bildpixel: Die Tapete hinter der Karte ist austauschbar (heute ein
/// hinterlegtes Luftbild, später amtliche Kacheln der LGB), die Geometrie soll
/// den Wechsel überleben. Ein einmal gezeichneter Serverraum wird sonst beim
/// Umstellen der Kartenquelle wertlos.
/// </summary>
public static class MapGeometry
{
    /// <summary>
    /// Kleinste Eckenzahl einer Fläche. Zwei Punkte sind eine Linie; als
    /// Polygon gespeichert ergäbe das einen Bereich ohne Inhalt, der sich
    /// später weder anklicken noch sinnvoll anzeigen lässt.
    /// </summary>
    public const int MinimumVertices = 3;

    /// <summary>
    /// Fügt auf der Kante <paramref name="edgeIndex"/> (von dort zum nächsten
    /// Punkt, mit Umlauf) eine neue Ecke in der Mitte ein und gibt die neue
    /// Liste zurück.
    ///
    /// <b>Die Mitte wird geografisch gebildet</b>, nicht auf dem Bildschirm.
    /// Über eine einzelne Kante ist der Unterschied in Mercator vernachlässigbar,
    /// aber so hängt das Ergebnis nicht davon ab, wie weit gerade gezoomt ist —
    /// zweimal Einfügen an derselben Kante ergibt bei jedem Zoom dasselbe.
    /// </summary>
    public static IReadOnlyList<GeoPoint> InsertMidpoint(
        IReadOnlyList<GeoPoint> points, int edgeIndex)
    {
        if (points.Count == 0) return points;
        if (edgeIndex < 0 || edgeIndex >= points.Count) return points;

        var a = points[edgeIndex];
        var b = points[(edgeIndex + 1) % points.Count];

        var result = points.ToList();
        result.Insert(edgeIndex + 1,
            new GeoPoint((a.Lon + b.Lon) / 2, (a.Lat + b.Lat) / 2));
        return result;
    }

    /// <summary>
    /// Entfernt eine Ecke. Gibt die <b>unveränderte</b> Liste zurück, wenn
    /// dabei weniger als <see cref="MinimumVertices"/> Punkte übrig blieben —
    /// so bleibt aus einer Fläche nie stillschweigend eine Linie.
    /// </summary>
    public static IReadOnlyList<GeoPoint> RemoveVertex(
        IReadOnlyList<GeoPoint> points, int index)
    {
        if (index < 0 || index >= points.Count) return points;
        if (points.Count <= MinimumVertices) return points;

        var result = points.ToList();
        result.RemoveAt(index);
        return result;
    }

    /// <summary>
    /// Liest ein GeoJSON-Polygon. Akzeptiert sowohl ein nacktes
    /// <c>Polygon</c>-Geometrieobjekt als auch ein <c>Feature</c> mit
    /// <c>geometry</c> darin — beides kommt aus üblichen Werkzeugen, und an
    /// dieser Unterscheidung soll niemand scheitern.
    /// Nur der äußere Ring wird ausgewertet; Löcher zeichnen wir nicht.
    /// Rückgabe leer, wenn nichts Brauchbares drinsteht.
    /// </summary>
    public static IReadOnlyList<GeoPoint> Parse(string? geoJson)
    {
        if (string.IsNullOrWhiteSpace(geoJson)) return [];

        try
        {
            using var doc = JsonDocument.Parse(geoJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("geometry", out var inner))
                root = inner;

            if (!root.TryGetProperty("coordinates", out var coords)) return [];
            if (coords.ValueKind != JsonValueKind.Array || coords.GetArrayLength() == 0) return [];

            var ring = coords[0];
            if (ring.ValueKind != JsonValueKind.Array) return [];

            var points = new List<GeoPoint>();
            foreach (var pair in ring.EnumerateArray())
            {
                if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2) continue;
                if (!pair[0].TryGetDouble(out var lon)) continue;
                if (!pair[1].TryGetDouble(out var lat)) continue;
                points.Add(new GeoPoint(lon, lat));
            }

            // GeoJSON schliesst den Ring (erster Punkt = letzter). Intern
            // arbeiten wir ohne den Doppelpunkt, damit Bearbeiten und Zaehlen
            // nicht staendig ein Sonderfall sind.
            if (points.Count > 1 && Same(points[0], points[^1]))
                points.RemoveAt(points.Count - 1);

            return points;
        }
        catch (JsonException)
        {
            // Ein kaputtes Polygon darf die Karte nicht sprengen — der Bereich
            // erscheint dann ohne Flaeche und kann neu gezeichnet werden.
            return [];
        }
    }

    /// <summary>Schreibt ein GeoJSON-Polygon (geschlossener Ring, wie der Standard es will).</summary>
    public static string? ToGeoJson(IReadOnlyList<GeoPoint> points)
    {
        if (points.Count < 3) return null;   // weniger ist keine Flaeche

        var sb = new System.Text.StringBuilder();
        sb.Append("{\"type\":\"Polygon\",\"coordinates\":[[");
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0) sb.Append(',');
            Append(points[i]);
        }
        sb.Append(',');
        Append(points[0]);          // Ring schliessen
        sb.Append("]]}");
        return sb.ToString();

        void Append(GeoPoint p) => sb
            .Append('[')
            .Append(p.Lon.ToString("R", CultureInfo.InvariantCulture))
            .Append(',')
            .Append(p.Lat.ToString("R", CultureInfo.InvariantCulture))
            .Append(']');
    }

    /// <summary>
    /// Liegt der Punkt im Polygon? Ray-Casting (ungerade Anzahl Kreuzungen =
    /// innen). Braucht der Rechtsklick auf der Karte, um zu wissen, welchen
    /// Bereich der Anwender getroffen hat.
    /// </summary>
    public static bool Contains(IReadOnlyList<GeoPoint> polygon, GeoPoint point)
    {
        if (polygon.Count < 3) return false;

        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];

            // Kante muss die Waagerechte durch den Punkt kreuzen …
            if (pi.Lat > point.Lat == pj.Lat > point.Lat) continue;

            // … und zwar rechts vom Punkt.
            var x = (pj.Lon - pi.Lon) * (point.Lat - pi.Lat) / (pj.Lat - pi.Lat) + pi.Lon;
            if (point.Lon < x) inside = !inside;
        }
        return inside;
    }

    /// <summary>Umschliessendes Rechteck — zum Einpassen der Ansicht auf einen Bereich.</summary>
    public static (GeoPoint Min, GeoPoint Max)? Bounds(IReadOnlyList<GeoPoint> points)
    {
        if (points.Count == 0) return null;

        double minLon = points[0].Lon, maxLon = minLon;
        double minLat = points[0].Lat, maxLat = minLat;
        foreach (var p in points)
        {
            if (p.Lon < minLon) minLon = p.Lon;
            if (p.Lon > maxLon) maxLon = p.Lon;
            if (p.Lat < minLat) minLat = p.Lat;
            if (p.Lat > maxLat) maxLat = p.Lat;
        }
        return (new GeoPoint(minLon, minLat), new GeoPoint(maxLon, maxLat));
    }

    private static bool Same(GeoPoint a, GeoPoint b)
        => Math.Abs(a.Lon - b.Lon) < 1e-12 && Math.Abs(a.Lat - b.Lat) < 1e-12;
}
