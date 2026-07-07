using System.Text;
using System.Text.Json;
using Checkmk.Core;
using NLog;

namespace Checkmk.App.Services;

/// <summary>Persistierbare Verbindungseinstellungen. Secret wird verschluesselt abgelegt.</summary>
public sealed class ConnectionSettings
{
    public string Host { get; set; } = "";
    public string Site { get; set; } = "";
    public string Username { get; set; } = "automation";
    public bool UseHttps { get; set; } = true;
    public bool IgnoreCertificateErrors { get; set; }

    /// <summary>Plattformspezifisch verschluesseltes Secret (Base64). Nie im Klartext im JSON.</summary>
    public string? ProtectedSecret { get; set; }

    public CheckmkOptions ToOptions(string plainSecret) => new()
    {
        Host = Host,
        Site = Site,
        Username = Username,
        Secret = plainSecret,
        UseHttps = UseHttps,
        IgnoreCertificateErrors = IgnoreCertificateErrors
    };
}

public interface IConnectionSettingsStore
{
    ConnectionSettings Load();
    string? LoadSecret(ConnectionSettings settings);
    void Save(ConnectionSettings settings, string plainSecret);
    bool IsConfigured(ConnectionSettings settings);
    string SettingsFilePath { get; }
}

/// <summary>
/// Speichert die Verbindungskonfiguration:
/// <list type="bullet">
///   <item>Windows: zentral auf einem Fileshare — Default <c>\\Samba01\542$\Checkmk\settings.json</c>,
///     ueberschreibbar via <c>%APPDATA%\Kroste\Checkmk\bootstrap.json</c>. Verschluesselung mit
///     dem <see cref="SharedAesProtector"/>, damit mehrere Windows-Clients dieselbe Datei
///     entschluesseln koennen.</item>
///   <item>Linux/macOS: user-lokal unter <c>~/.config/Kroste/Checkmk/settings.json</c>,
///     Verschluesselung wie in <see cref="SecretProtectorFactory.Create"/> definiert.</item>
/// </list>
/// </summary>
public sealed class ConnectionSettingsStore : IConnectionSettingsStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly ISecretProtector _protector;
    private readonly string _path;

    public string SettingsFilePath => _path;

    public ConnectionSettingsStore(ISecretProtector protector)
    {
        _protector = protector;
        _path = ResolvePath();
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Zielverzeichnis konnte nicht erstellt werden: {Path}", _path);
        }
        Log.Info("Verbindungseinstellungen liegen unter {Path}", _path);
    }

    public ConnectionSettings Load()
    {
        if (!File.Exists(_path))
            return new ConnectionSettings();
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<ConnectionSettings>(json) ?? new ConnectionSettings();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Einstellungen konnten nicht geladen werden — nutze Defaults.");
            return new ConnectionSettings();
        }
    }

    public string? LoadSecret(ConnectionSettings settings)
    {
        if (string.IsNullOrEmpty(settings.ProtectedSecret))
            return null;
        try
        {
            var blob = Convert.FromBase64String(settings.ProtectedSecret);
            return Encoding.UTF8.GetString(_protector.Unprotect(blob));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Secret konnte nicht entschluesselt werden (evtl. anderer User/Rechner).");
            return null;
        }
    }

    public void Save(ConnectionSettings settings, string plainSecret)
    {
        settings.ProtectedSecret = string.IsNullOrEmpty(plainSecret)
            ? null
            : Convert.ToBase64String(_protector.Protect(Encoding.UTF8.GetBytes(plainSecret)));

        var json = JsonSerializer.Serialize(settings,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
        Log.Info("Verbindungseinstellungen gespeichert nach {Path}", _path);
    }

    public bool IsConfigured(ConnectionSettings s)
        => !string.IsNullOrWhiteSpace(s.Host)
           && !string.IsNullOrWhiteSpace(s.Site)
           && !string.IsNullOrWhiteSpace(s.Username)
           && !string.IsNullOrEmpty(s.ProtectedSecret);

    private static string ResolvePath()
    {
        if (OperatingSystem.IsWindows())
            return Bootstrap.LoadOrCreate().SharedSettingsPath;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kroste", "Checkmk", "settings.json");
    }
}

/// <summary>
/// Bootstrap-Datei im Userspace — enthaelt nur den Pfad zur zentralen Verbindungsdatei auf dem
/// Windows-Fileshare. Wird beim ersten Start mit dem Default belegt und kann von Hand editiert
/// werden, falls sich der Fileserver-Pfad aendert. Bewusst kein UI dafuer: der Default ist die
/// Konvention, Abweichungen sind Sonderfall.
/// </summary>
internal sealed class Bootstrap
{
    private const string DefaultWindowsSharedPath = @"\\Samba01\542$\Checkmk\settings.json";
    private const string DefaultUpdateChannelUrl =
        "https://api.github.com/repos/Kroste/Checkmk/releases/latest";

    public string SharedSettingsPath { get; set; } = DefaultWindowsSharedPath;
    public string UpdateChannelUrl { get; set; } = DefaultUpdateChannelUrl;

    private static string BootstrapFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Kroste", "Checkmk", "bootstrap.json");

    public static Bootstrap LoadOrCreate()
    {
        var path = BootstrapFile;
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<Bootstrap>(json);
                if (loaded != null && !string.IsNullOrWhiteSpace(loaded.SharedSettingsPath))
                    return loaded;
            }
            catch
            {
                // fall through to default
            }
        }

        var b = new Bootstrap();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,
                JsonSerializer.Serialize(b, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Wenn wir das Bootstrap-File nicht schreiben koennen, ist das nicht kritisch —
            // der Default gilt trotzdem.
        }
        return b;
    }
}
