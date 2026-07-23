using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Checkmk.App.Services;

/// <summary>
/// Baut Tray-Icons zur Laufzeit: das App-Icon (Assets/app.png) als Basis,
/// dazu ein kleiner Status-Dot unten rechts in Ampelfarbe. Damit bleibt das
/// Cockpit im Tray wiedererkennbar (statt eines nackten Farbpunktes) und
/// der Status ist trotzdem auf einen Blick sichtbar.
/// </summary>
public static class TrayIconFactory
{
    // Ampelfarben aus StateToBrushConverter — bewusst dieselben Werte, damit
    // Tray-Dot und Statusleisten-Ampel in der App konsistent aussehen.
    public static readonly Color OkGreen = Color.Parse("#2E7D32");
    public static readonly Color WarnYellow = Color.Parse("#F9A825");
    public static readonly Color CritRed = Color.Parse("#C62828");
    public static readonly Color UnknownGrey = Color.Parse("#616161");

    private const int IconSize = 32;
    private const int DotSize = 14;
    private const int DotBorder = 2;

    private static Bitmap? _appIcon;

    /// <summary>Cache-basiertes Laden des App-Icons als Bitmap.</summary>
    private static Bitmap AppIcon() =>
        _appIcon ??= new Bitmap(AssetLoader.Open(new Uri("avares://Checkmk.App/Assets/app.png")));

    /// <summary>Rendert das App-Icon mit einem farbigen Status-Dot in der unteren
    /// rechten Ecke und gibt es als <see cref="WindowIcon"/> zurueck (fuer
    /// <c>TrayIcon.Icon</c>).</summary>
    public static WindowIcon Create(Color dot)
    {
        var app = AppIcon();
        var rtb = new RenderTargetBitmap(new PixelSize(IconSize, IconSize), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            // App-Icon volle Flaeche
            ctx.DrawImage(app, new Rect(0, 0, IconSize, IconSize));

            // Status-Dot unten rechts, weisser Rand fuer Kontrast gegen dunkles App-Icon
            var outer = new Rect(IconSize - DotSize, IconSize - DotSize, DotSize, DotSize);
            ctx.DrawEllipse(new SolidColorBrush(Colors.White), null, outer);
            var inner = outer.Deflate(DotBorder);
            ctx.DrawEllipse(new SolidColorBrush(dot), null, inner);
        }

        // WindowIcon nimmt ein Bitmap direkt — kein Umweg ueber PNG-Kodierung
        // (Avalonia 12: Bitmap.Save(Stream) ist deprecated).
        return new WindowIcon(rtb);
    }
}
