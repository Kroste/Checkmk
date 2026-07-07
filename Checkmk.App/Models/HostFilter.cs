using System.Text.RegularExpressions;

namespace Checkmk.App.Models;

/// <summary>
/// Persistierbarer Host-Filter. Zwei Modi, die sich gegenseitig ausschliessen:
/// <list type="bullet">
///   <item><see cref="ExplicitHosts"/> nicht leer → Include-Liste (exakte Hostnamen).</item>
///   <item>Sonst <see cref="HostNameRegex"/> → Regex-Match auf den Hostnamen.</item>
///   <item>Beides leer → matcht alle Hosts (Standard).</item>
/// </list>
/// </summary>
public sealed class HostFilter
{
    public string Name { get; set; } = "";
    public string? HostNameRegex { get; set; }
    public List<string> ExplicitHosts { get; set; } = new();

    public bool Matches(string hostName)
    {
        if (ExplicitHosts.Count > 0)
            return ExplicitHosts.Any(h => string.Equals(h, hostName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(HostNameRegex))
        {
            try
            {
                return Regex.IsMatch(hostName, HostNameRegex, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException)
            {
                // ungueltiges Regex → matched nichts, damit der Anwender es visuell sofort merkt
                return false;
            }
        }

        return true;
    }

    public override string ToString() => Name;
}
