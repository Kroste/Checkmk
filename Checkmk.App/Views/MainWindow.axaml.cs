using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Checkmk.App.Controls;
using Checkmk.App.ViewModels;

namespace Checkmk.App.Views;

public partial class MainWindow : ChromeWindow
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
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
