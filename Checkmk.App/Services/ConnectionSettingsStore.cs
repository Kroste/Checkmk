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
}

/// <summary>
/// Speichert die Verbindungskonfiguration plattformkonform:
/// Windows unter <c>%APPDATA%</c>, Linux/macOS unter <c>$XDG_CONFIG_HOME</c>
/// (bzw. <c>~/.config</c>). Das Secret wird ueber <see cref="ISecretProtector"/>
/// plattformspezifisch verschluesselt (Windows: DPAPI-CurrentUser,
/// Linux: AES-GCM mit machine-id/UID-abgeleitetem Schluessel).
/// </summary>
public sealed class ConnectionSettingsStore : IConnectionSettingsStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly ISecretProtector _protector;
    private readonly string _path;

    public ConnectionSettingsStore(ISecretProtector protector)
    {
        _protector = protector;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kroste", "Checkmk");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
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
}
