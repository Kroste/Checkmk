using System.Collections.ObjectModel;
using Checkmk.App.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Checkmk.App.Services;

/// <summary>
/// Zentraler Live-State fuer Host-Filter — Singleton, den beide Tabs (Status + Konfig) beobachten.
/// Aenderungen an <see cref="Active"/>, <see cref="Add"/>, <see cref="Remove"/>, <see cref="Update"/>
/// persistieren automatisch in den <see cref="IHostFilterStore"/>.
/// </summary>
public sealed class HostFilterCollection : ObservableObject
{
    private readonly IHostFilterStore _store;

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

    public HostFilterCollection(IHostFilterStore store)
    {
        _store = store;
        var s = _store.Load();
        foreach (var f in s.Filters)
            Filters.Add(f);
        _active = string.IsNullOrEmpty(s.ActiveFilterName)
            ? null
            : Filters.FirstOrDefault(f => f.Name == s.ActiveFilterName);
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
        => _store.Save(new HostFilterState
        {
            Filters = Filters.ToList(),
            ActiveFilterName = _active?.Name
        });
}
