using System.Text.Json;
using System.Text.RegularExpressions;
using Checkmk.Core.Models;

namespace Checkmk.App.Services;

public enum OsFamily
{
    Unknown,
    Windows,
    Linux
}

/// <summary>
/// Ermittelt die OS-Familie aus der Check_MK-Agent-Service-Ausgabe
/// (z. B. "Version: 2.5.0p2, OS: windows, ..."). Zusatzdienst-frei, weil die
/// App die Service-Ausgaben ohnehin laedt.
/// </summary>
public static partial class OsDetection
{
    [GeneratedRegex(@"OS:\s*([A-Za-z]+)", RegexOptions.IgnoreCase)]
    private static partial Regex OsRegex();

    public static OsFamily ParseFamily(string? agentOutput)
    {
        if (string.IsNullOrWhiteSpace(agentOutput))
            return OsFamily.Unknown;

        var m = OsRegex().Match(agentOutput);
        if (!m.Success)
            return OsFamily.Unknown;

        return m.Groups[1].Value.ToLowerInvariant() switch
        {
            "windows" or "win" => OsFamily.Windows,
            "linux" => OsFamily.Linux,
            _ => OsFamily.Unknown
        };
    }

    public static string Label(OsFamily os) => os switch
    {
        OsFamily.Windows => "Windows",
        OsFamily.Linux => "Linux",
        _ => "?"
    };

    /// <summary>
    /// Sucht das OS anhand einer priorisierten Kandidaten-Liste im
    /// Custom-/Effective-Attributes-Dict. Erster Treffer gewinnt.
    /// Wert-Mapping via Contains (case-insensitive) — "Windows Server 2022"
    /// zaehlt als Windows, "RHEL Linux 9" als Linux.
    /// </summary>
    public static OsFamily ResolveFromAttributes(HostAttributes? attrs, IReadOnlyList<string> candidateKeys)
    {
        if (attrs?.AdditionalProperties is not { Count: > 0 } dict) return OsFamily.Unknown;

        foreach (var key in candidateKeys)
        {
            if (!dict.TryGetValue(key, out var el)) continue;
            var raw = ExtractString(el);
            if (string.IsNullOrWhiteSpace(raw)) continue;
            return ClassifyOsString(raw);
        }
        return OsFamily.Unknown;
    }

    private static string? ExtractString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.ToString(),
        // manche Custom-Attribute kommen als Objekt {"value": "..."} — defensiv abfangen
        JsonValueKind.Object when el.TryGetProperty("value", out var v) => v.GetString(),
        _ => null
    };

    private static OsFamily ClassifyOsString(string s)
    {
        var lower = s.ToLowerInvariant();
        if (lower.Contains("windows") || lower.Contains("win32") || lower.Contains("win64"))
            return OsFamily.Windows;
        if (lower.Contains("linux") || lower.Contains("rhel") || lower.Contains("debian")
            || lower.Contains("ubuntu") || lower.Contains("centos") || lower.Contains("suse")
            || lower.Contains("fedora"))
            return OsFamily.Linux;
        return OsFamily.Unknown;
    }
}
