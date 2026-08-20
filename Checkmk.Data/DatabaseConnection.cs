using System.Text.Json;
using NLog;

namespace Checkmk.Data;

/// <summary>
/// Woher der Verbindungsstring kommt. Zwei Quellen, in dieser Reihenfolge:
///
/// <list type="number">
/// <item><c>%APPDATA%\Kroste\Checkmk\db-dev.json</c> — Entwicklung. Liegt
/// ausserhalb des Repos, kann also nicht versehentlich mitcommittet werden.</item>
/// <item>Der beim Ausrollen mitgelieferte Wert aus <c>bootstrap.json</c>.</item>
/// </list>
///
/// Zur Ehrlichkeit: Der ausgelieferte String ist bestenfalls <b>verschleiert</b>,
/// nicht geschuetzt — er liegt auf ~50 Arbeitsplaetzen, und wer den Schluessel im
/// Binary sucht, findet ihn. Die wirksame Grenze ist das Datenbankrecht: Das
/// Laufzeitkonto darf Zeilen lesen und schreiben, sonst nichts. Deshalb steht in
/// db/README.md das Zwei-Konten-Modell, und deshalb ist es keine Foermlichkeit.
/// </summary>
public static class DatabaseConnection
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public static string DevConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Kroste", "Checkmk", "db-dev.json");

    public static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Kroste", "Checkmk", "globals-cache.json");

    private sealed class DevConfig
    {
        public string? ConnectionString { get; set; }
    }

    /// <summary>
    /// Liefert den Verbindungsstring oder <c>null</c>, wenn keiner konfiguriert
    /// ist. <c>null</c> ist ein gueltiger Zustand: Ohne Datenbank laeuft das
    /// Cockpit mit Cache bzw. Vorgaben weiter.
    /// </summary>
    public static string? Resolve(string? fromBootstrap = null)
    {
        if (TryReadDevConfig() is { } dev)
        {
            Log.Info("Datenbank-Verbindung aus {Path}.", DevConfigPath);
            return dev;
        }

        if (!string.IsNullOrWhiteSpace(fromBootstrap))
            return fromBootstrap;

        Log.Info("Keine Datenbank-Verbindung konfiguriert — lokaler Betrieb.");
        return null;
    }

    private static string? TryReadDevConfig()
    {
        try
        {
            if (!File.Exists(DevConfigPath)) return null;
            var cfg = JsonSerializer.Deserialize<DevConfig>(File.ReadAllText(DevConfigPath));
            return string.IsNullOrWhiteSpace(cfg?.ConnectionString) ? null : cfg.ConnectionString;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "db-dev.json nicht lesbar: {Path}", DevConfigPath);
            return null;
        }
    }
}
