using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Checkmk.App.Views;

public partial class ConfigView : UserControl
{
    public ConfigView() => AvaloniaXamlLoader.Load(this);
}
