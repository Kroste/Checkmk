using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using NLog;

namespace Checkmk.App.Services;

public sealed record AgentUpdateResult(bool Success, string Output);

/// <summary>
/// Aktualisiert den Checkmk-Agent auf einem entfernten Windows-Host per Remote-PowerShell.
///
/// Ablauf (Windows-only): powershell.exe liest das Skript ueber STDIN (Passwort steht
/// damit NICHT in den Prozess-Argumenten). Das Skript:
///  1. sucht den aktuellen MSI im Share (mit den Credentials DIESES Rechners),
///  2. oeffnet eine PSSession zum Zielhost mit den (geprompteten) Admin-Credentials,
///  3. kopiert den Installer per Copy-Item -ToSession auf den Host (umgeht Double-Hop),
///  4. fuehrt die editierbare Skript-Vorlage auf dem Host aus (Deinstall/Install/Register).
/// </summary>
[SupportedOSPlatform("windows")]
public static class AgentUpdater
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public static async Task<AgentUpdateResult> RunAsync(
        string host, string adminUser, string adminPassword,
        string share, string scriptTemplate,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        const string remoteInstaller = @"C:\Windows\Temp\checkmk-agent-update.msi";

        // Platzhalter in der Vorlage ersetzen (laeuft spaeter auf dem Host).
        var body = scriptTemplate
            .Replace("{host}", host)
            .Replace("{installer}", remoteInstaller);

        // Aeusseres Skript (laeuft auf DIESEM Rechner, baut die Session auf).
        // Tokens statt String-Interpolation, damit die PowerShell-{ } nicht kollidieren.
        const string outer = """
            $ErrorActionPreference = 'Stop'
            $u = '%%USER%%'
            $p = '%%PASS%%'
            $h = '%%HOST%%'
            $share = '%%SHARE%%'
            $remote = '%%REMOTE%%'
            Write-Output "== Client-Aktualisierung fuer $h =="
            Write-Output "Suche aktuellen Installer in $share ..."
            $src = Get-ChildItem -Path $share -Filter *.msi -ErrorAction Stop |
                   Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
            if (-not $src) { throw "Kein MSI im Share gefunden: $share" }
            Write-Output "Installer: $src"
            $sec = ConvertTo-SecureString $p -AsPlainText -Force
            $cred = New-Object System.Management.Automation.PSCredential($u, $sec)
            Write-Output "Verbinde zu $h ..."
            $s = New-PSSession -ComputerName $h -Credential $cred
            try {
                Write-Output "Kopiere Installer auf $h ..."
                Copy-Item -ToSession $s -Path $src -Destination $remote -Force
                Invoke-Command -Session $s -ScriptBlock {
            %%BODY%%
                }
            }
            finally { if ($s) { Remove-PSSession $s } }
            """;

        var script = outer
            .Replace("%%USER%%", Esc(adminUser))
            .Replace("%%PASS%%", Esc(adminPassword))
            .Replace("%%HOST%%", Esc(host))
            .Replace("%%SHARE%%", Esc(share))
            .Replace("%%REMOTE%%", remoteInstaller)
            .Replace("%%BODY%%", IndentBody(body));

        return await ExecutePowerShellAsync(script, progress, ct);
    }

    private static string IndentBody(string body)
    {
        var sb = new StringBuilder();
        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
            sb.Append("            ").Append(line).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>PowerShell einzelnes '-Argument fuer single-quoted Strings escapen.</summary>
    private static string Esc(string s) => s.Replace("'", "''");

    private static async Task<AgentUpdateResult> ExecutePowerShellAsync(
        string script, IProgress<string>? progress, CancellationToken ct)
    {
        var output = new StringBuilder();
        var ok = true;

        void Emit(string line)
        {
            output.AppendLine(line);
            progress?.Report(line);
        }

        // Mehrzeilige Skripte laufen ueber '-Command -' (STDIN) unzuverlaessig und
        // liefern teils gar keine Ausgabe. Deshalb: Temp-.ps1 schreiben und mit -File
        // ausfuehren (zuverlaessig fuer mehrzeilige Skripte).
        var tempPs1 = Path.Combine(Path.GetTempPath(), $"cmk-agent-update-{Guid.NewGuid():N}.ps1");
        try
        {
            await File.WriteAllTextAsync(tempPs1, script, new UTF8Encoding(true), ct);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempPs1}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) Emit(e.Data); };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                ok = false;
                Emit("FEHLER: " + e.Data);
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode != 0) ok = false;
            Emit($"--- Exit-Code: {proc.ExitCode} ---");
        }
        catch (Exception ex)
        {
            // WICHTIG: niemals 'script' loggen — enthaelt Admin- und Register-Passwort.
            Log.Warn(ex, "Agent-Update-Prozess konnte nicht ausgefuehrt werden.");
            Emit("FEHLER: " + ex.Message);
            ok = false;
        }
        finally
        {
            try { if (File.Exists(tempPs1)) File.Delete(tempPs1); }
            catch { /* best effort */ }
        }

        if (output.Length == 0)
            Emit("(keine Ausgabe erhalten — Skript wurde evtl. nicht ausgefuehrt)");

        return new AgentUpdateResult(ok, output.ToString());
    }
}
