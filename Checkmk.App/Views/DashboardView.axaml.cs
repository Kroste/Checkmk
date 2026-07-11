using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Checkmk.App.ViewModels;

namespace Checkmk.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => AvaloniaXamlLoader.Load(this);

    private void OnTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control c && c.DataContext is DashboardTile tile
            && DataContext is DashboardViewModel vm)
        {
            vm.OnTileClicked(tile);
        }
    }
}
