using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Checkmk.App.Controls;

/// <summary>
/// Basisfenster mit randlosem Chrome (SystemDecorations.BorderOnly) und
/// eigener Titelleiste. Abgeleitete Fenster binden ihre Titelleisten-Border
/// per PointerPressed an OnTitleBarPressed und die Buttons an die
/// Min/Max/Close-Handler.
/// </summary>
public class ChromeWindow : Window
{
    public ChromeWindow()
    {
        WindowDecorations = WindowDecorations.BorderOnly;
        CanResize = true;
        ExtendClientAreaToDecorationsHint = false;
        TransparencyLevelHint = [WindowTransparencyLevel.None];
        Background = null; // Theme-Hintergrund kommt aus dem Root-Panel
    }

    protected void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        // Wenn der Klick auf einem interaktiven Control landete (Button, ComboBox,
        // TextBox, ...), ueberlassen wir es dem. Sonst wuerde der Drag den ersten
        // Klick schlucken und die ComboBox brauchte einen zweiten zum Aufklappen.
        if (e.Source is Visual v && IsInteractiveDescendant(v))
            return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2)
                ToggleMaximize();
            else
                BeginMoveDrag(e);
        }
    }

    private static bool IsInteractiveDescendant(Visual source)
    {
        for (var v = source; v is not null; v = v.GetVisualParent())
        {
            if (v is Window) return false;
            if (v is Button or ComboBox or TextBox or CheckBox or RadioButton or ToggleButton)
                return true;
        }
        return false;
    }

    protected void OnMinimizeClick(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    protected void OnMaximizeClick(object? sender, RoutedEventArgs e)
        => ToggleMaximize();

    protected void OnCloseClick(object? sender, RoutedEventArgs e)
        => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
}
