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
    public StatusView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is StatusViewModel vm)
                vm.NewCriticalAppeared += OnNewCritical;
        };
    }

    private void OnNewCritical(ServiceStatus svc)
    {
        var grid = this.FindControl<DataGrid>("ServiceGrid");
        if (grid is null) return;
        // Auto-Scroll + Selection als Highlight. ScrollIntoView greift auf die
        // Row-Container-Ebene, danach markiert SelectedItem die Zeile im Standard-
        // Selection-Farbschema — sichtbar ohne Custom-Style.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            grid.ScrollIntoView(svc, null);
            grid.SelectedItem = svc;
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

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

    /// <summary>Wird aus dem MainWindow-Hotkey-Handler aufgerufen (Ctrl+K/D/A).</summary>
    internal async void TriggerHotkeyAction(ServiceHotkeyAction action)
    {
        var mode = action switch
        {
            ServiceHotkeyAction.Acknowledge => ServiceActionMode.Acknowledge,
            ServiceHotkeyAction.Downtime => ServiceActionMode.Downtime,
            _ => (ServiceActionMode?)null
        };
        if (mode is not null)
            await ShowActionAsync(mode.Value);
        else if (action == ServiceHotkeyAction.Comment)
            OnCommentClick(null, new RoutedEventArgs());
    }

    private async void OnHostSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusViewModel vm || vm.SelectedService is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dialog = new HostSettingsDialog(
            vm.SelectedService.HostName,
            App.Services!.GetRequiredService<IHostDomainStore>(),
            App.Services!.GetRequiredService<ISshCredentialStore>());
        await dialog.ShowDialog<bool>(owner);
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
        App.Services!.GetRequiredService<RemoteTools>().StartRdp(vm.SelectedService.HostName);
    }

    private void OnSshClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusViewModel vm || vm.SelectedService is null) return;
        // Kein User-Argument in Commit B — wird in Commit C aus SshCredentialStore geholt.
        App.Services!.GetRequiredService<RemoteTools>().StartSsh(vm.SelectedService.HostName, null);
    }

    private void OnRemoteShellClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusViewModel vm || vm.SelectedService is null) return;
        var host = vm.SelectedService.HostName;
        var os = vm.OsFor(host);
        App.Services!.GetRequiredService<RemoteTools>().StartRemoteShell(host, os, null);
    }

    private void OnPingClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusViewModel vm || vm.SelectedService is null) return;
        App.Services!.GetRequiredService<RemoteTools>().StartPing(vm.SelectedService.HostName);
    }

    private async void OnManageFiltersClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        await new FilterManagerWindow(vm.Filters).ShowDialog(owner);
    }

    private async void OnSaveHostsAsFavoriteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var hosts = GetTargetHostNames();
        if (hosts.Count == 0) return;

        var dialog = new NameInputDialog(
            title: "Favorit speichern",
            prompt: $"{hosts.Count} Host(s) als Favorit speichern unter Namen:",
            defaultValue: hosts.Count == 1 ? hosts[0] : "");
        var name = await dialog.ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(name)) return;

        vm.Filters.Add(new Models.HostFilter
        {
            Name = name.Trim(),
            ExplicitHosts = hosts.ToList()
        });
        vm.StatusMessage = $"Favorit „{name.Trim()}“ mit {hosts.Count} Host(s) gespeichert.";
    }

    private async void OnAddHostsToFavoriteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var hosts = GetTargetHostNames();
        if (hosts.Count == 0) return;

        var candidates = vm.Filters.Filters
            .Where(f => f.ExplicitHosts is { Count: > 0 })
            .ToList();

        // Kein passender Favorit vorhanden -> gleich neuen anlegen, damit die
        // Aktion nicht ins Leere laeuft (User hat rechtsgeklickt und erwartet
        // *irgendein* Ergebnis).
        if (candidates.Count == 0)
        {
            OnSaveHostsAsFavoriteClick(sender, e);
            return;
        }

        var dialog = new FavoritePickerDialog(
            $"{hosts.Count} Host(s) zu welchem Favoriten hinzufügen?",
            candidates);
        var chosen = await dialog.ShowDialog<Models.HostFilter?>(owner);
        if (chosen is null) return;

        var before = chosen.ExplicitHosts.Count;
        foreach (var h in hosts)
        {
            if (!chosen.ExplicitHosts.Any(x => string.Equals(x, h, System.StringComparison.OrdinalIgnoreCase)))
                chosen.ExplicitHosts.Add(h);
        }
        var added = chosen.ExplicitHosts.Count - before;
        vm.Filters.Update();
        vm.StatusMessage = $"{added} Host(s) zu Favorit „{chosen.Name}“ hinzugefuegt.";
    }

    private List<string> GetTargetHostNames()
    {
        return GetTargetServices()
            .Select(s => s.HostName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();
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
