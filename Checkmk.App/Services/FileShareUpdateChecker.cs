using System.Reflection;
using System.Text.RegularExpressions;
using Checkmk.Core;
using NLog;

namespace Checkmk.App.Services;

/// <summary>
/// Update-Kanal auf einem Ordner statt auf GitHub — typischerweise ein
/// Fileshare wie
/// <c>\\samba01\542$\5424_IT-Basis-Dienste\CheckMK\CheckMK_Copilot</c>.
///
/// <para>Der Ordner braucht nur das ZIP; die Version steht im Dateinamen
/// (<c>Checkmk-1.14.0-win-x64.zip</c>). Liegt zusätzlich ein
/// <c>update.json</c> daneben, gilt <b>das</b> als Index — und sobald ein
/// Signaturschlüssel im Binary steht, ist es Pflicht.</para>
///
/// <para><b>Warum die Version aus dem Dateinamen und nicht aus dem ZIP?</b> Um
/// sie aus dem Paket zu lesen, müsste man es erst herunterladen und auspacken —
/// bei 88 MB je Start, nur um festzustellen, dass sich nichts geändert hat. Der
/// Name reicht, und der Rest wird ohnehin gegen das signierte Manifest geprüft,
/// bevor irgendetwas passiert.</para>
/// </summary>
/// <param name="currentVersion">Laufende Version. Nur für Tests zu setzen — im
/// Betrieb gilt <see cref="AppVersion.Current"/>. Ohne diesen Haken müssten
/// Tests gegen die Version des Testhosts vergleichen, und dann prüfen sie
/// nichts Sinnvolles mehr.</param>
public sealed class FileShareUpdateChecker(
    string folder, IUpdatePreferences prefs, Version? currentVersion = null)
    : IUpdateChecker
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Optionale Release-Notes im Kanal-Ordner. Fehlen sie, bleibt der
    /// Dialog bei „keine Notizen" — kein Grund, das Update zu verschweigen.</summary>
    private static readonly string[] NoteFiles = ["RELEASE_NOTES.md", "notes.md", "README.md"];

    /// <summary><c>Checkmk-1.14.0-win-x64.zip</c> → 1.14.0.</summary>
    private static readonly Regex VersionInName = new(
        @"(\d+\.\d+\.\d+(?:\.\d+)?)", RegexOptions.Compiled);

    /// <summary>
    /// Erkennt, ob eine Kanal-Angabe ein Ordner ist statt einer Adresse. UNC
    /// (<c>\\server\share</c>) und Laufwerksbuchstaben zählen dazu; alles mit
    /// <c>http://</c> oder <c>https://</c> nicht.
    /// </summary>
    public static bool LooksLikeFolder(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel)) return false;
        var s = channel.Trim();
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;
        return s.StartsWith(@"\\") || s.StartsWith("//")
            || (s.Length > 2 && s[1] == ':');
    }

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        var (outcome, info) = await EvaluateAsync(honorSkip: true, ct).ConfigureAwait(false);
        return outcome == UpdateCheckOutcome.UpdateAvailable ? info : null;
    }

    public async Task<UpdateCheckResult> CheckManuallyAsync(CancellationToken ct = default)
    {
        var (outcome, info) = await EvaluateAsync(honorSkip: false, ct).ConfigureAwait(false);
        return new UpdateCheckResult(outcome, info);
    }

    private Task<(UpdateCheckOutcome, UpdateInfo?)> EvaluateAsync(bool honorSkip,
        CancellationToken ct)
        => Task.Run(() => Evaluate(honorSkip), ct);

    private (UpdateCheckOutcome, UpdateInfo?) Evaluate(bool honorSkip)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                // Kein Fehler im Log-Rang „Error": Ein Notebook ohne
                // Netzlaufwerk ist der Normalfall, nicht die Stoerung.
                Log.Debug("Update-Ordner {Folder} nicht erreichbar.", folder);
                return (UpdateCheckOutcome.Failed, null);
            }

            var manifestPath = Path.Combine(folder, GitHubReleasesUpdateChecker.UpdateManifestFileName);
            var manifest = File.Exists(manifestPath)
                ? UpdateSignature.FromJson(File.ReadAllText(manifestPath))
                : null;

            if (UpdateSignature.IsEnforced && manifest is null)
            {
                Log.Warn("Im Update-Ordner {Folder} fehlt {File} — ohne signiertes "
                       + "Manifest wird nicht aktualisiert.",
                    folder, GitHubReleasesUpdateChecker.UpdateManifestFileName);
                return (UpdateCheckOutcome.Failed, null);
            }

            var zip = manifest is not null
                ? Path.Combine(folder, manifest.File)
                : NewestZip();

            if (zip is null || !File.Exists(zip))
            {
                Log.Debug("Kein Paket im Update-Ordner {Folder}.", folder);
                return (UpdateCheckOutcome.Failed, null);
            }

            if (ParseVersion(manifest?.Version ?? Path.GetFileName(zip)) is not { } latest)
            {
                Log.Warn("Version aus '{Name}' nicht lesbar — erwartet wird etwas wie "
                       + "Checkmk-1.14.0-win-x64.zip.", Path.GetFileName(zip));
                return (UpdateCheckOutcome.Failed, null);
            }

            // Ueber AppVersion.Current, nie ueber GetName().Version: MinVer setzt
            // die AssemblyVersion nur auf Major.0.0.0, und der Vergleich damit
            // bietet dann sogar ein Downgrade an (laufend 1.15.1 -> „Update auf
            // 1.14.0"). Real passiert, siehe AppVersion.
            var current = currentVersion ?? AppVersion.Current;
            if (Normalize(latest) <= Normalize(current))
            {
                Log.Debug("Paket {Latest} im Ordner ist nicht neuer als {Current}.",
                    latest, current);
                return (UpdateCheckOutcome.UpToDate, null);
            }

            if (honorSkip && prefs.LoadSkippedVersion() is { } skipped
                && Normalize(skipped) >= Normalize(latest))
            {
                Log.Debug("Version {Latest} verfuegbar, aber vom User uebersprungen.", latest);
                return (UpdateCheckOutcome.UpToDate, null);
            }

            Log.Info("Update im Ordner gefunden: {Version} ({Zip}).", latest, Path.GetFileName(zip));

            return (UpdateCheckOutcome.UpdateAvailable, new UpdateInfo(
                Version: latest,
                TagName: $"v{latest}",
                ReleaseNotes: ReadNotes(latest),
                // „Release-Seite oeffnen" oeffnet dann den Ordner im Explorer —
                // der sinnvollste Ersatz fuer eine Release-Seite.
                ReleasePageUrl: folder,
                WindowsZipUrl: zip,
                ManifestUrl: manifest is not null ? manifestPath : null));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Update-Check im Ordner {Folder} fehlgeschlagen.", folder);
            return (UpdateCheckOutcome.Failed, null);
        }
    }

    /// <summary>
    /// Neuestes Paket, wenn kein Manifest da ist. <b>Nach Version sortiert, nicht
    /// nach Datum</b> — ein zurückkopiertes älteres Paket hat den neueren
    /// Zeitstempel und würde sonst als Update angeboten.
    /// </summary>
    private string? NewestZip()
        => Directory.EnumerateFiles(folder, "Checkmk-*win-x64.zip")
            .Select(p => (Path: p, Version: ParseVersion(Path.GetFileName(p))))
            .Where(x => x.Version is not null)
            .OrderByDescending(x => Normalize(x.Version!))
            .Select(x => x.Path)
            .FirstOrDefault();

    private string ReadNotes(Version version)
    {
        // Erst versionsgenau, dann allgemein: So kann im Ordner sowohl
        // „v1.14.0.md" je Version liegen als auch eine gepflegte Sammeldatei.
        var candidates = new[] { $"v{version.Major}.{version.Minor}.{version.Build}.md",
                                 $"{version.Major}.{version.Minor}.{version.Build}.md" }
            .Concat(NoteFiles);

        foreach (var name in candidates)
        {
            var path = Path.Combine(folder, name);
            try
            {
                if (File.Exists(path)) return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Release-Notes {Path} nicht lesbar.", path);
            }
        }
        return "";
    }

    internal static Version? ParseVersion(string text)
    {
        var m = VersionInName.Match(text);
        return m.Success && Version.TryParse(m.Groups[1].Value, out var v) ? v : null;
    }

    /// <summary>Auf vier Segmente bringen — sonst ist „1.4.0" (Revision −1)
    /// kleiner als „1.4.0.0" (Revision 0). Gleiche Regel wie im GitHub-Checker.</summary>
    private static Version Normalize(Version v)
        => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
