using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;
using Checkmk.App.ViewModels;

namespace Checkmk.App.Views;

public partial class SettingsWindow : ChromeWindow
{
    public SettingsWindow(SettingsViewModel vm)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = vm;
        vm.RequestClose += (_, _) => Close();
    }

    // Parameterloser ctor nur fuer den XAML-Designer.
    public SettingsWindow() => AvaloniaXamlLoader.Load(this);
}
