using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Checkmk.App.Controls;

/// <summary>
/// Kroste-Standard-Titelleiste fuer Fenster mit WindowDecorations.BorderOnly.
/// Setzt via WindowDecorationProperties.ElementRole die Hit-Test-Rollen
/// (TitleBar/User), damit das OS Drag + Doppelklick nativ macht und Buttons/
/// interaktive Extras Klicks bekommen.
/// Zusaetzlich Drag/Doppelklick im Code-behind als Fallback fuer Plattformen
/// ohne native Caption-Behandlung.
/// </summary>
public partial class TitleBar : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<TitleBar, string?>(nameof(Title));

    /// <summary>Zusaetzliche Inhalte in der Titelleiste (z. B. Site-Umschalter).
    /// Werden rechts vom Titel, vor den Fensterbuttons angezeigt. Kinder bekommen
    /// automatisch ElementRole="User", damit Klicks bei ihnen ankommen.</summary>
    public static readonly StyledProperty<object?> ExtrasProperty =
        AvaloniaProperty.Register<TitleBar, object?>(nameof(Extras));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Extras
    {
        get => GetValue(ExtrasProperty);
        set => SetValue(ExtrasProperty, value);
    }

    public TitleBar()
    {
        InitializeComponent();

        MinButton.Click += (_, _) => { if (Host is { } w) w.WindowState = WindowState.Minimized; };
        MaxButton.Click += (_, _) => ToggleMaximize();
        CloseButton.Click += (_, _) => Host?.Close();

        Bar.PointerPressed += OnBarPointerPressed;
        Bar.DoubleTapped += (_, _) => ToggleMaximize();

        TryLoadAppIcon();
    }

    // Avalonia 12: VisualRoot ist NICHT mehr das Window, sondern der interne
    // TopLevelHost. "VisualRoot as Window" liefert null -> stille No-Ops.
    private Window? Host => TopLevel.GetTopLevel(this) as Window;

    private void OnBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Host?.BeginMoveDrag(e);
    }

    private void ToggleMaximize()
    {
        if (Host is { } w)
            w.WindowState = w.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
    }

    private void TryLoadAppIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Checkmk.App/Assets/app.png"));
            IconImage.Source = new Bitmap(stream);
            IconImage.IsVisible = true;
        }
        catch
        {
            // Ohne Icon lauffaehig bleiben.
        }
    }
}
