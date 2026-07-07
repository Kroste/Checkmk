using System.Collections.ObjectModel;
using Checkmk.App.Models;
using Checkmk.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Checkmk.App.ViewModels;

/// <summary>Dialog-VM zum Verwalten der Host-Filter (Anlegen/Bearbeiten/Loeschen).</summary>
public sealed partial class FilterManagerViewModel : ObservableObject
{
    private readonly HostFilterCollection _collection;

    public ObservableCollection<HostFilter> Filters => _collection.Filters;

    [ObservableProperty]
    private HostFilter? _selected;

    // Editor-Felder (auf Selected gemappt beim Selection-Change)
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editRegex = "";
    [ObservableProperty] private string _editExplicitHosts = "";

    public FilterManagerViewModel(HostFilterCollection collection)
    {
        _collection = collection;
        _selected = _collection.Active ?? _collection.Filters.FirstOrDefault();
        LoadFromSelected();
    }

    partial void OnSelectedChanged(HostFilter? value) => LoadFromSelected();

    [RelayCommand]
    private void New()
    {
        var f = new HostFilter { Name = NextName() };
        _collection.Add(f);
        Selected = f;
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        var toRemove = Selected;
        var idx = Filters.IndexOf(toRemove);
        _collection.Remove(toRemove);
        Selected = Filters.Count == 0
            ? null
            : Filters[Math.Min(idx, Filters.Count - 1)];
    }

    [RelayCommand]
    private void Apply()
    {
        if (Selected is null) return;
        Selected.Name = string.IsNullOrWhiteSpace(EditName) ? "unbenannt" : EditName.Trim();
        Selected.HostNameRegex = string.IsNullOrWhiteSpace(EditRegex) ? null : EditRegex.Trim();
        Selected.ExplicitHosts = EditExplicitHosts
            .Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _collection.Update();

        // ObservableCollection benachrichtigt bei Property-Aenderungen auf Items nicht — Refresh trickst
        var idx = Filters.IndexOf(Selected);
        if (idx >= 0)
        {
            Filters.RemoveAt(idx);
            Filters.Insert(idx, Selected);
            Selected = Filters[idx];
        }
    }

    [RelayCommand]
    private void ActivateSelected()
    {
        _collection.Active = Selected;
    }

    [RelayCommand]
    private void ClearActive()
    {
        _collection.Active = null;
    }

    private void LoadFromSelected()
    {
        if (Selected is null)
        {
            EditName = "";
            EditRegex = "";
            EditExplicitHosts = "";
            return;
        }
        EditName = Selected.Name;
        EditRegex = Selected.HostNameRegex ?? "";
        EditExplicitHosts = string.Join(Environment.NewLine, Selected.ExplicitHosts);
    }

    private string NextName()
    {
        var i = 1;
        while (Filters.Any(f => f.Name == $"Filter {i}")) i++;
        return $"Filter {i}";
    }
}
