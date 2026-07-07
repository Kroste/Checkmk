using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Checkmk.App.Views;

public partial class StatusView : UserControl
{
    public StatusView() => AvaloniaXamlLoader.Load(this);
}
