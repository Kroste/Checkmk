using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Checkmk.App.Converters;

/// <summary>
/// Faerbung der Age-Spalte nach Alter des Statuswechsels:
/// - &lt;10 min: hellrot (aufgetauchte Frisch-Probleme; direkt bearbeiten)
/// - &lt;1 h  : orange
/// - &lt;8 h  : normal (heller Text)
/// - &gt;=8 h : grau (Alt-Bestand)
/// Auf UTC-Basis, damit Zeitzone egal ist.
/// </summary>
public sealed class AgeToBrushConverter : IValueConverter
{
    public static readonly AgeToBrushConverter Instance = new();

    private static readonly IBrush FreshBrush = new SolidColorBrush(Color.Parse("#FF7373"));
    private static readonly IBrush WarmBrush = new SolidColorBrush(Color.Parse("#F9A825"));
    private static readonly IBrush Neutral = new SolidColorBrush(Color.Parse("#DDDDDD"));
    private static readonly IBrush StaleBrush = new SolidColorBrush(Color.Parse("#888888"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset lastChange)
            return Neutral;
        if (lastChange <= DateTimeOffset.FromUnixTimeSeconds(0))
            return StaleBrush;

        var age = DateTimeOffset.UtcNow - lastChange;
        if (age < TimeSpan.FromMinutes(10)) return FreshBrush;
        if (age < TimeSpan.FromHours(1)) return WarmBrush;
        if (age < TimeSpan.FromHours(8)) return Neutral;
        return StaleBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
