using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Services;
using Checkmk.App.ViewModels;
using Checkmk.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Checkmk.App.Views;

public partial class ConfigView : UserControl
{
    public ConfigView() => AvaloniaXamlLoader.Load(this);

    private async void OnManageFiltersClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        await new FilterManagerWindow(vm.Filters).ShowDialog(owner);
    }

    private void OnHostDoubleTapped(object? sender, TappedEventArgs e) => OpenHostDetails();
    private void OnOpenHostDetailsClick(object? sender, RoutedEventArgs e) => OpenHostDetails();

    private void OpenHostDetails()
    {
        if (DataContext is not ConfigViewModel vm || vm.SelectedHost?.Id is not { } hostName)
            return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var clients = App.Services!.GetRequiredService<ICheckmkClientProvider>();
        var detailVm = new HostDetailViewModel(clients, hostName);
        new HostDetailWindow(detailVm).Show(owner);
    }

    private async void OnSaveSelectionAsFavoriteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var grid = this.FindControl<DataGrid>("HostGrid");
        if (grid is null || grid.SelectedItems.Count == 0)
            return;

        var hostNames = grid.SelectedItems
            .OfType<CheckmkObject<HostConfigExtensions>>()
            .Select(h => h.Id ?? "")
            .Where(id => id.Length > 0)
            .ToList();
        if (hostNames.Count == 0)
            return;

        var dialog = new NameInputDialog(
            title: "Favorit speichern",
            prompt: $"{hostNames.Count} Host(s) als Favorit speichern unter Namen:",
            defaultValue: hostNames.Count == 1 ? hostNames[0] : "");
        var name = await dialog.ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(name))
            return;

        vm.Filters.Add(new Models.HostFilter
        {
            Name = name.Trim(),
            ExplicitHosts = hostNames
        });
    }
}
