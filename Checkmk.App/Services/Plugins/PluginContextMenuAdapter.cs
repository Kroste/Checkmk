using Avalonia.Controls;
using Checkmk.PluginContracts;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace Checkmk.App.Services.Plugins;

/// <summary>
/// Haengt an ein <see cref="ContextMenu"/> die von Plugins beigesteuerten
/// <see cref="IContextMenuContribution"/>-Eintraege an. Aufrufen von
/// jedem Cockpit-Kontextmenue in dem Plugins mitmischen sollen (Status-Grid,
/// Status-TreeView, Hosts-Grid).
/// </summary>
public static class PluginContextMenuAdapter
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Registriert den Extension-Handler auf <paramref name="menu"/>.Opened —
    /// bei jedem Aufklappen wird die Plugin-Liste neu geholt und die Eintraege
    /// hinter einem Separator angehaengt. Vorherige Plugin-Eintraege werden
    /// vorher entfernt, damit's beim mehrfachen Aufklappen nicht duplicated.
    /// </summary>
    /// <param name="menu">Das Kontextmenue (aus dem XAML).</param>
    /// <param name="location">Wo dieses Menue wohnt — Plugins filtern via
    /// <see cref="IContextMenuContribution.SupportsLocation"/>.</param>
    /// <param name="targetProvider">Liefert zum Klickzeitpunkt den aktuellen
    /// Target-Kontext (welcher Host/Service ist ausgewaehlt). Kann null
    /// zurueckgeben, wenn nichts selektiert ist — dann werden keine Plugin-
    /// Eintraege angezeigt.</param>
    public static void Attach(
        ContextMenu menu,
        ContextMenuLocation location,
        Func<ContextMenuTarget?> targetProvider)
    {
        menu.Opened += (_, _) => RefreshItems(menu, location, targetProvider);
    }

    private static void RefreshItems(
        ContextMenu menu,
        ContextMenuLocation location,
        Func<ContextMenuTarget?> targetProvider)
    {
        // Frueher hinzugefuegte Plugin-Eintraege wieder rausnehmen (inklusive
        // des Separators davor). Markierung via Tag = "plugin".
        for (var i = menu.Items.Count - 1; i >= 0; i--)
        {
            if (menu.Items[i] is Control ctl && ReferenceEquals(ctl.Tag, PluginTag))
                menu.Items.RemoveAt(i);
        }

        var target = targetProvider();
        if (target is null) return;

        var contribs = App.Services!.GetServices<IContextMenuContribution>()
            .Where(c => c.SupportsLocation(location))
            .OrderBy(c => c.Order)
            .ToList();
        if (contribs.Count == 0) return;

        var separator = new Separator { Tag = PluginTag };
        menu.Items.Add(separator);

        foreach (var contrib in contribs)
        {
            var item = new MenuItem { Header = contrib.Label, Tag = PluginTag };
            // lokale Kopie, weil Closure-Semantik
            var localContrib = contrib;
            var localTarget = target;
            item.Click += async (_, _) =>
            {
                try
                {
                    await localContrib.ExecuteAsync(localTarget);
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Plugin-Kontextmenue-Eintrag '{Label}' warf.", localContrib.Label);
                }
            };
            menu.Items.Add(item);
        }
    }

    private static readonly object PluginTag = new();
}
