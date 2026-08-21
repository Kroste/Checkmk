using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Checkmk.App.Controls;
using Checkmk.App.Services;
using Checkmk.App.ViewModels;
using Checkmk.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Checkmk.App.Views;

public partial class AreaView : UserControl
{
    private MapCanvas? _map;

    public AreaView()
    {
        AvaloniaXamlLoader.Load(this);

        _map = this.FindControl<MapCanvas>("Map");
        if (_map is not null)
        {
            _map.AreaClicked += OnMapAreaClicked;
            _map.DrawingFinished += OnMapDrawingFinished;
            _map.DrawingModeChanged += UpdateDrawState;

            // GetService wegen des XAML-Previewers (kein DI-Container).
            if (App.Services?.GetService<MapTileLoader>() is { } tiles)
                _map.Attach(tiles);
        }

        DataContextChanged += (_, _) => BindViewModel();
    }

    private AreaViewModel? Vm => DataContext as AreaViewModel;

    private void BindViewModel()
    {
        if (Vm is not { } vm) return;
        vm.MapChanged += RefreshMap;
        vm.PropertyChanged += OnVmPropertyChanged;
        RefreshMap();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AreaViewModel.SelectedNode)) return;
        if (_map is null || Vm is null) return;

        _map.HighlightedAreaId = Vm.SelectedNode is { IsUnassigned: false } n ? n.AreaId : null;

        // Auf die Flaeche springen, wenn es eine gibt — sonst muesste man sie
        // auf einer Stadtkarte erst suchen.
        if (Vm.SelectedNode is { IsUnassigned: false } sel
            && MapGeometry.Parse(Vm.GeometryOf(sel.AreaId)) is { Count: >= 3 } points)
            _map.FitTo(points);
        else
            _map.InvalidateVisual();
    }

    /// <summary>Flächen und Farben neu an die Karte geben.</summary>
    private void RefreshMap()
    {
        if (_map is null || Vm is not { } vm) return;

        var shapes = new List<MapShape>();
        foreach (var node in vm.AllAreas())
        {
            var points = MapGeometry.Parse(vm.GeometryOf(node.AreaId));
            if (points.Count < 3) continue;   // Bereich ohne Flaeche: nur im Baum
            shapes.Add(new MapShape(node.AreaId, node.Name, points, ColorFor(node)));
        }

        _map.Shapes = shapes;
        _map.InvalidateVisual();
    }

    /// <summary>Dieselbe Ampel wie im Baum — grau, wenn keine Hosts drin sind.</summary>
    private static Color ColorFor(AreaNodeViewModel node) => node.IsEmptyOfHosts
        ? Color.FromRgb(0x88, 0x88, 0x88)
        : node.WorstState switch
        {
            ServiceState.Critical => Color.FromRgb(0xEF, 0x53, 0x50),
            ServiceState.Warning => Color.FromRgb(0xFF, 0xCA, 0x28),
            ServiceState.Unknown => Color.FromRgb(0xAB, 0x47, 0xBC),
            _ => Color.FromRgb(0x66, 0xBB, 0x6A)
        };

    private void OnMapAreaClicked(int areaId)
    {
        if (Vm?.NodeOf(areaId) is { } node) Vm.SelectedNode = node;
    }

    private async void OnMapDrawingFinished(IReadOnlyList<GeoPoint> points)
    {
        if (Vm is not { CanWrite: true } vm) return;
        if (vm.SelectedNode is not { IsUnassigned: false } node) return;

        await vm.SaveGeometryAsync(node.AreaId, MapGeometry.ToGeoJson(points));
        RefreshMap();
    }

    private void UpdateDrawState()
    {
        var drawing = _map?.IsDrawing ?? false;
        if (this.FindControl<Button>("DrawButton") is { } b)
            b.Content = drawing ? "Zeichnen abbrechen" : "Fläche zeichnen";
        if (this.FindControl<Border>("DrawHint") is { } hint)
            hint.IsVisible = drawing;
    }

    private void OnDrawAreaClick(object? sender, RoutedEventArgs e)
    {
        if (_map is null || Vm is not { CanWrite: true } vm) return;
        if (vm.SelectedNode is not { IsUnassigned: false } node) return;

        if (_map.IsDrawing) { _map.CancelDrawing(); return; }

        vm.StatusMessage = $"Fläche für „{node.Name}“ zeichnen — Punkte klicken, "
                         + "Doppelklick oder Enter schließt ab.";
        _map.BeginDrawing();
    }

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
