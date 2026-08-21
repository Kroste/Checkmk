namespace Checkmk.App.Services;

/// <summary>
/// Web-Mercator (EPSG:3857) — dieselbe Projektion, die jede Kachelkarte
/// benutzt. Wir rechnen in „Weltpixeln" auf einer Zoomstufe: Bei Zoom z ist die
/// Welt <c>256 · 2^z</c> Pixel breit.
///
/// Warum das jetzt schon steht, obwohl noch keine Kachel geladen werden kann:
/// Sobald der Proxy die Dienste der LGB durchlässt, ist die Kachel-URL nur noch
/// eine Einstellung — die Mathematik dahinter ist dieselbe, und sie ist der
/// Teil, den man ohne Netz prüfen kann.
/// </summary>
public static class WebMercator
{
    public const int TileSize = 256;

    /// <summary>Grenze der Projektion. Jenseits davon läuft die Mercator-Formel
    /// gegen unendlich — deshalb kappt jede Kachelkarte bei ±85,0511°.</summary>
    public const double MaxLatitude = 85.05112878;

    public static double WorldSize(double zoom) => TileSize * Math.Pow(2, zoom);

    /// <summary>Geografisch → Weltpixel (x nach rechts, y nach unten).</summary>
    public static (double X, double Y) ToWorld(GeoPoint p, double zoom)
    {
        var size = WorldSize(zoom);
        var lat = Math.Clamp(p.Lat, -MaxLatitude, MaxLatitude);

        var x = (p.Lon + 180.0) / 360.0 * size;
        var sin = Math.Sin(lat * Math.PI / 180.0);
        var y = (0.5 - Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * size;
        return (x, y);
    }

    /// <summary>Weltpixel → geografisch. Gegenstück zu <see cref="ToWorld"/>;
    /// braucht der Klick auf die Karte.</summary>
    public static GeoPoint ToGeo(double x, double y, double zoom)
    {
        var size = WorldSize(zoom);
        var lon = x / size * 360.0 - 180.0;
        var n = Math.PI - 2.0 * Math.PI * y / size;
        var lat = 180.0 / Math.PI * Math.Atan(Math.Sinh(n));
        return new GeoPoint(lon, lat);
    }

    /// <summary>Kachel-Index für einen Weltpixel.</summary>
    public static (int X, int Y) TileAt(double worldX, double worldY)
        => ((int)Math.Floor(worldX / TileSize), (int)Math.Floor(worldY / TileSize));

    /// <summary>
    /// Zoomstufe, bei der ein geografisches Rechteck in eine Fläche von
    /// <paramref name="widthPx"/> × <paramref name="heightPx"/> passt.
    /// Für „auf Bereich einpassen".
    /// </summary>
    public static double FitZoom(GeoPoint min, GeoPoint max, double widthPx, double heightPx)
    {
        if (widthPx <= 0 || heightPx <= 0) return 0;

        // Auf Zoom 0 rechnen und daraus den Faktor ableiten: dort ist die Welt
        // genau eine Kachel breit, das macht die Rechnung unabhaengig vom Start.
        var (x1, y1) = ToWorld(min, 0);
        var (x2, y2) = ToWorld(max, 0);

        var spanX = Math.Abs(x2 - x1);
        var spanY = Math.Abs(y2 - y1);
        if (spanX <= 0 && spanY <= 0) return 16;   // ein Punkt: nah heran

        var zoomX = spanX > 0 ? Math.Log2(widthPx / spanX) : double.MaxValue;
        var zoomY = spanY > 0 ? Math.Log2(heightPx / spanY) : double.MaxValue;

        return Math.Clamp(Math.Min(zoomX, zoomY), 0, 21);
    }
}
