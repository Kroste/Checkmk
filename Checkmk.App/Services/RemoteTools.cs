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

    public RemoteTools(HostContext context) => _context = context;

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

    /// <summary>Oeffnet eine SSH-Session per Standard-Windows-OpenSSH-Client.
    /// Wenn <paramref name="user"/> leer ist, waehlt SSH den lokalen Windows-User
    /// als Login-Namen (SSH-Standardverhalten).</summary>
    public void StartSsh(string host, string? user)
    {
        var fqdn = _context.FqdnFor(host);
        if (string.IsNullOrWhiteSpace(fqdn)) return;
        var target = string.IsNullOrWhiteSpace(user) ? fqdn : $"{user}@{fqdn}";
        try
        {
            // cmd /k statt /c: Fenster bleibt offen, User kann per Rechtsklick
            // das Passwort aus dem Clipboard einfuegen (siehe RemoteTools+Ssh
            // in Commit C).
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k ssh {target}",
                UseShellExecute = true
            });
            Log.Info("SSH-Session geoeffnet: {Target}", target);
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
