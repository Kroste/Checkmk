using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
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
        if (DataContext is not StatusViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var svc = GetTargetService();
        if (svc is null) return;
        vm.SelectedService = svc; // PerformAddCommentAsync arbeitet auf SelectedService

        var dialog = new CommentInputDialog($"{svc.HostName} / {svc.Description}");
        var result = await dialog.ShowDialog<CommentInputResult?>(owner);
        if (result is null) return;

        await vm.PerformAddCommentAsync(result.Comment, result.Persistent);
    }

    private async void OnUpdateClientClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var host = GetTargetHostName();
        if (host is null) return;

        if (!System.OperatingSystem.IsWindows())
        {
            if (DataContext is StatusViewModel svm)
                svm.StatusMessage = "Client-Aktualisierung ist nur unter Windows verfuegbar.";
            return;
        }

        var creds = await new CredentialDialog(host).ShowDialog<CredentialResult?>(owner);
        if (creds is null) return;

        var settings = App.Services!.GetRequiredService<IConnectionSettingsStore>().Load();
        await new AgentUpdateWindow(host, creds.User, creds.Password,
            settings.AgentShare, settings.AgentUpdateScript).ShowDialog(owner);
    }

    private void OnOpenInWebClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusViewModel vm || vm.SelectedService is null) return;
        var svc = vm.SelectedService;
        var web = App.Services!.GetRequiredService<CheckmkWebLinker>();
        web.OpenServiceView(svc.HostName, svc.Description);
    }

    private void OnRdpClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusViewModel vm || vm.SelectedService is null) return;
        RemoteTools.StartRdp(vm.SelectedService.HostName);
    }

    private void OnPingClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusViewModel vm || vm.SelectedService is null) return;
        RemoteTools.StartPing(vm.SelectedService.HostName);
    }

    private async void OnManageFiltersClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        await new FilterManagerWindow(vm.Filters).ShowDialog(owner);
    }

    private async void OnExportCsvClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not { } top) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Status als CSV exportieren",
            SuggestedFileName = $"checkmk-status-{System.DateTime.Now:yyyyMMdd-HHmm}.csv",
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        });
        if (file is null) return;

        try
        {
            var bytes = CsvExporter.ToCsvBytes(vm.Services);
            await System.IO.File.WriteAllBytesAsync(file.Path.LocalPath, bytes);
            vm.StatusMessage = $"{vm.Services.Count} Zeilen exportiert: {file.Name}";
        }
        catch (System.Exception ex)
        {
            vm.StatusMessage = $"CSV-Export fehlgeschlagen: {ex.Message}";
        }
    }

    private void OnServiceDoubleTapped(object? sender, TappedEventArgs e) => OpenHostDetails();
    private void OnOpenHostDetailsClick(object? sender, RoutedEventArgs e) => OpenHostDetails();

    private void OpenHostDetails()
    {
        if (DataContext is not StatusViewModel) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var host = GetTargetHostName();
        if (host is null) return;

        var clients = App.Services!.GetRequiredService<ICheckmkClientProvider>();
        var detailVm = new HostDetailViewModel(clients, host);
        new HostDetailWindow(detailVm).Show(owner);
    }

    private async Task ShowActionAsync(ServiceActionMode mode)
    {
        if (DataContext is not StatusViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var selected = GetTargetServices();
        if (selected.Count == 0) return;

        ServiceActionDialogViewModel dialogVm;
        if (selected.Count == 1)
        {
            var svc = selected[0];
            vm.SelectedService = svc; // damit die Single-Service-Methoden das richtige Ziel treffen
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

    // --- Ziel-Aufloesung: Tabelle (Grid-Auswahl) oder Baum (SelectedTreeItem) ---

    private IReadOnlyList<ServiceStatus> GetTargetServices()
    {
        if (DataContext is not StatusViewModel vm) return [];
        if (!vm.TreeView) return GetSelectedServices();

        return vm.SelectedTreeItem switch
        {
            ServiceStatus s => [s],
            HostNodeViewModel h => h.Services.ToList(),
            _ => []
        };
    }

    private ServiceStatus? GetTargetService()
    {
        if (DataContext is not StatusViewModel vm) return null;
        if (!vm.TreeView) return vm.SelectedService;

        return vm.SelectedTreeItem switch
        {
            ServiceStatus s => s,
            HostNodeViewModel h => h.Services.FirstOrDefault(),
            _ => null
        };
    }

    private string? GetTargetHostName()
    {
        if (DataContext is not StatusViewModel vm) return null;
        if (!vm.TreeView) return vm.SelectedService?.HostName;

        return vm.SelectedTreeItem switch
        {
            ServiceStatus s => s.HostName,
            HostNodeViewModel h => h.HostName,
            _ => null
        };
    }
}
