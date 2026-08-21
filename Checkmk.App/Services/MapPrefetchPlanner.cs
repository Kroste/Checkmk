namespace Checkmk.App.Services;

/// <summary>
/// Rechnet aus, welche Kacheln vorab geholt werden sollen, damit die
/// Standort-Sicht ohne Wartezeit — und notfalls ohne Internet — funktioniert.
///
/// Gemessen: Eine kalte Kachel kostet gut eine Sekunde, aus dem Cache acht
/// Millisekunden. Ein Bildschirm voll sind rund ein Dutzend Kacheln, also
/// fünf Sekunden für jeden neuen Ausschnitt und jede Zoomstufe. Vorabladen
/// verlegt diese Wartezeit in den Hintergrund.
///
/// Bewusst <b>keine Flächendeckung</b>: Potsdam vollständig bis Zoom 18 wären
/// über 40.000 Kacheln je Ebene. Geholt wird nur, was jemand tatsächlich
/// anschaut — die Umgebung der bekannten Standorte plus eine Stadtübersicht.
/// </summary>
public static class MapPrefetchPlanner
{
    /// <summary>Übersichtszoom: ganz Potsdam, damit die Startansicht sofort steht.</summary>
    public const int OverviewMinZoom = 11;
    public const int OverviewMaxZoom = 14;

    /// <summary>Detailzoom je Standort — auf dieser Höhe erkennt man Gebäude.</summary>
    public const int DetailMinZoom = 15;
    public const int DetailMaxZoom = 18;

    /// <summary>
    /// Kacheln für die Umgebung eines Punktes. <paramref name="radius"/> ist die
    /// Zahl der Kacheln in jede Richtung: 1 ergibt 3×3, also bei Zoom 18 rund
    /// 450 m Kantenlänge — genug, um sich zu orientieren, ohne die Menge
    /// explodieren zu lassen.
    /// </summary>
    public static IEnumerable<TileKey> AroundPoint(GeoPoint point, int minZoom, int maxZoom, int radius = 1)
    {
        for (var z = minZoom; z <= maxZoom; z++)
        {
            var (wx, wy) = WebMercator.ToWorld(point, z);
            var (cx, cy) = WebMercator.TileAt(wx, wy);
            var max = 1 << z;

            for (var dx = -radius; dx <= radius; dx++)
            for (var dy = -radius; dy <= radius; dy++)
            {
                var x = cx + dx;
                var y = cy + dy;
                if (x < 0 || y < 0 || x >= max || y >= max) continue;   // ausserhalb der Welt
                yield return new TileKey(z, x, y);
            }
        }
    }

    /// <summary>Kacheln, die ein geografisches Rechteck abdecken.</summary>
    public static IEnumerable<TileKey> ForBounds(GeoPoint min, GeoPoint max, int minZoom, int maxZoom)
    {
        for (var z = minZoom; z <= maxZoom; z++)
        {
            var (x1, y1) = WebMercator.ToWorld(new GeoPoint(min.Lon, max.Lat), z);   // oben links
            var (x2, y2) = WebMercator.ToWorld(new GeoPoint(max.Lon, min.Lat), z);   // unten rechts
            var (tx1, ty1) = WebMercator.TileAt(x1, y1);
            var (tx2, ty2) = WebMercator.TileAt(x2, y2);
            var limit = 1 << z;

            for (var x = Math.Max(0, tx1); x <= Math.Min(limit - 1, tx2); x++)
            for (var y = Math.Max(0, ty1); y <= Math.Min(limit - 1, ty2); y++)
                yield return new TileKey(z, x, y);
        }
    }

    /// <summary>
    /// Der vollständige Plan: Stadtübersicht plus Umgebung jedes Standorts,
    /// ohne Dubletten. <paramref name="cityBounds"/> darf <c>null</c> sein —
    /// dann wird nur um die Standorte herum geholt.
    /// </summary>
    public static IReadOnlyList<TileKey> Plan(
        IEnumerable<GeoPoint> places,
        (GeoPoint Min, GeoPoint Max)? cityBounds)
    {
        var set = new HashSet<TileKey>();

        if (cityBounds is { } b)
            foreach (var t in ForBounds(b.Min, b.Max, OverviewMinZoom, OverviewMaxZoom))
                set.Add(t);

        foreach (var p in places)
            foreach (var t in AroundPoint(p, DetailMinZoom, DetailMaxZoom))
                set.Add(t);

        // Grobe Stufen zuerst: Die Übersicht steht damit als Erstes, und wer
        // während des Vorabladens schon hineinzoomt, sieht wenigstens etwas.
        return [.. set.OrderBy(t => t.Zoom)];
    }
}
