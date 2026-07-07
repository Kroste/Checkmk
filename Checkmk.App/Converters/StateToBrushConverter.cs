using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Checkmk.Core.Models;

namespace Checkmk.App.Converters;

/// <summary>Mappt Host-/Service-State auf eine Ampelfarbe.</summary>
public sealed class StateToBrushConverter : IValueConverter
{
    public static readonly StateToBrushConverter Instance = new();

    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#2E7D32"));
    private static readonly IBrush Yellow = new SolidColorBrush(Color.Parse("#F9A825"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#C62828"));
    private static readonly IBrush Orange = new SolidColorBrush(Color.Parse("#EF6C00"));
    private static readonly IBrush Grey = new SolidColorBrush(Color.Parse("#616161"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            ServiceState s => s switch
            {
                ServiceState.Ok => Green,
                ServiceState.Warning => Yellow,
                ServiceState.Critical => Red,
                _ => Grey
            },
            HostState h => h switch
            {
                HostState.Up => Green,
                HostState.Down => Red,
                HostState.Unreachable => Orange,
                _ => Grey
            },
            _ => Grey
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
