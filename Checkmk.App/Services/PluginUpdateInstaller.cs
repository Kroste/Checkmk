using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Runtime.Versioning;
using NLog;

namespace Checkmk.App.Services;

/// <summary>
/// Aktualisiert eine oder mehrere Plugin-DLLs. Weil die geladenen Plugin-DLLs
/// unter Windows waehrend der Laufzeit gesperrt sind, laeuft der Austausch
/// ueber ein Batch-Skript (analog Cockpit-Self-Update): App beenden, .bat
/// wartet auf PID-Exit, kopiert neue DLLs in <c>plugins\</c>, startet Cockpit
/// neu.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PluginUpdateInstaller
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly HttpClient _http;

    public PluginUpdateInstaller()
    {
        var handler = new HttpClientHandler
        {
            UseProxy = true,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Checkmk-Cockpit-PluginUpdate");
    }

    /// <summary>Laedt die ZIPs aller uebergebenen Plugin-Updates herunter,
    /// entpackt sie, schreibt EIN Batch-Skript, das alle DLLs auf einmal
    /// kopiert und Cockpit neu startet. Der Aufrufer beendet die App nach
    /// erfolgreicher Rueckkehr.</summary>
    public async Task<bool> DownloadAndApplyAsync(
        IReadOnlyList<PluginUpdateInfo> updates,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var applicable = updates.Where(u => u.UpdateAvailable && !string.IsNullOrEmpty(u.ZipAssetUrl)).ToList();
        if (applicable.Count == 0)
        {
            Log.Info("Keine anwendbaren Plugin-Updates.");
            return false;
        }

        try
        {
            var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var pluginsDir = Path.Combine(appDir, "plugins");
            var work = Path.Combine(Path.GetTempPath(), "Checkmk-plugin-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);

            // Downloads sammeln und entpacken; pro Plugin einen eigenen Extract-Ordner.
            var extractedDlls = new List<string>();
            for (var i = 0; i < applicable.Count; i++)
            {
                var upd = applicable[i];
                var zipPath = Path.Combine(work, upd.ZipAssetName ?? $"{upd.PluginId}.zip");
                await DownloadWithProgressAsync(upd.ZipAssetUrl!, zipPath,
                    p => progress?.Report((i + p) / applicable.Count), ct);

                var extractDir = Path.Combine(work, "extracted-" + i);
                ZipFile.ExtractToDirectory(zipPath, extractDir);

                // Alle CheckmkPlugin.*.dll aus dem ZIP einsammeln (Konvention:
                // ein ZIP = ein Plugin = eine DLL, aber wir sind defensiv).
                foreach (var dll in Directory.GetFiles(extractDir, "CheckmkPlugin.*.dll", SearchOption.AllDirectories))
                    extractedDlls.Add(dll);
            }

            if (extractedDlls.Count == 0)
            {
                Log.Warn("Kein CheckmkPlugin.*.dll in den heruntergeladenen Plugin-ZIPs gefunden.");
                return false;
            }

            return LaunchWindowsSwap(extractedDlls, work, appDir, pluginsDir);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Plugin-Update fehlgeschlagen.");
            return false;
        }
    }

    private async Task DownloadWithProgressAsync(
        string url, string dest, Action<double>? progress, CancellationToken ct)
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
            if (total > 0) progress?.Invoke((double)read / total);
        }
    }

    /// <summary>Batch-Skript-Muster wie beim Cockpit-Self-Update: unerhoehte
    /// Zeilen (sonst kein gueltiges cmd-Sprungziel bei :label), Wait-Process
    /// per PowerShell, dann pro DLL ein <c>copy /Y</c> nach <c>plugins\</c>.
    /// Update-Log bleibt zur Fehlersuche erhalten.</summary>
    private static bool LaunchWindowsSwap(
        IReadOnlyList<string> extractedDlls, string work, string appDir, string pluginsDir)
    {
        var pid = Environment.ProcessId;
        var exe = Path.Combine(appDir, "Checkmk.App.exe");
        var bat = Path.Combine(work, "apply-plugins.bat");
        var log = Path.Combine(work, "plugin-update.log");

        var lines = new List<string>
        {
            "@echo off",
            $"echo Warte auf Prozess {pid} >\"{log}\"",
            $"powershell -NoProfile -Command \"try {{ Wait-Process -Id {pid} -ErrorAction Stop }} catch {{}}\" >>\"{log}\" 2>&1",
            // Kurzer Nachlauf, damit File-Handles frei sind.
            "ping 127.0.0.1 -n 2 >NUL",
            $"if not exist \"{pluginsDir}\" mkdir \"{pluginsDir}\"",
            $"echo Ersetze Plugin-DLLs >>\"{log}\""
        };
        foreach (var dll in extractedDlls)
        {
            var target = Path.Combine(pluginsDir, Path.GetFileName(dll));
            lines.Add($"copy /Y \"{dll}\" \"{target}\" >>\"{log}\" 2>&1");
        }
        lines.Add($"echo Starte Cockpit neu >>\"{log}\"");
        lines.Add($"start \"\" \"{exe}\"");

        File.WriteAllLines(bat, lines);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{bat}\"\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = work
        });
        Log.Info("Plugin-Update-Batch gestartet ({Bat}) — App wird beendet.", bat);
        return true;
    }
}
