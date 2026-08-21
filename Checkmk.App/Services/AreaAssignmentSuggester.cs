using Checkmk.Data;

namespace Checkmk.App.Services;

/// <summary>Woher ein Vorschlag stammt.</summary>
public enum SuggestionSource
{
    /// <summary>Checkmk-Ortstag des Hosts — gepflegte Angabe.</summary>
    Tag,

    /// <summary>Regulärer Ausdruck auf dem Hostnamen — erschlossen.</summary>
    Pattern
}

/// <summary>Ein Zuordnungsvorschlag für genau einen Host.</summary>
/// <param name="CurrentAreaId">Wo der Host heute steht, oder <c>null</c>.</param>
/// <param name="ConflictingAreas">Weitere Bereiche, die ebenfalls passen.
/// Nicht leer heißt: nicht eindeutig, Finger weg ohne Nachdenken.</param>
public sealed record AssignmentSuggestion(
    string HostName,
    int AreaId,
    string AreaName,
    int? CurrentAreaId,
    string? CurrentAreaName,
    IReadOnlyList<string> ConflictingAreas,
    SuggestionSource Source = SuggestionSource.Pattern)
{
    /// <summary>Der Host steht schon woanders — eine Übernahme verschiebt ihn.</summary>
    public bool WouldMove => CurrentAreaId is { } id && id != AreaId;

    public bool IsAmbiguous => ConflictingAreas.Count > 0;

    /// <summary>Kurzfassung für die Liste im Dialog.</summary>
    public string Note
    {
        get
        {
            var why = Source == SuggestionSource.Tag ? "Tag" : "Muster";
            return IsAmbiguous
                ? $"mehrdeutig — passt auch auf {string.Join(", ", ConflictingAreas)}"
                : WouldMove
                    ? $"verschiebt von {CurrentAreaName} ({why})"
                    : $"neu ({why})";
        }
    }
}

/// <summary>
/// Schlägt Host-Zuordnungen vor — aus dem <b>Checkmk-Ortstag</b> des Hosts,
/// hilfsweise aus dem Namensmuster des Bereichs.
///
/// Die Rangfolge ist keine Geschmacksfrage: Der Tag steht im Checkmk-Setup und
/// ist gepflegt, das Muster erschließt dieselbe Information aus dem Hostnamen.
/// Wo beide etwas sagen, hat der Tag recht — <c>WLC-01SL-01</c> trägt
/// <c>schule_01</c>, aber kein Namensmuster der Welt liest daraus eine 1, ohne
/// gleichzeitig die halbe Anlage einzusammeln.
///
/// Bewusst nur <b>Vorschläge</b>: Ein Muster kann danebenliegen, und eine
/// falsche Massenzuordnung von tausend Hosts hinterher aufzuräumen ist deutlich
/// teurer, als sie einmal durchzusehen. Deshalb liefert die Klasse eine Liste
/// zum Bestätigen und ordnet nichts selbst zu.
/// </summary>
public static class AreaAssignmentSuggester
{
    /// <param name="areas">Bereiche samt Tag und Muster; ohne beides übersprungen.</param>
    /// <param name="hostNames">Alle bekannten Hosts der aktuellen Sicht.</param>
    /// <param name="hostToArea">Bestehende Zuordnung.</param>
    /// <param name="tagFor">Ortstag eines Hosts, oder <c>null</c>. Fehlt der
    /// Parameter, wird ausschließlich über Muster vorgeschlagen — so verhält
    /// sich der Aufruf wie vor Einführung der Tags.</param>
    public static IReadOnlyList<AssignmentSuggestion> Suggest(
        IReadOnlyList<AreaRow> areas,
        IEnumerable<string> hostNames,
        IReadOnlyDictionary<string, int> hostToArea,
        Func<string, string?>? tagFor = null)
    {
        var withPattern = areas.Where(a => !string.IsNullOrWhiteSpace(a.HostPattern)).ToList();

        // Mehrere Bereiche mit demselben Tag sind in der Datenbank durch einen
        // eindeutigen Index ausgeschlossen; kommen sie aus einem aelteren
        // Schema trotzdem vor, sollen sie als mehrdeutig auffallen statt still
        // den ersten gewinnen zu lassen.
        var byTag = areas
            .Where(a => !string.IsNullOrWhiteSpace(a.HostTag))
            .GroupBy(a => a.HostTag!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        if (withPattern.Count == 0 && byTag.Count == 0) return [];

        var nameById = areas.ToDictionary(a => a.AreaId, a => a.Name);
        var result = new List<AssignmentSuggestion>();

        foreach (var host in hostNames.Distinct(StringComparer.OrdinalIgnoreCase)
                                      .OrderBy(h => h, StringComparer.OrdinalIgnoreCase))
        {
            List<AreaRow> hits;
            SuggestionSource source;

            if (tagFor?.Invoke(host) is { } tag
                && byTag.TryGetValue(tag.Trim(), out var tagged) && tagged.Count > 0)
            {
                hits = tagged;
                source = SuggestionSource.Tag;
            }
            else
            {
                hits = [.. withPattern.Where(a => HostPatternMatcher.Matches(a.HostPattern, host))];
                source = SuggestionSource.Pattern;
                if (hits.Count == 0) continue;
            }

            // Trifft mehr als ein Bereich, gewinnt keiner. Der Anwender sieht
            // beide Namen und entscheidet — automatisch den ersten zu nehmen
            // waere eine stille Fehlzuordnung.
            var chosen = hits[0];
            var conflicts = hits.Skip(1).Select(a => a.Name).ToList();

            hostToArea.TryGetValue(host, out var currentRaw);
            int? current = hostToArea.ContainsKey(host) ? currentRaw : null;

            // Schon richtig zugeordnet und eindeutig? Dann gibt es nichts vorzuschlagen.
            if (current == chosen.AreaId && conflicts.Count == 0) continue;

            result.Add(new AssignmentSuggestion(
                host,
                chosen.AreaId,
                chosen.Name,
                current,
                current is { } c ? nameById.GetValueOrDefault(c) : null,
                conflicts,
                source));
        }

        return result;
    }
}
