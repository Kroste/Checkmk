using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Checkmk.App.Services;

using Path = Avalonia.Controls.Shapes.Path;

namespace Checkmk.App.Converters;

/// <summary>
/// OS-Familie -> Pictogramm-Control: generisches Fenster (Windows), Tux-artiger
/// Pinguin (Linux), "?" (unbekannt). Vektor, keine Bild-Assets, kein Marken-Logo.
/// </summary>
public sealed class OsToIconConverter : IValueConverter
{
    private static readonly IBrush WindowBlue = new SolidColorBrush(Color.Parse("#4FA3E3"));
    private static readonly IBrush Black = new SolidColorBrush(Color.Parse("#1E1E1E"));
    private static readonly IBrush White = Brushes.White;
    private static readonly IBrush Orange = new SolidColorBrush(Color.Parse("#F4A81D"));
    private static readonly IBrush Grey = new SolidColorBrush(Color.Parse("#888888"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            OsFamily.Windows => WindowIcon(),
            OsFamily.Linux => PenguinIcon(),
            _ => UnknownIcon()
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Control WindowIcon() => new Path
    {
        Width = 16,
        Height = 16,
        Stretch = Stretch.Uniform,
        Stroke = WindowBlue,
        StrokeThickness = 1.4,
        StrokeJoin = PenLineJoin.Round,
        // Rahmen + Titelleiste + Fensterkreuz (Panes)
        Data = Geometry.Parse("M2,3 H14 V13 H2 Z M2,6 H14 M8,6 V13 M2,9.5 H14")
    };

    private static Control PenguinIcon()
    {
        var canvas = new Canvas { Width = 16, Height = 16 };

        var body = new Path
        {
            Fill = Black,
            Data = Geometry.Parse(
                "M8,1 C5.4,1 4.5,3.2 4.5,5 C3,6.2 2.5,8.8 3.2,11.2 " +
                "C3.8,13.1 5.7,14.3 8,14.3 C10.3,14.3 12.2,13.1 12.8,11.2 " +
                "C13.5,8.8 13,6.2 11.5,5 C11.5,3.2 10.6,1 8,1 Z")
        };
        var belly = new Path
        {
            Fill = White,
            Data = Geometry.Parse(
                "M8,4 C6.6,4 5.9,5.6 5.9,7.6 C5.9,10 6.8,12.4 8,12.4 " +
                "C9.2,12.4 10.1,10 10.1,7.6 C10.1,5.6 9.4,4 8,4 Z")
        };
        var beak = new Path { Fill = Orange, Data = Geometry.Parse("M7,4.1 H9 L8,5.5 Z") };
        var footL = new Path { Fill = Orange, Data = Geometry.Parse("M6,13.8 L4.7,15.4 L7,14.9 Z") };
        var footR = new Path { Fill = Orange, Data = Geometry.Parse("M10,13.8 L11.3,15.4 L9,14.9 Z") };
        var eyeL = new Ellipse { Width = 1.3, Height = 1.3, Fill = Black };
        var eyeR = new Ellipse { Width = 1.3, Height = 1.3, Fill = Black };
        Canvas.SetLeft(eyeL, 6.4); Canvas.SetTop(eyeL, 3.0);
        Canvas.SetLeft(eyeR, 8.3); Canvas.SetTop(eyeR, 3.0);

        canvas.Children.Add(body);
        canvas.Children.Add(belly);
        canvas.Children.Add(beak);
        canvas.Children.Add(footL);
        canvas.Children.Add(footR);
        canvas.Children.Add(eyeL);
        canvas.Children.Add(eyeR);
        return canvas;
    }

    private static Control UnknownIcon() => new TextBlock
    {
        Text = "?",
        FontWeight = FontWeight.Bold,
        FontSize = 14,
        Foreground = Grey,
        Width = 16,
        Height = 16,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
}
