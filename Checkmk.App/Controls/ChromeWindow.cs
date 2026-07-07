using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

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
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2)
                ToggleMaximize();
            else
                BeginMoveDrag(e);
        }
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
