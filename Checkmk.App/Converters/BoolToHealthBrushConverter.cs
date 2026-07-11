using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Checkmk.App.Converters;

/// <summary>bool -> gruener / roter Brush fuer den Backend-Health-Punkt in der Statusleiste.</summary>
public sealed class BoolToHealthBrushConverter : IValueConverter
{
    public static readonly BoolToHealthBrushConverter Instance = new();

    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#66BB6A"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#EF5350"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? Green : Red;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
