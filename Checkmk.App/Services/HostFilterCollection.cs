using System.Collections.ObjectModel;
using Checkmk.App.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Checkmk.App.Services;

/// <summary>
/// Zentraler Live-State fuer Host-Filter — Singleton, den beide Tabs (Status + Konfig) beobachten.
/// Filter sind **pro Site** organisiert: beim Site-Wechsel wird die Collection
/// neu geladen. Aenderungen an <see cref="Active"/>, <see cref="Add"/>,
/// <see cref="Remove"/>, <see cref="Update"/> persistieren automatisch in den
/// <see cref="IHostFilterStore"/> unter der aktuellen Site.
/// </summary>
public sealed class HostFilterCollection : ObservableObject
{
    private readonly IHostFilterStore _store;
    private readonly IConnectionSettingsStore _settings;
    private string _currentSite;

    public ObservableCollection<HostFilter> Filters { get; } = new();

    private HostFilter? _active;
    public HostFilter? Active
    {
        get => _active;
        set
        {
            if (SetProperty(ref _active, value))
                Persist();
        }
    }

    public HostFilterCollection(IHostFilterStore store, IConnectionSettingsStore settings)
    {
        _store = store;
        _settings = settings;
        _currentSite = _settings.Load().Site;
        LoadFiltersForCurrentSite();
    }

    private void LoadFiltersForCurrentSite()
    {
        var s = _store.Load(_currentSite);
        Filters.Clear();
        foreach (var f in s.Filters)
        {
            // Defensiv: alte filter.json kann einen null-Eintrag enthalten.
            if (f is not null)
                Filters.Add(f);
        }
        _active = string.IsNullOrEmpty(s.ActiveFilterName)
            ? null
            : Filters.FirstOrDefault(f => f.Name == s.ActiveFilterName);
        OnPropertyChanged(nameof(Active));
    }

    /// <summary>Wechselt das Filter-Set auf die neue Site. Persistiert erst die aktuelle
    /// Site, laedt dann die neue.</summary>
    public void SwitchSite(string newSite)
    {
        if (string.Equals(_currentSite, newSite, StringComparison.OrdinalIgnoreCase))
            return;
        Persist();
        _currentSite = newSite;
        LoadFiltersForCurrentSite();
    }

    public void Add(HostFilter f)
    {
        Filters.Add(f);
        Persist();
    }

    public void Remove(HostFilter f)
    {
        Filters.Remove(f);
        if (ReferenceEquals(_active, f))
            Active = null;
        else
            Persist();
    }

    /// <summary>Nach externer Bearbeitung eines Filters aufrufen, um den Store zu aktualisieren.</summary>
    public void Update() => Persist();

    private void Persist()
        => _store.Save(_currentSite, new HostFilterState
        {
            Filters = Filters.ToList(),
            ActiveFilterName = _active?.Name
        });
}
