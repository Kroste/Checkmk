using Checkmk.Data;

namespace Checkmk.App.Services;

/// <summary>Ein Zuordnungsvorschlag für genau einen Host.</summary>
/// <param name="CurrentAreaId">Wo der Host heute steht, oder <c>null</c>.</param>
/// <param name="ConflictingAreas">Weitere Bereiche, deren Muster ebenfalls
/// passen. Nicht leer heißt: nicht eindeutig, Finger weg ohne Nachdenken.</param>
public sealed record AssignmentSuggestion(
    string HostName,
    int AreaId,
    string AreaName,
    int? CurrentAreaId,
    string? CurrentAreaName,
    IReadOnlyList<string> ConflictingAreas)
{
    /// <summary>Der Host steht schon woanders — eine Übernahme verschiebt ihn.</summary>
    public bool WouldMove => CurrentAreaId is { } id && id != AreaId;

    public bool IsAmbiguous => ConflictingAreas.Count > 0;

    /// <summary>Kurzfassung für die Liste im Dialog.</summary>
    public string Note => IsAmbiguous
        ? $"mehrdeutig — passt auch auf {string.Join(", ", ConflictingAreas)}"
        : WouldMove
            ? $"verschiebt von {CurrentAreaName}"
            : "neu";
}

/// <summary>
/// Schlägt Host-Zuordnungen aus den Namensmustern der Bereiche vor.
///
/// Bewusst nur <b>Vorschläge</b>: Ein Muster kann danebenliegen, und eine
/// falsche Massenzuordnung von tausend Hosts hinterher aufzuräumen ist
/// deutlich teurer, als sie einmal durchzusehen. Deshalb liefert die Klasse
/// eine Liste zum Bestätigen und ordnet nichts selbst zu.
/// </summary>
public static class AreaAssignmentSuggester
{
    /// <param name="areas">Bereiche samt Muster; ohne Muster wird übersprungen.</param>
    /// <param name="hostNames">Alle bekannten Hosts der aktuellen Sicht.</param>
    /// <param name="hostToArea">Bestehende Zuordnung.</param>
    public static IReadOnlyList<AssignmentSuggestion> Suggest(
        IReadOnlyList<AreaRow> areas,
        IEnumerable<string> hostNames,
        IReadOnlyDictionary<string, int> hostToArea)
    {
        var withPattern = areas
            .Where(a => !string.IsNullOrWhiteSpace(a.HostPattern))
            .ToList();
        if (withPattern.Count == 0) return [];

        var nameById = areas.ToDictionary(a => a.AreaId, a => a.Name);
        var result = new List<AssignmentSuggestion>();

        foreach (var host in hostNames.Distinct(StringComparer.OrdinalIgnoreCase)
                                      .OrderBy(h => h, StringComparer.OrdinalIgnoreCase))
        {
            var hits = withPattern
                .Where(a => HostPatternMatcher.Matches(a.HostPattern, host))
                .ToList();
            if (hits.Count == 0) continue;

            // Trifft mehr als ein Muster, gewinnt keines. Der Anwender sieht
            // beide Namen und entscheidet — automatisch das erste zu nehmen
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
                conflicts));
        }

        return result;
    }
}
