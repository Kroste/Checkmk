using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using Checkmk.Data;
using NLog;

namespace Checkmk.App.Services;

/// <summary>Kachel-Adresse: Zoomstufe und Index im globalen Web-Mercator-Raster.</summary>
public readonly record struct TileKey(int Zoom, int X, int Y);

/// <summary>
/// Holt Kartenkacheln und hält sie vor — im Speicher und auf der Platte.
///
/// Der Plattencache ist nicht Beiwerk: Die Daten sind Open Data
/// (dl-de/by-2.0), dürfen also gespiegelt werden, und ein Wandmonitor beim
/// Wachschutz, der acht Stunden dieselbe Sicht zeigt, soll den Landesdienst
/// nicht achtstündig befragen. Genau das verbietet Google Maps und ist einer
/// der Gründe, warum wir es nicht benutzen.
///
/// Der <see cref="HttpClient"/> ist wie im übrigen Cockpit konfiguriert:
/// Systemproxy mit Windows-Anmeldedaten. Ohne das antwortet der FortiProxy
/// mit 407.
/// </summary>
public sealed class MapTileLoader : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Halbe Breite der Welt in Web-Mercator-Metern (EPSG:3857).</summary>
    private const double HalfWorld = 20037508.342789244;

    private readonly HttpClient _http;
    private readonly IGlobalSettingsProvider _globals;
    private readonly string _cacheRoot;

    /// <summary>Speichercache. Deckelt sich selbst — eine Sicht braucht selten
    /// mehr als ein paar Dutzend Kacheln, aber beim Zoomen sammeln sich sonst
    /// Hunderte an.</summary>
    private readonly ConcurrentDictionary<TileKey, Bitmap> _memory = new();
    private readonly ConcurrentDictionary<TileKey, byte> _inFlight = new();

    /// <summary>Deckel für gleichzeitige Abrufe. Ein Zoomsprung fordert sonst
    /// 40 Kacheln auf einmal an, und der Dienst ist kein Content-Delivery-Netz.</summary>
    private readonly SemaphoreSlim _gate = new(4, 4);

    /// <summary>Eigener, kleinerer Deckel fuers Vorabladen und Auffrischen —
    /// Hintergrundarbeit darf das Schieben und Zoomen nie ausbremsen.</summary>
    private readonly SemaphoreSlim _prefetchGate = new(2, 2);

    /// <summary>Kacheln, die in dieser Sitzung schon auf Alter geprueft wurden.</summary>
    private readonly ConcurrentDictionary<TileKey, byte> _refreshed = new();

    public MapTileLoader(IGlobalSettingsProvider globals)
    {
        _globals = globals;

        var handler = new HttpClientHandler
        {
            UseProxy = true,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CheckmkCockpit/1.9 (+internes Monitoring)");

        _cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kroste", "Checkmk", "tiles");
    }

    public string Attribution => _globals.Current.MapAttribution;

    /// <summary>Auswählbare Hintergründe aus den zentralen Einstellungen.</summary>
    public IReadOnlyList<MapLayerDefinition> Layers => _globals.Current.MapLayers;

    private MapLayerDefinition? _active;

    /// <summary>
    /// Aktiver Hintergrund. Ohne Auswahl gilt der erste aus <see cref="Layers"/>,
    /// sonst die Einzelwerte <c>MapWmsUrl</c>/<c>MapWmsLayer</c> — so bleibt eine
    /// Installation lauffähig, die nur die alten Schlüssel gesetzt hat.
    /// </summary>
    public MapLayerDefinition Active
    {
        get => _active
            ?? _globals.Current.MapLayers.FirstOrDefault()
            ?? new MapLayerDefinition("Karte", _globals.Current.MapWmsUrl, _globals.Current.MapWmsLayer);
        set
        {
            if (_active == value) return;
            _active = value;
            // Speichercache leeren: sonst zeigt die Karte nach dem Umschalten
            // weiter die Kacheln der alten Ebene. Der Plattencache trennt schon
            // ueber den Hash im Pfad.
            ForgetMemory();
        }
    }

    /// <summary>Kachel aus dem Speicher — <c>null</c>, wenn sie noch nicht da
    /// ist. Die Zeichenfläche fragt so ab und zeichnet erst, was vorliegt.</summary>
    public Bitmap? Peek(TileKey key) => _memory.GetValueOrDefault(key);

    /// <summary>
    /// Stößt das Laden an, falls nötig. <paramref name="onReady"/> läuft, wenn
    /// eine Kachel neu verfügbar ist — die Zeichenfläche zeichnet daraufhin neu.
    /// </summary>
    public void Request(TileKey key, Action onReady)
    {
        if (_memory.ContainsKey(key)) return;
        if (!_inFlight.TryAdd(key, 0)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var bitmap = await LoadAsync(key).ConfigureAwait(false);
                if (bitmap is not null)
                {
                    _memory[key] = bitmap;
                    onReady();
                }
            }
            catch (Exception ex)
            {
                // Eine fehlende Kachel ist ein Loch im Bild, kein Grund zum
                // Abbruch — der Rest der Karte bleibt benutzbar.
                Log.Debug(ex, "Kachel {Z}/{X}/{Y} nicht ladbar.", key.Zoom, key.X, key.Y);
            }
            finally
            {
                _inFlight.TryRemove(key, out _);
            }
        });
    }

    private async Task<Bitmap?> LoadAsync(TileKey key)
    {
        var path = CachePath(key);

        if (File.Exists(path))
        {
            try
            {
                await using var fs = File.OpenRead(path);
                var cached = new Bitmap(fs);
                // Alt gewordene Kachel im Hintergrund erneuern, aber SOFORT die
                // vorhandene zeigen. Der Anwender wartet nie auf eine
                // Auffrischung — dafuer aendern sich Luftbilder viel zu selten.
                ScheduleRefreshIfStale(key, path);
                return cached;
            }
            catch (Exception ex)
            {
                // Halb geschriebene Datei nach einem Absturz: wegwerfen und neu holen.
                Log.Debug(ex, "Kachel aus dem Cache unlesbar, hole neu: {Path}", path);
                TryDelete(path);
            }
        }

        // Gemeinsamer Speicher: Was ein Kollege schon geholt hat, muss niemand
        // erneut beim Landesdienst anfragen. Kalt kostet eine Kachel ueber eine
        // Sekunde, aus dem Cache acht Millisekunden.
        if (SharedPath(key) is { } shared && File.Exists(shared))
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(shared).ConfigureAwait(false);
                if (LooksLikeImage(bytes))
                {
                    WriteCache(path, bytes);   // lokal spiegeln: der Share kann weg sein
                    using var ms = new MemoryStream(bytes);
                    return new Bitmap(ms);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Kachel aus dem gemeinsamen Speicher nicht lesbar: {Path}", shared);
            }
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var url = BuildUrl(key);
            using var response = await _http.GetAsync(url).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Debug("Kachelserver antwortete {Status} fuer {Z}/{X}/{Y}.",
                    (int)response.StatusCode, key.Zoom, key.X, key.Y);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (!LooksLikeImage(bytes))
            {
                // WMS meldet Fehler gern als XML mit Status 200. Ohne diese
                // Pruefung landet die Fehlermeldung als kaputte Datei im Cache
                // und wird nie wieder neu geholt.
                Log.Debug("Kachel {Z}/{X}/{Y}: keine Bilddaten ({Len} B).",
                    key.Zoom, key.X, key.Y, bytes.Length);
                return null;
            }

            WriteCache(path, bytes);
            if (SharedPath(key) is { } target) WriteShared(target, bytes);

            using var ms = new MemoryStream(bytes);
            return new Bitmap(ms);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Laedt Kacheln im Hintergrund, damit der erste Blick auf einen Standort
    /// nicht fuenf Sekunden dauert. Eigener, kleinerer Deckel als der
    /// interaktive Pfad: Vorabladen darf das Schieben und Zoomen nie ausbremsen.
    /// </summary>
    public async Task PrefetchAsync(IEnumerable<TileKey> keys, IProgress<(int Done, int Total)>? progress,
        CancellationToken ct = default)
    {
        var todo = keys.Where(k => !_memory.ContainsKey(k) && !File.Exists(CachePath(k))).ToList();
        if (todo.Count == 0) return;

        Log.Info("Kachel-Vorabladen: {Count} fehlen fuer Ebene '{Layer}'.", todo.Count, Active.Name);

        var done = 0;
        foreach (var key in todo)
        {
            if (ct.IsCancellationRequested) break;
            await _prefetchGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var bitmap = await LoadAsync(key).ConfigureAwait(false);
                // Nicht in den Speichercache legen: Vorabladen fuellt die
                // Platte, nicht den Arbeitsspeicher — sonst haelt eine
                // Hintergrundaufgabe hunderte Bitmaps fest.
                bitmap?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Vorabladen von {Z}/{X}/{Y} fehlgeschlagen.", key.Zoom, key.X, key.Y);
            }
            finally
            {
                _prefetchGate.Release();
                progress?.Report((Interlocked.Increment(ref done), todo.Count));
            }
        }

        Log.Info("Kachel-Vorabladen abgeschlossen: {Done}/{Total}.", done, todo.Count);
    }

    /// <summary>Erneuert eine veraltete Kachel im Hintergrund — hoechstens eine
    /// je Kachel und Sitzung.</summary>
    private void ScheduleRefreshIfStale(TileKey key, string path)
    {
        var maxAge = _globals.Current.MapTileMaxAgeDays;
        if (maxAge <= 0) return;
        if (!_refreshed.TryAdd(key, 0)) return;

        try
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromDays(maxAge)) return;
        }
        catch (IOException) { return; }

        _ = Task.Run(async () =>
        {
            await _prefetchGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var url = BuildUrl(key);
                using var response = await _http.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return;
                var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (!LooksLikeImage(bytes)) return;

                WriteCache(path, bytes);
                if (SharedPath(key) is { } target) WriteShared(target, bytes);
                Log.Debug("Kachel {Z}/{X}/{Y} aufgefrischt.", key.Zoom, key.X, key.Y);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Auffrischen von {Z}/{X}/{Y} fehlgeschlagen.", key.Zoom, key.X, key.Y);
            }
            finally { _prefetchGate.Release(); }
        });
    }

    /// <summary>Pfad im gemeinsamen Speicher, oder <c>null</c> wenn keiner
    /// eingerichtet ist.</summary>
    private string? SharedPath(TileKey key)
    {
        var root = _globals.Current.MapTileSharePath;
        if (string.IsNullOrWhiteSpace(root)) return null;
        return Path.Combine(root, LayerId(), key.Zoom.ToString(), key.X.ToString(), $"{key.Y}.png");
    }

    /// <summary>
    /// Schreibt in den gemeinsamen Speicher, wenn es geht. Fehlschlaege sind
    /// der Normalfall — die meisten Nutzer haben dort nur Leserecht, und das
    /// ist Absicht.
    /// </summary>
    private static void WriteShared(string path, byte[] bytes)
    {
        try
        {
            if (File.Exists(path)) return;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Ueber eine Datei mit Zufallsnamen: zwei Clients koennen dieselbe
            // Kachel gleichzeitig schreiben wollen.
            var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            try { File.Move(tmp, path, overwrite: false); }
            catch (IOException) { File.Delete(tmp); }   // war jemand schneller
        }
        catch (Exception)
        {
            // Kein Log: bei fehlendem Schreibrecht waere das eine Zeile je Kachel.
        }
    }

    private string BuildUrl(TileKey key) => BuildUrl(Active.Url, Active.Layer, key, Active.Crs);

    /// <summary>
    /// WMS-GetMap für genau die Bounding-Box dieser Kachel, in EPSG:3857.
    ///
    /// Zwei Festlegungen, die nicht geraten sind, sondern gegen den Dienst der
    /// LGB geprüft wurden:
    /// <list type="number">
    /// <item><b>Version 1.1.1 mit <c>SRS</c></b> statt 1.3.0 mit <c>CRS</c>. In
    /// 1.3.0 hängt die Achsenreihenfolge vom Koordinatensystem ab — daran
    /// vertauschen sich Länge und Breite lautlos, und die Karte zeigt
    /// irgendwas.</item>
    /// <item><b>WMS statt WMTS.</b> Das Matrix-Set <c>grid_3857</c> der LGB hat
    /// einen auf Brandenburg beschränkten Ursprung und weist globale
    /// Slippy-Map-Kachelindizes mit <c>TileOutOfRange</c> ab. Über GetMap gibt
    /// der Client die BBOX selbst vor; MapProxy liefert trotzdem aus seinem
    /// Kachel-Cache.</item>
    /// </list>
    /// </summary>
    internal static string BuildUrl(string baseUrl, string layer, TileKey key,
        string crs = "EPSG:3857")
    {
        var bbox = crs.EndsWith("4326", StringComparison.Ordinal)
            ? GeographicBbox(key)
            : MercatorBbox(key);

        var separator = baseUrl.Contains('?') ? "&" : "?";
        return $"{baseUrl}{separator}REQUEST=GetMap&SERVICE=WMS&VERSION=1.1.1"
             + $"&FORMAT=image/png&LAYERS={Uri.EscapeDataString(layer)}"
             + $"&SRS={Uri.EscapeDataString(crs)}"
             + $"&WIDTH={WebMercator.TileSize}&HEIGHT={WebMercator.TileSize}"
             + $"&BBOX={bbox}&STYLES=&TRANSPARENT=FALSE";
    }

    private static string MercatorBbox(TileKey key)
    {
        var span = 2 * HalfWorld / Math.Pow(2, key.Zoom);
        var minX = -HalfWorld + key.X * span;
        var maxY = HalfWorld - key.Y * span;
        return Join(minX, maxY - span, minX + span, maxY, "F3");
    }

    /// <summary>
    /// Kachelgrenzen in Grad — für Dienste, die kein Web-Mercator können.
    ///
    /// Der Server rendert die Grad-Ausdehnung linear (Plattkarte), Web-Mercator
    /// ist dagegen in der Breite gestreckt. Über eine einzelne Kachel ist der
    /// Unterschied vernachlässigbar, weil sich der Streckungsfaktor auf so
    /// kleiner Fläche kaum ändert — bei ~150 m Kantenlänge (Zoom 18) liegt der
    /// Versatz weit unter einem Pixel. Auf kleinen Zoomstufen wäre das nicht
    /// mehr wahr; die betroffenen Dienste sind aber ohnehin Gebäudekarten, die
    /// man nur nah heran benutzt.
    ///
    /// WMS 1.1.1 erwartet bei EPSG:4326 die Reihenfolge Länge, Breite — in
    /// 1.3.0 wäre sie umgekehrt. Noch ein Grund, bei 1.1.1 zu bleiben.
    /// </summary>
    private static string GeographicBbox(TileKey key)
    {
        var size = (double)WebMercator.TileSize;
        var topLeft = WebMercator.ToGeo(key.X * size, key.Y * size, key.Zoom);
        var bottomRight = WebMercator.ToGeo((key.X + 1) * size, (key.Y + 1) * size, key.Zoom);
        return Join(topLeft.Lon, bottomRight.Lat, bottomRight.Lon, topLeft.Lat, "F7");
    }

    private static string Join(double a, double b, double c, double d, string format)
        => string.Join(',', new[] { a, b, c, d }
            .Select(v => v.ToString(format, CultureInfo.InvariantCulture)));

    /// <summary>PNG- oder JPEG-Signatur. Reicht, um XML-Fehlermeldungen abzuweisen.</summary>
    private static bool LooksLikeImage(byte[] bytes)
        => bytes.Length > 8
           && ((bytes[0] == 0x89 && bytes[1] == 0x50)    // PNG
               || (bytes[0] == 0xFF && bytes[1] == 0xD8)); // JPEG

    /// <summary>
    /// Cache-Pfad. Der Layer geht als Kurz-Hash in den Pfad ein, damit ein
    /// Wechsel der Kartenquelle nicht auf die Kacheln der alten trifft — sonst
    /// zeigt die Karte nach dem Umstellen weiter das alte Bild.
    /// </summary>
    private string CachePath(TileKey key)
        => Path.Combine(_cacheRoot, LayerId(), key.Zoom.ToString(), key.X.ToString(), $"{key.Y}.png");

    /// <summary>Kurz-Hash aus Adresse und Layer — trennt die Ebenen im Cache,
    /// sonst zeigt die Karte nach dem Umschalten weiter das alte Bild.</summary>
    private string LayerId()
        => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Active.Url + "|" + Active.Layer)))[..8];

    private static void WriteCache(string path, byte[] bytes)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Ueber eine temporaere Datei: ein Abbruch mitten im Schreiben
            // hinterlaesst sonst eine halbe PNG, die spaeter als gueltiger
            // Cache-Treffer gilt.
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Kachel konnte nicht zwischengespeichert werden: {Path}", path);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { /* egal */ }
    }

    /// <summary>Speichercache leeren — beim Quellenwechsel.</summary>
    public void ForgetMemory()
    {
        foreach (var b in _memory.Values) b.Dispose();
        _memory.Clear();
    }

    public void Dispose()
    {
        ForgetMemory();
        _http.Dispose();
        _gate.Dispose();
    }
}
