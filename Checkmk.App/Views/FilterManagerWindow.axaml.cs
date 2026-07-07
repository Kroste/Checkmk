using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;
using Checkmk.App.Services;
using Checkmk.App.ViewModels;

namespace Checkmk.App.Views;

public partial class FilterManagerWindow : ChromeWindow
{
    public FilterManagerWindow(HostFilterCollection filters)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = new FilterManagerViewModel(filters);
    }

    // Parameterloser ctor nur fuer den XAML-Designer.
    public FilterManagerWindow() => AvaloniaXamlLoader.Load(this);

    private void OnDismissClick(object? sender, RoutedEventArgs e) => Close();
}
