using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;

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
        Bar.DoubleTapped += OnBarDoubleTapped;

        TryLoadAppIcon();
    }

    // Avalonia 12: VisualRoot ist NICHT mehr das Window, sondern der interne
    // TopLevelHost. "VisualRoot as Window" liefert null -> stille No-Ops.
    private Window? Host => TopLevel.GetTopLevel(this) as Window;

    private void OnBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Der Klick kam per Bubbling aus einem interaktiven Kind (Site-ComboBox,
        // Buttons im Extras-Slot). Dann gehoert er dem Control, nicht dem Fenster.
        if (LandedOnInteractiveChild(e.Source))
            return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Host?.BeginMoveDrag(e);
    }

    private void OnBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (LandedOnInteractiveChild(e.Source))
            return;

        ToggleMaximize();
    }

    /// <summary>
    /// Laeuft vom Ereignis-Ursprung den Visual-Tree hoch bis zur Titelleisten-Border
    /// und meldet true, wenn unterwegs ein interaktives Control liegt.
    ///
    /// WARUM: PointerPressed bubbelt. Die ComboBox im Extras-Slot markiert den
    /// Press nicht als handled (anders als Button, der ihn abfaengt und den
    /// Pointer captured) — ohne diesen Guard startet <see cref="Window.BeginMoveDrag"/>
    /// einen Fenster-Drag, der Pointer wandert zum OS und die ComboBox sieht nie
    /// ein PointerReleased. Symptom: Dropdown laesst sich gar nicht oeffnen, nur
    /// der ToolTip erscheint. Genau dieser Bug war schon einmal in ChromeWindow
    /// gefixt (be95724) und ging beim TitleBar-Refactor (23160d8) verloren.
    /// </summary>
    private bool LandedOnInteractiveChild(object? source)
    {
        for (var v = source as Visual; v is not null; v = v.GetVisualParent())
        {
            // Die Titelleiste selbst (und alles darueber) ist Drag-Flaeche.
            if (ReferenceEquals(v, Bar))
                return false;

            // Button deckt ToggleButton/CheckBox/RadioButton/RepeatButton mit ab.
            if (v is Button or ComboBox or TextBox or Slider or ListBox or MenuItem)
                return true;

            // Auffangnetz fuer Controls, die oben nicht gelistet sind: alles,
            // was den Fokus annehmen kann, will den Klick selbst verarbeiten.
            if (v is InputElement { Focusable: true })
                return true;
        }

        // Ursprung liegt ausserhalb der Titelleiste (z. B. in einem Popup-Root).
        return true;
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
