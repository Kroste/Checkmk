using System.Diagnostics;
using System.Runtime.Versioning;
using NLog;

namespace Checkmk.App.Services;

/// <summary>
/// Startet die Windows-Standard-Remote-Tools zum markierten Host:
/// <c>mstsc /v:host</c> und <c>cmd /k ping -t host</c>. Fuer den Admin-
/// Alltag deutlich schneller als „Host kopieren, Programm starten, einfuegen".
/// </summary>
[SupportedOSPlatform("windows")]
public static class RemoteTools
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public static void StartRdp(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "mstsc.exe",
                Arguments = $"/v:{host}",
                UseShellExecute = true
            });
            Log.Info("RDP-Verbindung geoeffnet: {Host}", host);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "mstsc konnte nicht gestartet werden (Host={Host}).", host);
        }
    }

    public static void StartPing(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return;
        try
        {
            // cmd /k statt /c, damit das Fenster offen bleibt und der Nutzer
            // die Ausgabe lesen kann.
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k ping -t {host}",
                UseShellExecute = true
            });
            Log.Info("Ping-Fenster geoeffnet: {Host}", host);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "ping konnte nicht gestartet werden (Host={Host}).", host);
        }
    }
}
