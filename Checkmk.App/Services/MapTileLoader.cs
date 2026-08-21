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
                return new Bitmap(fs);
            }
            catch (Exception ex)
            {
                // Halb geschriebene Datei nach einem Absturz: wegwerfen und neu holen.
                Log.Debug(ex, "Kachel aus dem Cache unlesbar, hole neu: {Path}", path);
                TryDelete(path);
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
            using var ms = new MemoryStream(bytes);
            return new Bitmap(ms);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string BuildUrl(TileKey key) => BuildUrl(Active.Url, Active.Layer, key);

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
    internal static string BuildUrl(string baseUrl, string layer, TileKey key)
    {
        var span = 2 * HalfWorld / Math.Pow(2, key.Zoom);
        var minX = -HalfWorld + key.X * span;
        var maxY = HalfWorld - key.Y * span;

        var bbox = string.Join(',', new[] { minX, maxY - span, minX + span, maxY }
            .Select(v => v.ToString("F3", CultureInfo.InvariantCulture)));

        var separator = baseUrl.Contains('?') ? "&" : "?";
        return $"{baseUrl}{separator}REQUEST=GetMap&SERVICE=WMS&VERSION=1.1.1"
             + $"&FORMAT=image/png&LAYERS={Uri.EscapeDataString(layer)}"
             + $"&SRS=EPSG:3857&WIDTH={WebMercator.TileSize}&HEIGHT={WebMercator.TileSize}"
             + $"&BBOX={bbox}&STYLES=";
    }

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
    {
        var id = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Active.Url + "|" + Active.Layer)))[..8];
        return Path.Combine(_cacheRoot, id, key.Zoom.ToString(), key.X.ToString(), $"{key.Y}.png");
    }

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
