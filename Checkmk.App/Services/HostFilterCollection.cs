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
    private bool _suppressPersist;

    public ObservableCollection<HostFilter> Filters { get; } = new();

    private HostFilter? _active;
    public HostFilter? Active
    {
        get => _active;
        set
        {
            // Beim Laden setzt die two-way-gebundene ComboBox waehrend Filters.Clear()
            // Active=null zurueck. Ohne diesen Guard wuerde der Setter dann Persist()
            // mit LEERER Filterliste ausloesen und die Site auf Platte loeschen.
            if (SetProperty(ref _active, value) && !_suppressPersist)
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
        _suppressPersist = true;
        try
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
        }
        finally { _suppressPersist = false; }
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
