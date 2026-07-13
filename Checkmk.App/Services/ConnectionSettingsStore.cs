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
    public string Username { get; set; } = Environment.UserName;
    public bool UseHttps { get; set; } = true;
    public bool IgnoreCertificateErrors { get; set; }

    /// <summary>
    /// Windows-/LDAP-Anmeldung (Basic-Auth, empfohlen) vs. klassischer Automation-User
    /// (Bearer). Default ist Bearer aus Backward-Compat mit bestehenden Installs
    /// (deren settings.json das Feld nicht kennt). Bei User-Anmeldung zeigt der
    /// Checkmk-Audit-Log den echten Namen bei Ack/Downtime.
    /// </summary>
    public CheckmkAuthMode AuthMode { get; set; } = CheckmkAuthMode.AutomationBearer;

    /// <summary>
    /// Zusätzliche Sites am selben Checkmk-Server (Host/User/Secret identisch,
    /// nur die Site wechselt). Enthält typischerweise auch die aktuelle
    /// <see cref="Site"/> — wenn nicht, wird sie beim Laden ergänzt. Leer =
    /// kein Umschalter im UI.
    /// </summary>
    public List<string> KnownSites { get; set; } = [];

    /// <summary>Plattformspezifisch verschluesseltes Secret (Base64). Nie im Klartext im JSON.</summary>
    public string? ProtectedSecret { get; set; }

    /// <summary>Share, in dem der aktuelle Checkmk-Agent-Installer (MSI) liegt.</summary>
    public string AgentShare { get; set; } = @"\\samba01\542$\5424_IT-Basis-Dienste\CheckMK";

    /// <summary>
    /// Editierbare PowerShell-Skript-Vorlage, die auf dem Zielhost ausgefuehrt wird.
    /// Platzhalter: {host} = Hostname, {installer} = lokaler Pfad des kopierten Installers.
    /// Enthaelt auch den Register-Befehl inkl. Passwort (Klartext, bewusst).
    /// </summary>
    public string AgentUpdateScript { get; set; } = DefaultAgentUpdateScript;

    public const string DefaultAgentUpdateScript =
        "# Laeuft auf dem Zielhost. {host}=Hostname, {installer}=lokaler MSI-Pfad.\n" +
        "$ErrorActionPreference = 'Stop'\n" +
        "\n" +
        "# 1) Vorhandenen Checkmk-Agent deinstallieren (falls vorhanden)\n" +
        "#    msiexec meldet Fehler nur via ExitCode — Start-Process -Wait ohne -PassThru\n" +
        "#    verschluckt sie. Deshalb -PassThru und ExitCode explizit pruefen.\n" +
        "$keys = 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*'," +
        "'HKLM:\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*'\n" +
        "Get-ItemProperty $keys -ErrorAction SilentlyContinue |\n" +
        "  Where-Object { $_.DisplayName -like 'Checkmk Agent*' } |\n" +
        "  ForEach-Object {\n" +
        "    Write-Output \"Deinstalliere $($_.DisplayName)\"\n" +
        "    $p = Start-Process msiexec.exe -ArgumentList \"/x $($_.PSChildName) /qn /norestart\" -Wait -PassThru\n" +
        "    if ($p.ExitCode -ne 0) { throw \"msiexec /x fehlgeschlagen (ExitCode $($p.ExitCode))\" }\n" +
        "  }\n" +
        "\n" +
        "# 2) Aktuellen Client installieren (wurde nach {installer} kopiert)\n" +
        "Write-Output 'Installiere neuen Agent'\n" +
        "$p = Start-Process msiexec.exe -ArgumentList \"/i `\"{installer}`\" /qn /norestart\" -Wait -PassThru\n" +
        "if ($p.ExitCode -ne 0) { throw \"msiexec /i fehlgeschlagen (ExitCode $($p.ExitCode))\" }\n" +
        "\n" +
        "# 3) Registrieren (--trust-cert: Server-Zertifikat ohne interaktive Rueckfrage vertrauen)\n" +
        "Write-Output 'Registriere Agent-Controller'\n" +
        "& \"C:\\Program Files (x86)\\checkmk\\service\\cmk-agent-ctl.exe\" register --trust-cert " +
        "-H {host} -s cmk.lhp.intern -i LHP -U Agent_cmk -P ************\n" +
        "Write-Output 'Fertig.'\n";

    public CheckmkOptions ToOptions(string plainSecret) => new()
    {
        Host = Host,
        Site = Site,
        Username = Username,
        Secret = plainSecret,
        UseHttps = UseHttps,
        IgnoreCertificateErrors = IgnoreCertificateErrors,
        AuthMode = AuthMode
    };
}

public interface IConnectionSettingsStore
{
    ConnectionSettings Load();
    string? LoadSecret(ConnectionSettings settings);
    void Save(ConnectionSettings settings, string plainSecret);
    bool IsConfigured(ConnectionSettings settings);
    string SettingsFilePath { get; }

    /// <summary>Wechselt nur die aktive Site (Site-Umschalter). Secret bleibt unangetastet.</summary>
    void UpdateActiveSite(string newSite);
}

/// <summary>
/// Speichert die Verbindungskonfiguration user-lokal unter
/// <c>%APPDATA%\Kroste\Checkmk\settings.json</c> — verschluesselt mit
/// <see cref="WindowsDpapiProtector"/> (CurrentUser-Scope). Pfad ist per
/// <c>bootstrap.json</c> ueberschreibbar. Frueher zentral auf dem Samba-
/// Share; zurueckverlegt weil die Verbindungsdaten pro Nutzer gehoeren.
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

    public void UpdateActiveSite(string newSite)
    {
        if (string.IsNullOrWhiteSpace(newSite)) return;
        var settings = Load();
        if (string.Equals(settings.Site, newSite, StringComparison.Ordinal)) return;

        settings.Site = newSite;
        // ProtectedSecret bleibt drin — wir serialisieren das Settings-Objekt direkt,
        // *ohne* Save() zu benutzen (das erwartet plainSecret und wuerde die Verschluesselung
        // rotieren).
        var json = JsonSerializer.Serialize(settings,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
        Log.Info("Site auf '{Site}' umgeschaltet.", newSite);
    }

    private static string ResolvePath() => Bootstrap.LoadOrCreate().SharedSettingsPath;
}

/// <summary>
/// Bootstrap-Datei im Userspace — enthaelt nur den Pfad zur zentralen Verbindungsdatei auf dem
/// Windows-Fileshare. Wird beim ersten Start mit dem Default belegt und kann von Hand editiert
/// werden, falls sich der Fileserver-Pfad aendert. Bewusst kein UI dafuer: der Default ist die
/// Konvention, Abweichungen sind Sonderfall.
/// </summary>
internal sealed class Bootstrap
{
    private static readonly string DefaultLocalSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Kroste", "Checkmk", "settings.json");

    // Alter Default aus v1.0-v1.4 — wenn der noch in bootstrap.json steht, migrieren
    // wir bei LoadOrCreate() automatisch auf den neuen lokalen Default.
    private const string LegacySambaSettingsPath =
        @"\\Samba01\542$\5424_IT-Basis-Dienste\_Oste\CheckMK\settings.json";

    private const string DefaultSharedHostsPath = @"\\Samba01\542$\5424_IT-Basis-Dienste\_Oste\CheckMK\hosts.json";
    private const string DefaultUpdateChannelUrl =
        "https://api.github.com/repos/Kroste/Checkmk/releases/latest";
    private const string DefaultDomain = "lhp.intern";

    /// <summary>Pfad zur Verbindungsdatei. Default ist user-lokal (%APPDATA%).</summary>
    public string SharedSettingsPath { get; set; } = DefaultLocalSettingsPath;

    /// <summary>Zentrale, unverschluesselte Host-Metadaten-Datei (Domain je Host, spaeter
    /// evtl. weitere Notizen). Alle Cockpit-Nutzer teilen dieselbe Zuordnung.</summary>
    public string SharedHostsPath { get; set; } = DefaultSharedHostsPath;

    /// <summary>Default-Domain fuer Hosts ohne explizite Zuordnung. Wird an den
    /// Hostnamen angehaengt, wenn Ping/RDP/SSH einen FQDN brauchen.</summary>
    public string HostDefaultDomain { get; set; } = DefaultDomain;

    public string UpdateChannelUrl { get; set; } = DefaultUpdateChannelUrl;

    /// <summary>
    /// Interne Attribut-Keys, unter denen die OS-Familie im Host-Config-Dict
    /// gesucht wird (Custom Host Attribute oder Host-Tag). Erster Treffer gewinnt.
    /// Wenn dein Attribut anders heisst, hier den Key ergaenzen — die App logged
    /// bei jedem Refresh die tatsaechlich gesehenen Keys unter Debug.
    /// </summary>
    public List<string> HostOsAttributeKeys { get; set; } =
    [
        "tag_operation_system",
        "operation_system",
        "operating_system",
        "os_family"
    ];

    /// <summary>
    /// Blendet das „Host anlegen"-Formular im Konfig-Tab ein. Default false —
    /// bewusst versteckt, weil Setup-Handgriffe im Fachbereich zentral erfolgen und
    /// eine Fehlbedienung Config-Aenderungen produziert. Bei Bedarf per JSON auf true
    /// setzen (kein UI-Schalter).
    /// </summary>
    public bool ShowHostCreation { get; set; }

    // App-Konfiguration wird zentral geteilt — im Idealfall ein Wert pro Feld,
    // alle Cockpit-User profitieren. User-Secrets (settings.json, ssh-creds)
    // liegen weiterhin pro Nutzer.
    private const string CentralBootstrapPath =
        @"\\Samba01\542$\5424_IT-Basis-Dienste\_Oste\CheckMK\bootstrap.json";

    private static string LocalBootstrapPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Kroste", "Checkmk", "bootstrap.json");

    public static Bootstrap LoadOrCreate()
    {
        // 1) Zentraler Pfad hat Vorrang.
        if (TryLoad(CentralBootstrapPath, out var central))
        {
            NormalizeAndPatchInPlace(central, CentralBootstrapPath);
            return central;
        }

        // 2) Sonst versuchen wir den lokalen Legacy-Pfad — und migrieren einmalig
        //    nach zentral, damit alle User denselben Konfigstand haben.
        if (TryLoad(LocalBootstrapPath, out var local))
        {
            NormalizeAndPatchInPlace(local, LocalBootstrapPath);
            TryMigrateToCentral(local);
            return local;
        }

        // 3) Nichts vorhanden -> Default schreiben (bevorzugt zentral, Fallback lokal).
        var b = new Bootstrap();
        if (!TryWrite(CentralBootstrapPath, b))
            TryWrite(LocalBootstrapPath, b);
        return b;
    }

    private static bool TryLoad(string path, out Bootstrap result)
    {
        result = null!;
        try
        {
            if (!File.Exists(path)) return false;
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<Bootstrap>(json);
            if (loaded is null || string.IsNullOrWhiteSpace(loaded.SharedSettingsPath))
                return false;
            result = loaded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void NormalizeAndPatchInPlace(Bootstrap loaded, string sourcePath)
    {
        var dirty = false;

        // Wer aus v1.0-v1.4 upgradet hat noch den Samba-Pfad in SharedSettingsPath drin.
        // Anmeldedaten gehoeren pro Nutzer — Default zurueck auf lokal.
        if (string.Equals(loaded.SharedSettingsPath,
                LegacySambaSettingsPath, StringComparison.OrdinalIgnoreCase))
        {
            loaded.SharedSettingsPath = DefaultLocalSettingsPath;
            dirty = true;
        }

        // Neue Properties nachziehen, falls die Datei aus einer aelteren Version stammt
        // (JSON hatte das Feld nicht -> Deserializer liess es null/leer).
        if (loaded.HostOsAttributeKeys is null || loaded.HostOsAttributeKeys.Count == 0)
        {
            loaded.HostOsAttributeKeys = new Bootstrap().HostOsAttributeKeys;
            dirty = true;
        }

        if (dirty)
        {
            try
            {
                File.WriteAllText(sourcePath, JsonSerializer.Serialize(loaded,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* Best-effort */ }
        }
    }

    private static void TryMigrateToCentral(Bootstrap b)
    {
        try
        {
            var dir = Path.GetDirectoryName(CentralBootstrapPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            // Nur schreiben wenn zentral wirklich noch nicht existiert — sonst haben
            // wir vielleicht gerade eine neuere zentrale Version ueberholt.
            if (!File.Exists(CentralBootstrapPath))
            {
                File.WriteAllText(CentralBootstrapPath, JsonSerializer.Serialize(b,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch { /* Migration ist best-effort; lokale Datei bleibt Fallback */ }
    }

    private static bool TryWrite(string path, Bootstrap b)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(b,
                new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
