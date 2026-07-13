using System.Text.RegularExpressions;
using Checkmk.Core.Models;

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
    // Harter Cap gegen catastrophic backtracking bei bloed geschriebenen Regexes
    // ( ".*.*", "(a+)+", ...). 100 ms sind viel fuer einen einzelnen Hostnamen,
    // aber schuetzen zuverlaessig vor UI-Freezes.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

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
                return Regex.IsMatch(hostName, HostNameRegex, RegexOptions.IgnoreCase, RegexTimeout);
            }
            catch (ArgumentException)
            {
                // ungueltiges Regex → matched nichts, damit der Anwender es visuell sofort merkt
                return false;
            }
            catch (RegexMatchTimeoutException)
            {
                // Der Regex ist zwar syntaktisch gueltig, hat aber catastrophic backtracking.
                // Wir behandeln ihn wie "matched nichts", damit die App nicht einfriert.
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Bildet den Filter auf einen <see cref="LivestatusHostFilter"/> ab, den der
    /// Client als serverseitigen Livestatus-Query verschickt. Rueckgabe <c>null</c>
    /// wenn der Filter effektiv „alle Hosts" bedeutet (kein Regex, keine Include-Liste).
    /// </summary>
    public LivestatusHostFilter? ToLivestatus()
    {
        if (ExplicitHosts.Count > 0)
            return new LivestatusHostFilter { IncludeHosts = ExplicitHosts };
        if (!string.IsNullOrWhiteSpace(HostNameRegex))
            return new LivestatusHostFilter { HostNameRegex = HostNameRegex };
        return null;
    }

    public override string ToString() => Name;
}
