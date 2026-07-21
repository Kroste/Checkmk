using System.Net;
using System.Text.Json;
using Checkmk.App.Services.Plugins;
using NLog;

namespace Checkmk.App.Services;

/// <summary>Ergebnis einer Plugin-Update-Pruefung fuer ein einzelnes Plugin.</summary>
public sealed record PluginUpdateInfo(
    string PluginId,
    string PluginName,
    string CurrentVersion,
    string LatestVersion,
    bool UpdateAvailable,
    string ReleasePageUrl,
    string? ZipAssetUrl,
    string? ZipAssetName);

/// <summary>
/// Prueft pro geladenem Plugin ueber dessen <c>PluginMetadata.UpdateChannelUrl</c>
/// (GitHub-Releases-latest-Endpoint) ob eine neuere Version verfuegbar ist.
/// Proxy-aware wie der Cockpit-<see cref="GitHubReleasesUpdateChecker"/>.
/// </summary>
public sealed class PluginUpdateService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly HttpClient _http;
    private readonly IReadOnlyList<LoadedPlugin> _plugins;

    public PluginUpdateService(IReadOnlyList<LoadedPlugin> plugins)
    {
        _plugins = plugins;
        // Analog zum Cockpit-UpdateChecker: DefaultProxyCredentials fuer den
        // Firmen-Proxy (FortiProxy will Kerberos/NTLM).
        var handler = new HttpClientHandler
        {
            UseProxy = true,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Checkmk-Cockpit-PluginUpdate");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>Prueft alle Plugins, die eine UpdateChannelUrl gesetzt haben.</summary>
    public async Task<IReadOnlyList<PluginUpdateInfo>> CheckAllAsync(CancellationToken ct = default)
    {
        var results = new List<PluginUpdateInfo>();
        foreach (var p in _plugins)
        {
            var url = p.Metadata.UpdateChannelUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                Log.Debug("Plugin {Id} hat keine UpdateChannelUrl — Check uebersprungen.", p.Metadata.Id);
                continue;
            }
            try
            {
                var info = await CheckOneAsync(p, url, ct);
                if (info is not null) results.Add(info);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Plugin-Update-Check fehlgeschlagen fuer {Id}.", p.Metadata.Id);
            }
        }
        return results;
    }

    private async Task<PluginUpdateInfo?> CheckOneAsync(LoadedPlugin plugin, string url, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            Log.Warn("Plugin-Update-Check {Id}: HTTP {Status}", plugin.Metadata.Id, (int)resp.StatusCode);
            return null;
        }
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var htmlUrl = root.TryGetProperty("html_url", out var hu) ? hu.GetString() ?? "" : "";

        var latest = ParseVersion(tag);
        if (latest is null)
        {
            Log.Debug("Plugin {Id}: Tag '{Tag}' nicht parsebar.", plugin.Metadata.Id, tag);
            return null;
        }
        var current = ParseVersion(plugin.Metadata.Version) ?? new Version(0, 0, 0);

        // Passendes ZIP-Asset heraussuchen (Konvention <Repo>-X.Y.Z.zip).
        string? zipUrl = null;
        string? zipName = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString();
                if (name is not null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    zipName = name;
                    zipUrl = a.GetProperty("browser_download_url").GetString();
                    break;
                }
            }
        }

        return new PluginUpdateInfo(
            plugin.Metadata.Id, plugin.Metadata.Name,
            current.ToString(), latest.ToString(),
            UpdateAvailable: Normalize(latest) > Normalize(current),
            htmlUrl, zipUrl, zipName);
    }

    /// <summary>"v1.2.3", "1.2.3+abc" oder "1.2.3-alpha" -> Version. Null bei Muell.</summary>
    internal static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var s = tag.Trim().TrimStart('v', 'V');
        var cut = s.IndexOfAny(['+', '-']);
        if (cut >= 0) s = s[..cut];
        return Version.TryParse(s, out var v) ? v : null;
    }

    /// <summary>"1.2.3" (Rev=-1) und "1.2.3.0" (Rev=0) auf gleicher Basis vergleichen.</summary>
    internal static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
}
