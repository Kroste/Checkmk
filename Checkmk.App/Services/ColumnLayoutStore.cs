using System.Text.Json;
using NLog;

namespace Checkmk.App.Services;

/// <summary>Eine Spalte im gespeicherten Layout. Reihenfolge = Position in der Liste.</summary>
public sealed class ColumnSetting
{
    public string Key { get; set; } = "";

    public bool Visible { get; set; } = true;

    /// <summary>Vom Anwender gezogene Breite in Pixeln. <c>null</c> = Vorgabe der
    /// Factory behalten (u. a. die Stern-Breite der Ausgabe-Spalte, die den Restplatz
    /// einnimmt — die darf nicht in eine feste Pixelzahl umkippen, nur weil wir
    /// speichern).</summary>
    public double? Width { get; set; }
}

/// <summary>Gespeicherte Spaltenanordnung einer Tabelle.</summary>
public sealed class ColumnLayout
{
    public List<ColumnSetting> Columns { get; set; } = [];

    /// <summary>JsonIgnore, sonst landet das abgeleitete Flag als Feld in der Datei
    /// und suggeriert beim Reinschauen, man koenne es setzen.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Columns.Count == 0;
}

public interface IColumnLayoutStore
{
    /// <summary><paramref name="viewId"/> trennt mehrere Tabellen (aktuell nur "status").</summary>
    ColumnLayout Load(string viewId);
    void Save(string viewId, ColumnLayout layout);
    void Reset(string viewId);
    string FilePath { get; }
}

/// <summary>
/// Persistiert die Spaltenanordnung unter
/// <c>%APPDATA%\Kroste\Checkmk\columns.json</c>. User-lokal und unverschluesselt —
/// reine Ansichtseinstellung, wie <c>statusview.json</c>.
/// </summary>
public sealed class ColumnLayoutStore : IColumnLayoutStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    private readonly string _path;

    public string FilePath => _path;

    /// <param name="path">Nur fuer Tests. Ohne Angabe der user-lokale Standardpfad —
    /// Tests duerfen nicht in die echte Konfiguration des angemeldeten Nutzers schreiben.</param>
    public ColumnLayoutStore(string? path = null)
    {
        _path = path ?? DefaultPath();
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    private static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Kroste", "Checkmk", "columns.json");

    public ColumnLayout Load(string viewId)
    {
        var all = LoadAll();
        return all.TryGetValue(viewId, out var layout) ? layout : new ColumnLayout();
    }

    public void Save(string viewId, ColumnLayout layout)
    {
        try
        {
            var all = LoadAll();
            all[viewId] = layout;
            File.WriteAllText(_path, JsonSerializer.Serialize(all, Opts));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "columns.json konnte nicht gespeichert werden.");
        }
    }

    public void Reset(string viewId)
    {
        try
        {
            var all = LoadAll();
            if (all.Remove(viewId))
                File.WriteAllText(_path, JsonSerializer.Serialize(all, Opts));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "columns.json konnte nicht zurueckgesetzt werden.");
        }
    }

    private Dictionary<string, ColumnLayout> LoadAll()
    {
        if (!File.Exists(_path))
            return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, ColumnLayout>>(
                       File.ReadAllText(_path)) ?? [];
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "columns.json konnte nicht gelesen werden — nutze Vorgabe.");
            return [];
        }
    }
}
