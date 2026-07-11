using System.Diagnostics;
using System.Runtime.Versioning;
using NLog;

namespace Checkmk.App.Services;

/// <summary>
/// Startet die Windows-Standard-Remote-Tools zum markierten Host — jeweils gegen
/// den <b>FQDN</b>, weil DMZ-Hosts nur unter <c>host.dmz.lhp.intern</c>
/// erreichbar sind. Die Domain kommt aus <see cref="HostContext"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RemoteTools
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly HostContext _context;
    private readonly ISshCredentialStore _sshCreds;

    public RemoteTools(HostContext context, ISshCredentialStore sshCreds)
    {
        _context = context;
        _sshCreds = sshCreds;
    }

    public void StartRdp(string host)
    {
        var fqdn = _context.FqdnFor(host);
        if (string.IsNullOrWhiteSpace(fqdn)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "mstsc.exe",
                Arguments = $"/v:{fqdn}",
                UseShellExecute = true
            });
            Log.Info("RDP-Verbindung geoeffnet: {Fqdn}", fqdn);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "mstsc konnte nicht gestartet werden (Host={Host}).", fqdn);
        }
    }

    public void StartPing(string host)
    {
        var fqdn = _context.FqdnFor(host);
        if (string.IsNullOrWhiteSpace(fqdn)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k ping -t {fqdn}",
                UseShellExecute = true
            });
            Log.Info("Ping-Fenster geoeffnet: {Fqdn}", fqdn);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "ping konnte nicht gestartet werden (Host={Host}).", fqdn);
        }
    }

    /// <summary>
    /// Oeffnet eine SSH-Session per Standard-Windows-OpenSSH-Client.
    /// User-Quelle (Reihenfolge):
    /// <list type="number">
    ///   <item>Expliziter <paramref name="userOverride"/>-Parameter</item>
    ///   <item>SshCredentialStore-Eintrag zum Host</item>
    ///   <item>Kein User -> SSH nimmt den Windows-Anmeldenamen</item>
    /// </list>
    /// Wenn im Store ein Passwort hinterlegt ist, wird es fuer 30 Sekunden in
    /// die Zwischenablage kopiert — der SSH-Client fragt im Prompt danach,
    /// Rechtsklick in cmd fuegt ein. OpenSSH nimmt keine Passwoerter per CLI-
    /// Argument, sonst haetten wir es direkt uebergeben.
    /// </summary>
    public void StartSsh(string host, string? userOverride)
    {
        var fqdn = _context.FqdnFor(host);
        if (string.IsNullOrWhiteSpace(fqdn)) return;

        var stored = _sshCreds.Get(host);
        var user = !string.IsNullOrWhiteSpace(userOverride) ? userOverride
                 : !string.IsNullOrWhiteSpace(stored?.User) ? stored.User
                 : null;
        var target = string.IsNullOrWhiteSpace(user) ? fqdn : $"{user}@{fqdn}";

        // Passwort in die Zwischenablage — nur wenn wir eins haben und der
        // Store es entschluesseln konnte.
        var pw = stored is not null && _sshCreds is SshCredentialStore concrete
            ? concrete.DecryptPassword(stored)
            : null;
        if (!string.IsNullOrEmpty(pw))
            _ = ClipboardTransport.PutTemporary(pw, TimeSpan.FromSeconds(30));

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k ssh {target}",
                UseShellExecute = true
            });
            Log.Info("SSH-Session geoeffnet: {Target}{Hint}", target,
                string.IsNullOrEmpty(pw) ? "" : " (Passwort 30 s in Zwischenablage)");
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "ssh konnte nicht gestartet werden (Target={Target}).", target);
        }
    }

    /// <summary>Waehlt anhand der OS-Familie: Windows -> RDP, Linux/Unbekannt -> SSH.
    /// Fuer Unbekannt: der User wollte explizit SSH als Fallback (bei einer nicht
    /// ueberwachten Netzwerk-Appliance ist SSH die wahrscheinlichere Wahl).</summary>
    public void StartRemoteShell(string host, OsFamily os, string? sshUser)
    {
        if (os == OsFamily.Windows)
            StartRdp(host);
        else
            StartSsh(host, sshUser);
    }
}
