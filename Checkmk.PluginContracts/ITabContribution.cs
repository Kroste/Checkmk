namespace Checkmk.PluginContracts;

/// <summary>
/// Ein Plugin kann einen (oder mehrere) neue Tabs neben Status/Hosts/Dashboard
/// beisteuern. Der Cockpit sammelt alle beim UI-Aufbau und fuegt sie in das
/// Haupt-TabControl ein.
/// </summary>
public interface ITabContribution
{
    /// <summary>Beschriftung im Reiter.</summary>
    string Header { get; }

    /// <summary>Sortier-Reihenfolge. Cockpit-eigene Tabs liegen bei 0-999,
    /// Plugin-Tabs ab 1000 landen rechts.</summary>
    int Order { get; }

    /// <summary>
    /// Erzeugt die View fuer den Tab. Muss ein Avalonia-<c>Control</c> sein
    /// (Contracts referenzieren Avalonia bewusst nicht, um die Plugin-
    /// Abhaengigkeitsgrafik minimal zu halten — Cockpit castet auf Control).
    /// Wird beim ersten Sichtbarwerden des Tabs aufgerufen.
    /// </summary>
    object CreateView();
}
