using System.Text.Json;
using NLog;

namespace Checkmk.Data;

/// <summary>
/// Lokale Kopie der zuletzt erfolgreich gelesenen globalen Einstellungen.
///
/// Eigene Klasse, damit sich der Ausfallweg ohne Datenbank testen laesst — er
/// ist der Grund, warum das Verlagern auf FOC-SQL01 die Verfuegbarkeit nicht
/// verschlechtert, und genau solche Pfade werden sonst nie geprueft, weil man
/// zum Testen einen Serverausfall braucht.
///
/// Lesen und Schreiben schlagen nie durch: Ein nicht schreibbarer Cache ist
/// aergerlich, aber kein Grund, einen frisch gelesenen Stand zu verwerfen.
/// </summary>
public sealed class GlobalSettingsCache(string path)
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Path => path;

    public void Write(IReadOnlyDictionary<string, string?> rows)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(rows, Json));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Ausfall-Cache konnte nicht geschrieben werden: {Path}", path);
        }
    }

    /// <summary>Gelesene Zeilen oder <c>null</c>, wenn es keinen brauchbaren
    /// Cache gibt (fehlt, leer oder kaputt).</summary>
    public Dictionary<string, string?>? Read()
    {
        try
        {
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return null;
            var rows = JsonSerializer.Deserialize<Dictionary<string, string?>>(text);
            return rows is { Count: > 0 } ? rows : null;
        }
        catch (Exception ex)
        {
            // Halb geschriebene Datei nach einem Absturz: lieber Vorgaben als
            // ein Stand, dem wir nicht trauen koennen.
            Log.Debug(ex, "Ausfall-Cache nicht lesbar: {Path}", path);
            return null;
        }
    }
}
