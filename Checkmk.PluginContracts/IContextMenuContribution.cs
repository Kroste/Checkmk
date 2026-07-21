namespace Checkmk.PluginContracts;

/// <summary>
/// Wo im Cockpit ein Kontextmenue-Eintrag angeboten werden kann. Der Cockpit
/// prueft <see cref="IContextMenuContribution.SupportsLocation"/> und blendet
/// die Kontribution nur an den passenden Stellen ein.
/// </summary>
public enum ContextMenuLocation
{
    /// <summary>Rechtsklick auf eine Service-Zeile im Status-Tab (Tabelle oder Baum).
    /// Der <see cref="ContextMenuTarget"/> hat einen <c>HostName</c>.</summary>
    StatusServiceRow,

    /// <summary>Rechtsklick auf einen Host-Knoten in der Baumansicht des Status-Tabs.</summary>
    StatusHostNode,

    /// <summary>Rechtsklick auf eine Zeile im Hosts-Tab (Host-Config-Liste).</summary>
    HostConfigRow
}

/// <summary>Ziel-Kontext eines Kontextmenue-Klicks: was wurde selektiert, wo laeuft der Klick.</summary>
public sealed record ContextMenuTarget(
    ContextMenuLocation Location,
    string HostName,
    string? ServiceDescription,
    object? OwnerWindow);

/// <summary>
/// Ein Plugin kann eine (oder mehrere) dieser Kontributionen als Service im DI
/// registrieren. Der Cockpit sammelt alle beim UI-Aufbau und blendet sie in die
/// jeweiligen Kontextmenues.
/// </summary>
public interface IContextMenuContribution
{
    /// <summary>Beschriftung des Menue-Eintrags.</summary>
    string Label { get; }

    /// <summary>Sortier-Reihenfolge. Cockpit-eigene Eintraege liegen bei 0-999,
    /// Plugin-Eintraege ab 1000 landen im "Plugins"-Bereich unten.</summary>
    int Order { get; }

    /// <summary>true, wenn der Eintrag an dieser Stelle angezeigt werden soll.
    /// Kann kontextabhaengig sein (z. B. nur wenn HostName nicht leer).</summary>
    bool SupportsLocation(ContextMenuLocation location);

    /// <summary>Wird bei Klick aufgerufen. Async, weil UI-Handler blocken sonst.</summary>
    Task ExecuteAsync(ContextMenuTarget target, CancellationToken ct = default);
}
