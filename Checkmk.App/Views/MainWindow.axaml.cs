using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;

namespace Checkmk.App.Views;

public partial class MainWindow : ChromeWindow
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
