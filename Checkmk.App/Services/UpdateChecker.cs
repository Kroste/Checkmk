using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Checkmk.Core;
using NLog;

namespace Checkmk.App.Services;

public sealed record UpdateInfo(
    Version Version,
    string TagName,
    string ReleaseNotes,
    string ReleasePageUrl,
    string? WindowsZipUrl);

public interface IUpdateChecker
{
    /// <summary>
    /// Fragt den Update-Kanal an, vergleicht mit der laufenden Assembly-Version
    /// und beruecksichtigt die vom Nutzer uebersprungene Version.
    /// Liefert <c>null</c>, wenn keine neuere Version verfuegbar ist oder der
    /// Check fehlschlaegt (Fehler werden geloggt, nie ins UI durchgereicht).
    /// </summary>
    Task<UpdateInfo?> CheckAsync(CancellationToken ct = default);
}

/// <summary>
/// Fragt <c>GET /repos/{owner}/{repo}/releases/latest</c> ab. Draft- und
/// Prerelease-Versionen filtert GitHub selbst; wir vertrauen dem Endpunkt.
/// Trust-Anker: TLS-Zertifikat von <c>api.github.com</c> + Repo-Owner in der URL.
/// </summary>
public sealed class GitHubReleasesUpdateChecker : IUpdateChecker
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly HttpClient _http;
    private readonly string _apiUrl;
    private readonly IUpdatePreferences _prefs;

    public GitHubReleasesUpdateChecker(HttpClient http, string apiUrl, IUpdatePreferences prefs)
    {
        _http = http;
        _apiUrl = apiUrl;
        _prefs = prefs;

        // GitHub verlangt einen User-Agent, sonst 403.
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Checkmk-Cockpit/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        var currentAsm = Assembly.GetExecutingAssembly().GetName().Version;
        if (currentAsm is null)
        {
            Log.Debug("Keine Assembly-Version — Update-Check uebersprungen.");
            return null;
        }
        var current = new Version(currentAsm.Major, currentAsm.Minor,
            Math.Max(0, currentAsm.Build), Math.Max(0, currentAsm.Revision));

        GitHubRelease? release;
        try
        {
            release = await _http.GetFromJsonAsync<GitHubRelease>(_apiUrl, ct);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Update-Check fehlgeschlagen ({Url}).", _apiUrl);
            return null;
        }
        if (release is null || string.IsNullOrEmpty(release.TagName))
            return null;

        if (!SemVerTag.TryParse(release.TagName, out var latest))
        {
            Log.Debug("Konnte Release-Tag '{Tag}' nicht als Version parsen.", release.TagName);
            return null;
        }

        if (latest <= current)
            return null;

        var skipped = _prefs.LoadSkippedVersion();
        if (skipped is not null && skipped >= latest)
        {
            Log.Debug("Neuere Version {Latest} verfuegbar, aber vom User uebersprungen.", latest);
            return null;
        }

        var zip = release.Assets?
            .FirstOrDefault(a => a.Name is not null &&
                                 a.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase) &&
                                 a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ?.BrowserDownloadUrl;

        return new UpdateInfo(
            Version: latest,
            TagName: release.TagName,
            ReleaseNotes: release.Body ?? "",
            ReleasePageUrl: release.HtmlUrl ?? "",
            WindowsZipUrl: zip);
    }

    // ---- GitHub API DTOs ----
    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] List<GitHubAsset>? Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl);
}
