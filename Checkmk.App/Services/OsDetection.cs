using System.Text.RegularExpressions;

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
}
