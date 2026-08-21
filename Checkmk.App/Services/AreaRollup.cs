using Checkmk.Core.Models;
using Checkmk.Data;

namespace Checkmk.App.Services;

/// <summary>Aggregat eines Bereichs — inklusive seiner Unterbereiche.</summary>
/// <param name="HostCount">Hosts im Bereich und darunter, die auf den aktiven Filter passen.</param>
/// <param name="ProblemCount">Davon mit Status schlechter als OK.</param>
/// <param name="Worst">Schlechtester Status; <c>Ok</c> auch dann, wenn gar kein Host da ist.</param>
/// <param name="HasHosts">false = leerer Bereich. Wird gebraucht, weil ein leerer
/// Bereich nicht gruen aussehen soll wie ein gesunder.</param>
public sealed record AreaAggregate(int HostCount, int ProblemCount, ServiceState Worst, bool HasHosts)
{
    public static readonly AreaAggregate Empty = new(0, 0, ServiceState.Ok, false);
}

/// <summary>
/// Rechnet den Status eines Bereichs aus den Hosts darin hoch: schlechtester
/// gewinnt, von den Blaettern nach oben.
///
/// Bewusst statisch und ohne Seiteneffekte — das ist die Stelle, an der die
/// Farbe der Karte entsteht, und sie soll ohne Datenbank und ohne UI pruefbar
/// sein.
///
/// <b>Die Host-Menge ist bereits die Linse.</b> Der Aufrufer uebergibt nur die
/// Hosts, die auf den aktiven Filter passen — deshalb ist derselbe Serverraum
/// fuer das DB-Team gruen und fuer den Wachschutz rot. Nicht hier zusaetzlich
/// filtern wollen.
/// </summary>
public static class AreaRollup
{
    /// <summary>
    /// Aggregat je Bereichs-Id. Bereiche ohne eigene und ohne geerbte Hosts
    /// kommen mit <see cref="AreaAggregate.Empty"/> zurueck.
    /// </summary>
    public static Dictionary<int, AreaAggregate> Compute(
        IReadOnlyList<AreaRow> areas,
        IReadOnlyDictionary<string, int> hostToArea,
        IReadOnlyDictionary<string, ServiceState> hostWorstState)
    {
        var result = new Dictionary<int, AreaAggregate>();
        if (areas.Count == 0) return result;

        var childrenOf = new Dictionary<int, List<int>>();
        var known = new HashSet<int>();
        foreach (var a in areas)
        {
            known.Add(a.AreaId);
            if (a.ParentAreaId is { } p)
            {
                if (!childrenOf.TryGetValue(p, out var list))
                    childrenOf[p] = list = [];
                list.Add(a.AreaId);
            }
        }

        // Direkt zugeordnete Hosts je Bereich. Zuordnungen auf geloeschte
        // Bereiche werden ignoriert statt zu werfen — die Tabelle kann durch
        // einen Fremdschluessel zwar nicht verwaisen, aber die Momentaufnahme
        // kann aelter sein als der Baum.
        var directHosts = new Dictionary<int, List<string>>();
        foreach (var (host, areaId) in hostToArea)
        {
            if (!known.Contains(areaId)) continue;
            if (!hostWorstState.ContainsKey(host)) continue;   // nicht im Filter
            if (!directHosts.TryGetValue(areaId, out var list))
                directHosts[areaId] = list = [];
            list.Add(host);
        }

        // Wurzeln: kein Elternteil oder ein Elternteil, den es nicht (mehr) gibt.
        var roots = areas
            .Where(a => a.ParentAreaId is not { } p || !known.Contains(p))
            .Select(a => a.AreaId);

        // Der visited-Satz schuetzt gegen einen Zyklus im Baum. Die Datenbank
        // verhindert ihn nicht (ein UPDATE reicht), und ohne Schutz haengt hier
        // die Oberflaeche statt eine falsche Zahl zu zeigen.
        var visited = new HashSet<int>();
        foreach (var root in roots)
            Walk(root);

        // Bereiche, die durch einen Zyklus nicht erreicht wurden, trotzdem melden.
        foreach (var a in areas)
            result.TryAdd(a.AreaId, AreaAggregate.Empty);

        return result;

        AreaAggregate Walk(int areaId)
        {
            if (!visited.Add(areaId))
                return result.GetValueOrDefault(areaId, AreaAggregate.Empty);

            var hostCount = 0;
            var problemCount = 0;
            var worst = ServiceState.Ok;
            var hasHosts = false;

            if (directHosts.TryGetValue(areaId, out var hosts))
            {
                foreach (var h in hosts)
                {
                    var state = hostWorstState[h];
                    hostCount++;
                    hasHosts = true;
                    if (state != ServiceState.Ok) problemCount++;
                    if (Severity(state) > Severity(worst)) worst = state;
                }
            }

            if (childrenOf.TryGetValue(areaId, out var children))
            {
                foreach (var child in children)
                {
                    var sub = Walk(child);
                    hostCount += sub.HostCount;
                    problemCount += sub.ProblemCount;
                    hasHosts |= sub.HasHosts;
                    if (Severity(sub.Worst) > Severity(worst)) worst = sub.Worst;
                }
            }

            var aggregate = new AreaAggregate(hostCount, problemCount, worst, hasHosts);
            result[areaId] = aggregate;
            return aggregate;
        }
    }

    /// <summary>
    /// Schlechtester Service-Status je Host. CRIT &gt; WARN &gt; UNKNOWN &gt; OK —
    /// dieselbe Reihenfolge wie im Host-Baum des Status-Tabs.
    /// </summary>
    public static Dictionary<string, ServiceState> WorstStatePerHost(
        IEnumerable<ServiceStatus> services)
    {
        var worst = new Dictionary<string, ServiceState>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in services)
        {
            if (worst.TryGetValue(s.HostName, out var current)
                && Severity(current) >= Severity(s.ServiceState))
                continue;
            worst[s.HostName] = s.ServiceState;
        }
        return worst;
    }

    /// <summary>UNKNOWN zaehlt schwerer als OK, aber leichter als WARN — ein
    /// Check, der nichts sagt, ist ein Hinweis und kein Alarm.</summary>
    private static int Severity(ServiceState state) => state switch
    {
        ServiceState.Critical => 3,
        ServiceState.Warning => 2,
        ServiceState.Unknown => 1,
        _ => 0
    };
}
