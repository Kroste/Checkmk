using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.ViewModels;

namespace Checkmk.App.Views;

public partial class StatusView : UserControl
{
    public StatusView() => AvaloniaXamlLoader.Load(this);

    private async void OnAcknowledgeClick(object? sender, RoutedEventArgs e)
        => await ShowActionAsync(ServiceActionMode.Acknowledge);

    private async void OnDowntimeClick(object? sender, RoutedEventArgs e)
        => await ShowActionAsync(ServiceActionMode.Downtime);

    private async void OnManageFiltersClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        await new FilterManagerWindow(vm.Filters).ShowDialog(owner);
    }

    private async Task ShowActionAsync(ServiceActionMode mode)
    {
        if (DataContext is not StatusViewModel vm || vm.SelectedService is null)
            return;
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var svc = vm.SelectedService;
        var dialogVm = new ServiceActionDialogViewModel(mode, svc.HostName, svc.Description);
        var dialog = new ServiceActionDialog(dialogVm);

        var confirmed = await dialog.ShowDialog<bool>(owner);
        if (!confirmed)
            return;

        if (mode == ServiceActionMode.Acknowledge)
        {
            await vm.PerformAcknowledgeAsync(dialogVm.Comment);
        }
        else
        {
            var (start, end) = dialogVm.Window();
            await vm.PerformDowntimeAsync(dialogVm.Comment, start, end);
        }
    }
}
