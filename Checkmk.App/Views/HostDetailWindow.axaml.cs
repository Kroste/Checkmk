using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;
using Checkmk.App.ViewModels;

namespace Checkmk.App.Views;

public partial class HostDetailWindow : ChromeWindow
{
    private readonly HostDetailViewModel? _vm;

    public HostDetailWindow(HostDetailViewModel vm)
    {
        AvaloniaXamlLoader.Load(this);
        _vm = vm;
        DataContext = vm;
        // Initial-Load, sobald das Fenster steht.
        Opened += async (_, _) => await vm.RefreshAsync();
    }

    // Parameterloser ctor nur fuer den XAML-Designer.
    public HostDetailWindow() => AvaloniaXamlLoader.Load(this);

    private async void OnServiceAckClick(object? sender, RoutedEventArgs e)
        => await ShowServiceActionAsync(ServiceActionMode.Acknowledge);

    private async void OnServiceDowntimeClick(object? sender, RoutedEventArgs e)
        => await ShowServiceActionAsync(ServiceActionMode.Downtime);

    private async void OnHostAckClick(object? sender, RoutedEventArgs e)
        => await ShowHostActionAsync(ServiceActionMode.Acknowledge);

    private async void OnHostDowntimeClick(object? sender, RoutedEventArgs e)
        => await ShowHostActionAsync(ServiceActionMode.Downtime);

    private async System.Threading.Tasks.Task ShowServiceActionAsync(ServiceActionMode mode)
    {
        if (_vm is null || _vm.SelectedService is null) return;

        var svc = _vm.SelectedService;
        var dialogVm = new ServiceActionDialogViewModel(mode, svc.HostName, svc.Description);
        var dialog = new ServiceActionDialog(dialogVm);
        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed) return;

        if (mode == ServiceActionMode.Acknowledge)
        {
            await _vm.PerformServiceAcknowledgeAsync(dialogVm.Comment);
        }
        else
        {
            var (start, end) = dialogVm.Window();
            await _vm.PerformServiceDowntimeAsync(dialogVm.Comment, start, end);
        }
    }

    private async System.Threading.Tasks.Task ShowHostActionAsync(ServiceActionMode mode)
    {
        if (_vm is null) return;

        // Fuer Host-Aktionen zeigen wir denselben Dialog, aber ohne Service-Description im Target.
        var dialogVm = new ServiceActionDialogViewModel(mode, _vm.HostName, serviceDescription: null);
        var dialog = new ServiceActionDialog(dialogVm);
        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed) return;

        if (mode == ServiceActionMode.Acknowledge)
        {
            await _vm.PerformHostAcknowledgeAsync(dialogVm.Comment);
        }
        else
        {
            var (start, end) = dialogVm.Window();
            await _vm.PerformHostDowntimeAsync(dialogVm.Comment, start, end);
        }
    }
}
