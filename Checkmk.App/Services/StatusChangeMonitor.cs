using Checkmk.Core.Models;

namespace Checkmk.App.Services;

/// <summary>Zusammenfassung der Statusaenderungen seit dem letzten Snapshot.</summary>
public sealed record ChangeSummary(int NewProblems, int Recoveries, int OtherChanges, string? FirstExample)
{
    public int Total => NewProblems + Recoveries + OtherChanges;
    public bool HasChanges => Total > 0;

    /// <summary>Kompakter Text fuer Toast/Tooltip, z. B. "2 neue Probleme, 1 Recovery".</summary>
    public string ToText()
    {
        var parts = new List<string>();
        if (NewProblems > 0) parts.Add($"{NewProblems} neue{(NewProblems == 1 ? "s" : "")} Problem{(NewProblems == 1 ? "" : "e")}");
        if (Recoveries > 0) parts.Add($"{Recoveries} Recovery{(Recoveries == 1 ? "" : "s")}");
        if (OtherChanges > 0) parts.Add($"{OtherChanges} weitere Aenderung{(OtherChanges == 1 ? "" : "en")}");
        return string.Join(", ", parts);
    }
}

/// <summary>
/// Vergleicht aufeinanderfolgende Service-Snapshots (bereits auf den aktiven Filter
/// beschraenkt) und meldet Statusaenderungen. Der erste Aufruf initialisiert nur
/// den Ausgangszustand (keine Meldung).
/// </summary>
public sealed class StatusChangeMonitor
{
    private Dictionary<string, int>? _previous;

    /// <summary>Setzt den Monitor zurueck (z. B. bei Filterwechsel), damit kein Fehlalarm entsteht.</summary>
    public void Reset() => _previous = null;

    public ChangeSummary Diff(IReadOnlyList<ServiceStatus> services)
    {
        var current = new Dictionary<string, int>(services.Count);
        foreach (var s in services)
            current[Key(s)] = s.State;

        // Erster Lauf: nur Ausgangszustand merken.
        if (_previous is null)
        {
            _previous = current;
            return new ChangeSummary(0, 0, 0, null);
        }

        int newProblems = 0, recoveries = 0, other = 0;
        string? firstExample = null;

        foreach (var (key, newState) in current)
        {
            if (!_previous.TryGetValue(key, out var oldState) || oldState == newState)
                continue;

            var wasOk = oldState == (int)ServiceState.Ok;
            var isOk = newState == (int)ServiceState.Ok;

            if (isOk) recoveries++;
            else if (wasOk) newProblems++;
            else other++; // z. B. WARN -> CRIT

            firstExample ??= $"{key.Replace('\u0000', '/')}: {(ServiceState)oldState} -> {(ServiceState)newState}";
        }

        _previous = current;
        return new ChangeSummary(newProblems, recoveries, other, firstExample);
    }

    private static string Key(ServiceStatus s) => $"{s.HostName}\u0000{s.Description}";
}
