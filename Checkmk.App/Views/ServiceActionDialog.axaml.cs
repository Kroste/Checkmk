using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;
using Checkmk.App.ViewModels;

namespace Checkmk.App.Views;

public partial class ServiceActionDialog : ChromeWindow
{
    public ServiceActionDialog(ServiceActionDialogViewModel vm)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = vm;
        vm.RequestClose += (_, ok) => Close(ok);
    }

    // Parameterloser ctor nur fuer den XAML-Designer.
    public ServiceActionDialog() => AvaloniaXamlLoader.Load(this);
}
