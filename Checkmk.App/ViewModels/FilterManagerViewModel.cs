using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
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

    /// <summary>Fehlermeldung fuer den Editor (v. a. Regex-Validierung).</summary>
    [ObservableProperty] private string _validationMessage = "";

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

        var regex = string.IsNullOrWhiteSpace(EditRegex) ? null : EditRegex.Trim();

        // Regex VOR dem Speichern validieren — ein kaputter Ausdruck wuerde sonst
        // erst zur Refresh-Zeit auffallen (und ist dann persistent in filter.json,
        // wodurch jeder Auto-Refresh die Ausnahme wiederholt).
        if (regex is not null)
        {
            try
            {
                _ = new Regex(regex, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException ex)
            {
                ValidationMessage = $"Regex ungültig: {ex.Message}";
                return;
            }
        }
        ValidationMessage = "";

        // Referenz sichern: Das RemoveAt unten leert die two-way-gebundene ListBox-Auswahl
        // und schreibt Selected=null zurueck. Ohne diese lokale Kopie wuerde danach ein
        // null in die Filter-Liste eingefuegt (-> NRE beim naechsten Laden/Matchen).
        var item = Selected;

        item.Name = string.IsNullOrWhiteSpace(EditName) ? "unbenannt" : EditName.Trim();
        item.HostNameRegex = regex;
        item.ExplicitHosts = EditExplicitHosts
            .Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _collection.Update();

        // ObservableCollection benachrichtigt bei Property-Aenderungen auf Items nicht — Re-Insert
        // erzwingt das Neu-Rendern des Eintrags (ListBox zeigt HostFilter via ToString/Name).
        var idx = Filters.IndexOf(item);
        if (idx >= 0)
        {
            Filters.RemoveAt(idx);
            Filters.Insert(idx, item);
            Selected = item;
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
        ValidationMessage = "";
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
