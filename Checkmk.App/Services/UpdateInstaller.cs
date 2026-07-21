using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Runtime.Versioning;
using NLog;

namespace Checkmk.App.Services;

/// <summary>
/// Self-Update nach Kroste-Standard (Referenz: Klemmbrett-Scaffold).
/// Windows-only (Cockpit ist WinExe): ZIP herunterladen, daneben entpacken,
/// eine .bat schreiben, die auf das App-Ende wartet, die Dateien ersetzt und
/// die neue Version startet. Die alte .exe kann sich unter Windows nicht selbst
/// ueberschreiben — deshalb der externe Prozess.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UpdateInstaller
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly HttpClient _http;

    public UpdateInstaller()
    {
        // Gleiche Proxy-Auth-Konfiguration wie im UpdateChecker — sonst 407 am
        // FortiProxy beim Download.
        var handler = new HttpClientHandler
        {
            UseProxy = true,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Checkmk-Cockpit-Update");
    }

    /// <summary>Laedt das ZIP herunter, entpackt neben die App und startet den
    /// Austausch-Prozess. Bei Erfolg kehrt die Methode zurueck — der Aufrufer
    /// muss danach die App beenden (Environment.Exit), damit die .bat die
    /// Dateien ueberschreiben kann.</summary>
    public async Task<bool> DownloadAndApplyAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(update.WindowsZipUrl))
        {
            Log.Warn("Kein Windows-ZIP im Release — Self-Update nicht moeglich.");
            return false;
        }

        try
        {
            var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var work = Path.Combine(Path.GetTempPath(), "Checkmk-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);

            var assetName = Path.GetFileName(new Uri(update.WindowsZipUrl).LocalPath);
            var zipPath = Path.Combine(work, assetName);

            Log.Info("Lade Update-ZIP: {Url}", update.WindowsZipUrl);
            await DownloadWithProgressAsync(update.WindowsZipUrl, zipPath, progress, ct);

            var extract = Path.Combine(work, "extracted");
            ZipFile.ExtractToDirectory(zipPath, extract);
            Log.Debug("Update entpackt nach {Path}", extract);

            return LaunchWindowsSwap(extract, work, appDir);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Self-Update fehlgeschlagen.");
            return false;
        }
    }

    private async Task DownloadWithProgressAsync(
        string url, string dest, IProgress<double>? progress, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1L;

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress?.Report((double)read / total);
        }
        Log.Debug("Download fertig: {Bytes} Bytes", read);
    }

    /// <summary>
    /// Schreibt die Austausch-Batch und startet sie. WICHTIG (aus Klemmbrett-
    /// Scaffold-Referenz gelernt): Zeilen OHNE Einrueckung, sonst ist ein
    /// eingeruecktes ":label" fuer cmd.exe kein gueltiges Sprungziel und die
    /// Kopie laeuft los, waehrend die alte App die Dateien noch sperrt (=> alte
    /// Version startet neu). Warten mit powershell Wait-Process ist
    /// zuverlaessiger als eine tasklist-Polling-Schleife.
    /// </summary>
    private static bool LaunchWindowsSwap(string extract, string work, string appDir)
    {
        var pid = Environment.ProcessId;
        var exe = Path.Combine(appDir, "Checkmk.App.exe");
        var bat = Path.Combine(work, "apply.bat");
        var log = Path.Combine(work, "update.log");

        var lines = new[]
        {
            "@echo off",
            $"echo Warte auf Prozess {pid} >\"{log}\"",
            $"powershell -NoProfile -Command \"try {{ Wait-Process -Id {pid} -ErrorAction Stop }} catch {{}}\" >>\"{log}\" 2>&1",
            // Kurzer Nachlauf, damit Datei-Handles sicher freigegeben sind.
            "ping 127.0.0.1 -n 2 >NUL",
            $"echo Kopiere Dateien >>\"{log}\"",
            $"xcopy /E /Y /I /Q \"{extract}\\*\" \"{appDir}\\\" >>\"{log}\" 2>&1",
            $"echo Starte neu >>\"{log}\"",
            $"start \"\" \"{exe}\""
            // work-Ordner NICHT loeschen — enthaelt das update.log fuer die
            // Fehlersuche, falls was schief geht.
        };
        File.WriteAllLines(bat, lines);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{bat}\"\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = work
        });
        Log.Info("Windows-Update vorbereitet ({Bat}) — App wird fuer den Austausch beendet.", bat);
        return true;
    }
}
