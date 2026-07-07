using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App;
using Checkmk.App.Services;
using Checkmk.App.ViewModels;
using Checkmk.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Checkmk.App.Views;

public partial class StatusView : UserControl
{
    public StatusView() => AvaloniaXamlLoader.Load(this);

    private async void OnAcknowledgeClick(object? sender, RoutedEventArgs e)
        => await ShowActionAsync(ServiceActionMode.Acknowledge);

    private async void OnDowntimeClick(object? sender, RoutedEventArgs e)
        => await ShowActionAsync(ServiceActionMode.Downtime);

    private async void OnCommentClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusViewModel vm || vm.SelectedService is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var svc = vm.SelectedService;
        var dialog = new CommentInputDialog($"{svc.HostName} / {svc.Description}");
        var result = await dialog.ShowDialog<CommentInputResult?>(owner);
        if (result is null) return;

        await vm.PerformAddCommentAsync(result.Comment, result.Persistent);
    }

    private async void OnManageFiltersClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        await new FilterManagerWindow(vm.Filters).ShowDialog(owner);
    }

    private void OnServiceDoubleTapped(object? sender, TappedEventArgs e) => OpenHostDetails();
    private void OnOpenHostDetailsClick(object? sender, RoutedEventArgs e) => OpenHostDetails();

    private void OpenHostDetails()
    {
        if (DataContext is not StatusViewModel vm || vm.SelectedService is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var clients = App.Services!.GetRequiredService<ICheckmkClientProvider>();
        var detailVm = new HostDetailViewModel(clients, vm.SelectedService.HostName);
        new HostDetailWindow(detailVm).Show(owner);
    }

    private async Task ShowActionAsync(ServiceActionMode mode)
    {
        if (DataContext is not StatusViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var selected = GetSelectedServices();
        if (selected.Count == 0) return;

        ServiceActionDialogViewModel dialogVm;
        if (selected.Count == 1)
        {
            var svc = selected[0];
            dialogVm = new ServiceActionDialogViewModel(mode, svc.HostName, svc.Description);
        }
        else
        {
            var hosts = selected.Select(s => s.HostName).Distinct().Count();
            var label = hosts == 1
                ? $"{selected.Count} Services auf {selected[0].HostName}"
                : $"{selected.Count} Services auf {hosts} Hosts";
            dialogVm = new ServiceActionDialogViewModel(mode, label);
        }

        var dialog = new ServiceActionDialog(dialogVm);
        var confirmed = await dialog.ShowDialog<bool>(owner);
        if (!confirmed) return;

        if (mode == ServiceActionMode.Acknowledge)
        {
            if (selected.Count == 1)
                await vm.PerformAcknowledgeAsync(dialogVm.Comment);
            else
                await vm.PerformBulkAcknowledgeAsync(selected, dialogVm.Comment);
        }
        else
        {
            var (start, end) = dialogVm.Window();
            if (selected.Count == 1)
                await vm.PerformDowntimeAsync(dialogVm.Comment, start, end);
            else
                await vm.PerformBulkDowntimeAsync(selected, dialogVm.Comment, start, end);
        }
    }

    private IReadOnlyList<ServiceStatus> GetSelectedServices()
    {
        var grid = this.FindControl<DataGrid>("ServiceGrid");
        if (grid is null) return [];
        return grid.SelectedItems.OfType<ServiceStatus>().ToList();
    }
}
