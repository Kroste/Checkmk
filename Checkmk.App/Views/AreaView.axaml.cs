using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.ViewModels;

namespace Checkmk.App.Views;

public partial class AreaView : UserControl
{
    public AreaView() => AvaloniaXamlLoader.Load(this);

    private AreaViewModel? Vm => DataContext as AreaViewModel;

    private async void OnNewAreaClick(object? sender, RoutedEventArgs e)
        => await CreateAsync(asChild: false);

    private async void OnNewChildAreaClick(object? sender, RoutedEventArgs e)
        => await CreateAsync(asChild: true);

    private async System.Threading.Tasks.Task CreateAsync(bool asChild)
    {
        if (Vm is not { CanWrite: true } vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var parent = asChild ? vm.SelectedNode : null;
        if (asChild && parent is not { IsUnassigned: false }) return;

        var prompt = asChild
            ? $"Neuer Bereich unterhalb von „{parent!.Name}“:"
            : "Name des neuen Bereichs:";

        var dialog = new NameInputDialog("Bereich anlegen", prompt, "");
        var name = await dialog.ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(name)) return;

        await vm.CreateAsync(name, asChild);
    }

    private async void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { CanWrite: true } vm) return;
        if (vm.SelectedNode is not { IsUnassigned: false } node) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var dialog = new NameInputDialog("Bereich umbenennen", "Neuer Name:", node.Name);
        var name = await dialog.ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(name)) return;

        await vm.RenameAsync(name);
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { CanWrite: true } vm) return;

        // Der Store lehnt nicht-leere Bereiche ab und liefert den Grund als Text —
        // der landet in der Statuszeile, damit der Klick nicht wirkungslos wirkt.
        var problem = await vm.DeleteSelectedAsync();
        if (problem is not null) vm.StatusMessage = problem;
    }

    /// <summary>
    /// Weist dem markierten Bereich Hosts zu. Die Quelle ist bewusst die Liste
    /// der noch nicht zugeordneten Hosts: Bei 1105 Geräten ist „was fehlt noch"
    /// die eigentliche Arbeitsliste. Einzelne Hosts weist man umgekehrt aus dem
    /// Status- oder Hosts-Tab zu, wo man sie ohnehin markiert hat.
    /// </summary>
    private async void OnAssignHostsClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { CanWrite: true } vm) return;
        if (vm.SelectedNode is not { IsUnassigned: false } node) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var candidates = vm.UnassignedHosts;
        if (candidates.Count == 0)
        {
            vm.StatusMessage = "Im aktiven Filter ist kein Host ohne Bereich übrig.";
            return;
        }

        var dialog = new HostMultiSelectDialog(
            $"Hosts nach „{node.Name}“ zuordnen", candidates);
        var chosen = await dialog.ShowDialog<System.Collections.Generic.IReadOnlyList<string>?>(owner);
        if (chosen is null || chosen.Count == 0) return;

        await vm.AssignAsync(chosen, node.AreaId);
    }
}
