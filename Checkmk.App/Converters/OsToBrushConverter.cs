using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Checkmk.App.Services;

namespace Checkmk.App.Converters;

/// <summary>OS-Familie -> Badge-Farbe (Windows blau, Linux amber, sonst grau).</summary>
public sealed class OsToBrushConverter : IValueConverter
{
    private static readonly IBrush Windows = new SolidColorBrush(Color.Parse("#0E639C"));
    private static readonly IBrush Linux = new SolidColorBrush(Color.Parse("#EF6C00"));
    private static readonly IBrush Unknown = new SolidColorBrush(Color.Parse("#555555"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            OsFamily.Windows => Windows,
            OsFamily.Linux => Linux,
            _ => Unknown
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
