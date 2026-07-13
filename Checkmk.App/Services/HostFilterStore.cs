using System.Text.Json;
using System.Text.Json.Serialization;
using Checkmk.App.Models;
using NLog;

namespace Checkmk.App.Services;

public sealed class HostFilterState
{
    public List<HostFilter> Filters { get; set; } = new();
    public string? ActiveFilterName { get; set; }
}

/// <summary>Root-Dokument: pro Site ein eigenes <see cref="HostFilterState"/>.</summary>
public sealed class HostFilterDoc
{
    /// <summary>Key = Site-Name. Case wird beim Lesen normalisiert (case-insensitive).</summary>
    [JsonPropertyName("Sites")]
    public Dictionary<string, HostFilterState> Sites { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public interface IHostFilterStore
{
    HostFilterState Load(string site);
    void Save(string site, HostFilterState state);
    string FilePath { get; }
}

/// <summary>
/// Host-Filter liegen user-lokal (nicht sensibel, deshalb unverschluesselt) unter
/// <c>%APPDATA%\Kroste\Checkmk\filter.json</c>. Pro Site ein eigenes Filter-Set —
/// LHP-Favoriten haben in Schul_IT nichts zu suchen und umgekehrt.
/// </summary>
public sealed class HostFilterStore : IHostFilterStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private HostFilterDoc _doc;

    public string FilePath => _path;

    public HostFilterStore(IConnectionSettingsStore settings)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kroste", "Checkmk");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "filter.json");

        _doc = LoadOrMigrate(settings.Load().Site);
    }

    public HostFilterState Load(string site)
    {
        if (string.IsNullOrWhiteSpace(site))
            return new HostFilterState();
        return _doc.Sites.TryGetValue(site, out var state) ? state : new HostFilterState();
    }

    public void Save(string site, HostFilterState state)
    {
        if (string.IsNullOrWhiteSpace(site))
        {
            Log.Debug("Filter-Save uebersprungen — leerer Site-Name (Cockpit vermutlich noch nicht konfiguriert).");
            return;
        }

        _doc.Sites[site] = state;

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_doc, JsonOpts));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Host-Filter konnten nicht gespeichert werden: {Path}", _path);
        }
    }

    /// <summary>
    /// Liest die Datei; migriert eine altformatige filter.json ({Filters,ActiveFilterName}
    /// auf top-level) auf das neue Format {Sites: {siteName: {Filters,ActiveFilterName}}}.
    /// </summary>
    private HostFilterDoc LoadOrMigrate(string currentSite)
    {
        if (!File.Exists(_path))
            return new HostFilterDoc();

        try
        {
            var raw = File.ReadAllText(_path);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // Neues Format erkennt man am "Sites"-Objekt.
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Sites", out _))
            {
                return JsonSerializer.Deserialize<HostFilterDoc>(raw) ?? new HostFilterDoc();
            }

            // Altformat: die Datei ist selbst der HostFilterState. Als Filter der aktuellen
            // Site interpretieren und beim naechsten Save ins neue Format ueberfuehren.
            var legacy = JsonSerializer.Deserialize<HostFilterState>(raw) ?? new HostFilterState();
            var migrated = new HostFilterDoc();
            if (!string.IsNullOrWhiteSpace(currentSite))
            {
                migrated.Sites[currentSite] = legacy;
                Log.Info("Host-Filter-Migration: altes Format auf Site '{Site}' abgelegt.", currentSite);
            }
            else
            {
                Log.Warn("Host-Filter-Migration: keine aktive Site — alte Filter bleiben ungenutzt bis zum ersten Site-Save.");
            }
            return migrated;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Host-Filter konnten nicht geladen werden — nutze leere Liste.");
            return new HostFilterDoc();
        }
    }
}
