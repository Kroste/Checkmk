using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Checkmk.App.Controls;
using Checkmk.App.ViewModels;
using Checkmk.PluginContracts;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace Checkmk.App.Views;

public partial class MainWindow : ChromeWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) => AddPluginTabs();
    }

    /// <summary>Fuegt Tabs, die von Plugins beigesteuert werden, rechts von den
    /// eingebauten Tabs (Status/Hosts/Dashboard) ein. Sortierung: Cockpit-Tabs
    /// liegen bei 0-999 (XAML-Reihenfolge), Plugin-Tabs ab Order 1000.</summary>
    private void AddPluginTabs()
    {
        var tabs = this.FindControl<TabControl>("MainTabs");
        if (tabs is null) return;

        // ITabContribution-Instanzen einzeln aufloesen und in try/catch iterieren —
        // wenn ein Plugin einen kaputten Ctor hat (z. B. IPluginContext als DI-
        // Dependency erwartet), wirft nur DIESES Plugin und die anderen laufen
        // trotzdem. Ohne den Try-Wrapper wuerde ein einziger Fehler in der
        // GetServices-Enumeration die ganze Kette killen -> Cockpit-Absturz.
        var contribs = new List<ITabContribution>();
        try
        {
            var descriptors = App.Services!.GetServices<ITabContribution>();
            foreach (var c in descriptors)
            {
                try { contribs.Add(c); }
                catch (Exception ex) { Log.Warn(ex, "Plugin-Tab-Instanz konnte nicht aufgeloest werden."); }
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Plugin-Tab-Enumeration fehlgeschlagen — kein Plugin-Tab wird geladen.");
            return;
        }
        contribs.Sort((a, b) => a.Order.CompareTo(b.Order));

        foreach (var contrib in contribs)
        {
            try
            {
                if (contrib.CreateView() is not Control view)
                {
                    Log.Warn("Plugin-Tab '{Header}' hat kein Avalonia-Control als View geliefert — uebersprungen.",
                        contrib.Header);
                    continue;
                }
                tabs.Items.Add(new TabItem { Header = contrib.Header, Content = view });
                Log.Info("Plugin-Tab hinzugefuegt: {Header} (Order {Order})", contrib.Header, contrib.Order);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Plugin-Tab '{Header}' konnte nicht erstellt werden.", contrib.Header);
            }
        }
    }

    /// <summary>Wird von aussen (App.axaml.cs) genutzt, um beim Dashboard-Klick
    /// zurueck in den Status-Tab zu springen.</summary>
    public void SelectMainTab(int index)
    {
        var tabs = this.FindControl<TabControl>("MainTabs");
        if (tabs is not null) tabs.SelectedIndex = index;
    }

    /// <summary>
    /// Alltags-Hotkeys. Tunnel-Routing damit sie die aktive TextBox nicht ueberschreiben —
    /// wir prueft ExplicitHandled-Flag und lassen die TextBox ihre eigene Tastaturbelegung
    /// behalten (nur Esc wird auch aus der Textbox verwendet, weil wir es zum Leeren nutzen).
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var focus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var focusIsTextInput = focus is TextBox;

        switch (e.Key)
        {
            case Key.F5:
                _ = vm.Status.RefreshCommand.ExecuteAsync(null);
                e.Handled = true;
                return;

            case Key.F when ctrl:
                // Fokus in die Freitext-Filter-Textbox des Status-Tabs.
                var box = this.FindDescendantOfType<TextBox>("FilterTextBox");
                if (box is not null)
                {
                    box.Focus();
                    box.SelectAll();
                    e.Handled = true;
                }
                return;

            case Key.Escape when focusIsTextInput && focus is TextBox tb
                              && tb.Name == "FilterTextBox":
                // Nur die Freitext-Filter-Box leeren; andere TextBoxen (Kommentar-Dialog etc.)
                // sollen ihr Escape-Verhalten behalten.
                tb.Text = "";
                e.Handled = true;
                return;
        }

        // Modifier-Hotkeys (Ctrl+K/D/A) sollen nicht in TextBoxen greifen.
        if (focusIsTextInput) return;

        switch (e.Key)
        {
            case Key.K when ctrl:
                RequestServiceAction(ServiceHotkeyAction.Comment);
                e.Handled = true;
                return;
            case Key.D when ctrl:
                RequestServiceAction(ServiceHotkeyAction.Downtime);
                e.Handled = true;
                return;
            case Key.A when ctrl:
                RequestServiceAction(ServiceHotkeyAction.Acknowledge);
                e.Handled = true;
                return;
        }
    }

    private void RequestServiceAction(ServiceHotkeyAction action)
    {
        // Delegiert an den StatusView-Code-Behind — der weiss, wie Kommentar-/
        // Downtime-/Ack-Dialog auf den markierten Services aufgeht.
        var status = this.FindDescendantOfType<StatusView>();
        status?.TriggerHotkeyAction(action);
    }
}

public enum ServiceHotkeyAction
{
    Comment,
    Downtime,
    Acknowledge
}

/// <summary>Kleine Findhelper — Avalonia hat kein direktes FindName ueber die Tree-Hierarchie.</summary>
internal static class ControlTreeExtensions
{
    public static T? FindDescendantOfType<T>(this Control root, string? name = null) where T : Control
    {
        foreach (var child in root.GetVisualDescendants())
        {
            if (child is T typed && (name is null || typed.Name == name))
                return typed;
        }
        return null;
    }
}
