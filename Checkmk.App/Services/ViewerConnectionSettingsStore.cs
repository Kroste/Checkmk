using NLog;

namespace Checkmk.App.Services;

/// <summary>
/// Verbindungsquelle im Viewer-Modus: liefert alles aus <c>viewer.json</c> neben der
/// Exe statt aus <c>%APPDATA%\Kroste\Checkmk\settings.json</c>. Alle schreibenden
/// Operationen sind No-Ops — die Datei wird vom Admin verteilt, nicht vom Anwender
/// gepflegt, und ein versehentliches Ueberschreiben wuerde die Verteilung zerstoeren.
/// </summary>
public sealed class ViewerConnectionSettingsStore : IConnectionSettingsStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly ViewerProfile _profile;

    public ViewerConnectionSettingsStore(ViewerProfile profile) => _profile = profile;

    public string SettingsFilePath => _profile.FilePath;

    public ConnectionSettings Load()
    {
        var c = _profile.Connection;
        return new ConnectionSettings
        {
            Host = c.Host,
            Site = c.Site,
            Username = c.Username,
            UseHttps = c.UseHttps,
            IgnoreCertificateErrors = c.IgnoreCertificateErrors,
            AuthMode = c.AuthMode,
            // Genau eine Site — der Umschalter in der Titelleiste blendet sich damit
            // von selbst aus (er zeigt erst ab zwei Eintraegen).
            KnownSites = string.IsNullOrWhiteSpace(c.Site) ? [] : [c.Site],
            // Kein ProtectedSecret: das Secret steht im Klartext im Profil und kommt
            // ueber LoadSecret. IsConfigured prueft deshalb hier nicht auf das Feld.
            ProtectedSecret = null
        };
    }

    /// <summary>Aufgeloestes Secret aus dem Profil (aus <c>secretBase64</c> dekodiert
    /// bzw. <c>secret</c> im Klartext). Siehe <see cref="ViewerConnection"/> dazu,
    /// warum Base64 hier Maskierung und keine Verschluesselung ist.</summary>
    public string? LoadSecret(ConnectionSettings settings)
        => string.IsNullOrEmpty(_profile.Connection.ResolvedSecret)
            ? null
            : _profile.Connection.ResolvedSecret;

    public void Save(ConnectionSettings settings, string plainSecret)
        => Log.Warn("Speichern der Verbindung im Viewer-Modus ignoriert — "
                  + "die Konfiguration kommt aus {Path}.", _profile.FilePath);

    public bool IsConfigured(ConnectionSettings s)
        => _profile.Connection.IsComplete;

    public void UpdateActiveSite(string newSite)
        => Log.Debug("Site-Wechsel im Viewer-Modus ignoriert (Profil gibt {Site} vor).",
            _profile.Connection.Site);
}
