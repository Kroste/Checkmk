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
}
