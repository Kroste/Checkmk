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

    /// <summary>
    /// Setzt einen vorgegebenen Filter (Viewer-Modus) an den Anfang der Liste und
    /// aktiviert ihn — <b>ohne</b> zu persistieren. Der Anwender darf danach frei
    /// umschalten; beim naechsten Start gilt wieder die Vorgabe aus <c>viewer.json</c>.
    /// </summary>
    public void ApplyPreset(HostFilter preset)
    {
        preset.IsTransient = true;
        _suppressPersist = true;
        try
        {
            // Gleichnamigen Bestandsfilter entfernen, damit die ComboBox keine
            // zwei optisch identischen Eintraege zeigt.
            var clash = Filters.FirstOrDefault(f =>
                string.Equals(f.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
            if (clash is not null)
                Filters.Remove(clash);

            Filters.Insert(0, preset);
            _active = preset;
        }
        finally { _suppressPersist = false; }
        OnPropertyChanged(nameof(Active));
    }

    private void Persist()
        => _store.Save(_currentSite, new HostFilterState
        {
            // Transiente Vorgabe-Filter bleiben draussen — sie gehoeren dem Profil,
            // nicht der Favoritenbibliothek des Anwenders.
            Filters = Filters.Where(f => !f.IsTransient).ToList(),
            ActiveFilterName = _active is { IsTransient: false } ? _active.Name : null
        });
}
