using System;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Checkmk.App.Controls;

/// <summary>
/// Custom-Chrome nach Avalonia-12-Konvention (Kroste-Standard, Referenz:
/// Klemmbrett-Scaffold). Alle vier Zeilen im Ctor sind Pflicht:
///
/// - <see cref="WindowDecorations.BorderOnly"/> statt None — sonst fehlen die
///   nativen Resize-Griffe und der Fensterschatten.
/// - <see cref="Window.ExtendClientAreaToDecorationsHint"/> + Height=-1 —
///   ohne beides liegt die OS-Caption-Hit-Test-Zone ueber der eigenen
///   Titelleiste und schluckt Klicks/Drag. Buttons ohne Funktion,
///   Fenster nicht verschiebbar.
/// - <see cref="Window.CanResize"/> true.
///
/// Fenster erben von dieser Klasse und packen im XAML eine
/// <see cref="TitleBar"/>-UserControl an den oberen Rand — dort sind die
/// Hit-Test-Rollen (WindowDecorationProperties.ElementRole) gesetzt, sodass
/// das OS Drag/Doppelklick nativ verarbeitet und Buttons/Extras Klicks
/// bekommen. Kein OnTitleBarPressed-Handler in den Fenstern noetig.
/// </summary>
public class ChromeWindow : Window
{
    protected ChromeWindow()
    {
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;
        CanResize = true;

        try
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Checkmk.App/Assets/app.png")));
        }
        catch
        {
            // Ohne Icon lauffaehig bleiben (falls Asset fehlt).
        }
    }
}
