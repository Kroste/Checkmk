using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;

namespace Checkmk.Data;

/// <summary>
/// Woher der Verbindungsstring kommt. Drei Quellen, in dieser Reihenfolge:
///
/// <list type="number">
/// <item><c>%APPDATA%\Kroste\Checkmk\db-dev.json</c> — Entwicklung. Liegt
/// außerhalb des Repos, kann also nicht versehentlich mitcommittet werden, und
/// überstimmt die ausgelieferte Datei auf dem eigenen Rechner.</item>
/// <item><c>database.json</c> <b>neben der Exe</b> — der Ausrollweg. Wird mit
/// der Anwendung verteilt, der Wert darin ist verschleiert
/// (<see cref="ConnectionStringObfuscator"/>).</item>
/// <item><c>bootstrap.json</c> — Notnagel, falls die zentrale Datei den Wert
/// doch tragen soll.</item>
/// </list>
///
/// Zur Ehrlichkeit: Der ausgelieferte String ist <b>verschleiert, nicht
/// geschützt</b> — der Schlüssel steckt im Binary daneben. Die wirksame Grenze
/// ist das Datenbankrecht des Laufzeitkontos (datareader/datawriter, kein
/// db_owner), siehe db/README.md.
/// </summary>
public static class DatabaseConnection
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Dateiname der ausgelieferten Verbindungsdatei neben der Exe.</summary>
    public const string FileName = "database.json";

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string DevConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Kroste", "Checkmk", "db-dev.json");

    /// <summary>Neben der Exe — dasselbe Muster wie <c>viewer.json</c>.</summary>
    public static string DeployedConfigPath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Kroste", "Checkmk", "globals-cache.json");

    /// <summary>
    /// Inhalt von <c>database.json</c> bzw. <c>db-dev.json</c>. Beide Felder
    /// werden gelesen: <see cref="ConnectionString"/> für von Hand geschriebene
    /// Testdateien, <see cref="ProtectedConnectionString"/> für den
    /// ausgelieferten, verschleierten Wert.
    /// </summary>
    public sealed class DatabaseConfig
    {
        public string? ConnectionString { get; set; }

        public string? ProtectedConnectionString { get; set; }

        /// <summary>Klartext, egal aus welchem der beiden Felder er stammt.</summary>
        public string? Resolve()
        {
            if (!string.IsNullOrWhiteSpace(ProtectedConnectionString))
                return ConnectionStringObfuscator.Deobfuscate(ProtectedConnectionString);
            return string.IsNullOrWhiteSpace(ConnectionString) ? null : ConnectionString;
        }
    }

    /// <summary>
    /// Liefert den Verbindungsstring oder <c>null</c>, wenn keiner konfiguriert
    /// ist. <c>null</c> ist ein gültiger Zustand: Ohne Datenbank läuft das
    /// Cockpit mit Cache bzw. Vorgaben weiter.
    /// </summary>
    public static string? Resolve(string? fromBootstrap = null)
    {
        if (TryRead(DevConfigPath) is { } dev)
        {
            Log.Info("Datenbank-Verbindung aus {Path}.", DevConfigPath);
            return dev;
        }

        if (TryRead(DeployedConfigPath) is { } deployed)
        {
            Log.Info("Datenbank-Verbindung aus {Path}.", DeployedConfigPath);
            return deployed;
        }

        if (!string.IsNullOrWhiteSpace(fromBootstrap))
        {
            Log.Info("Datenbank-Verbindung aus bootstrap.json.");
            return ConnectionStringObfuscator.Deobfuscate(fromBootstrap);
        }

        Log.Info("Keine Datenbank-Verbindung konfiguriert — lokaler Betrieb.");
        return null;
    }

    private static string? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<DatabaseConfig>(File.ReadAllText(path), Opts)?.Resolve();
        }
        catch (Exception ex)
        {
            // Eine kaputte Datei darf den Start nicht verhindern — das Cockpit
            // läuft ohne Datenbank weiter, und die Statusleiste sagt es.
            Log.Warn(ex, "{File} nicht lesbar: {Path}", Path.GetFileName(path), path);
            return null;
        }
    }

    /// <summary>
    /// Schreibt eine <c>database.json</c> mit verschleiertem Wert. Wird vom
    /// Schalter <c>--protect-db</c> benutzt; von Hand lässt sich der Wert sonst
    /// nicht erzeugen.
    /// </summary>
    public static void WriteDeployedConfig(string path, string plainConnectionString)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var config = new DatabaseConfig
        {
            ProtectedConnectionString = ConnectionStringObfuscator.Obfuscate(plainConnectionString)
        };
        File.WriteAllText(path, JsonSerializer.Serialize(config, Opts));
    }
}
