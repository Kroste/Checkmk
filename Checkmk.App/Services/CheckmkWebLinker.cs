using System.Diagnostics;
using NLog;

namespace Checkmk.App.Services;

/// <summary>
/// Baut Deep-Links ins Checkmk-Webinterface — Host- oder Service-Ansicht — aus
/// den aktuell aktiven Verbindungsdaten. Oeffnet die URL im Standardbrowser.
/// </summary>
public sealed class CheckmkWebLinker
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IConnectionSettingsStore _store;

    public CheckmkWebLinker(IConnectionSettingsStore store) => _store = store;

    public void OpenHostView(string hostName)
        => Open($"view.py?view_name=host&host={Uri.EscapeDataString(hostName)}");

    public void OpenServiceView(string hostName, string serviceDescription)
        => Open($"view.py?view_name=service"
              + $"&host={Uri.EscapeDataString(hostName)}"
              + $"&service={Uri.EscapeDataString(serviceDescription)}");

    private void Open(string pathAndQuery)
    {
        var s = _store.Load();
        if (string.IsNullOrWhiteSpace(s.Host) || string.IsNullOrWhiteSpace(s.Site))
        {
            Log.Warn("Kein Ziel fuer Checkmk-Web-Deep-Link — Verbindung nicht konfiguriert.");
            return;
        }

        var scheme = s.UseHttps ? "https" : "http";
        var url = $"{scheme}://{s.Host}/{s.Site}/check_mk/{pathAndQuery}";
        Log.Info("Oeffne Checkmk-Web: {Url}", url);
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte URL nicht oeffnen: {Url}", url);
        }
    }
}
