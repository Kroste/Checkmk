using System.Collections.ObjectModel;
using Checkmk.App.Services;
using Checkmk.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Checkmk.App.ViewModels;

/// <summary>
/// Ein Knoten im Bereichsbaum. Die Aggregate sind <see cref="ObservableProperty"/>
/// und werden <b>in place</b> aktualisiert statt den Baum neu zu bauen: Ein
/// Neuaufbau alle 30 Sekunden klappt jeden aufgeklappten Ast wieder zu, und
/// genau das will man beim Zuordnen von 1105 Hosts nicht.
/// </summary>
public sealed partial class AreaNodeViewModel : ObservableObject
{
    /// <summary>Kennung des Sammelknotens „Ohne Bereich" — kein Datensatz in der
    /// Datenbank, sondern die Restmenge.</summary>
    public const int UnassignedId = -1;

    public AreaNodeViewModel(int areaId, string name, bool isUnassigned = false)
    {
        AreaId = areaId;
        Name = name;
        IsUnassigned = isUnassigned;
    }

    public int AreaId { get; }

    public string Name { get; }

    /// <summary>true fuer den Sammelknoten. Er laesst sich nicht umbenennen,
    /// nicht loeschen und nicht als Zuweisungsziel waehlen.</summary>
    public bool IsUnassigned { get; }

    public ObservableCollection<AreaNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private int _hostCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge))]
    private int _problemCount;

    [ObservableProperty]
    private ServiceState _worstState = ServiceState.Ok;

    /// <summary>Kein Host im aktuellen Filter. Wird im XAML fuer einen grauen
    /// statt gruenen Punkt benutzt — ein leerer Bereich ist nicht „gesund".</summary>
    [ObservableProperty]
    private bool _isEmptyOfHosts = true;

    public bool IsEmpty => HostCount == 0;

    /// <summary>„12 Hosts · 2 Probleme" bzw. „leer".</summary>
    public string Badge => HostCount == 0
        ? "leer"
        : ProblemCount == 0
            ? $"{HostCount} Hosts"
            : $"{HostCount} Hosts · {ProblemCount} Probleme";

    public void Apply(AreaAggregate aggregate)
    {
        HostCount = aggregate.HostCount;
        ProblemCount = aggregate.ProblemCount;
        WorstState = aggregate.Worst;
        IsEmptyOfHosts = !aggregate.HasHosts;
    }

    /// <summary>Alle Knoten ab hier, inklusive sich selbst.</summary>
    public IEnumerable<AreaNodeViewModel> Flatten()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var n in child.Flatten())
                yield return n;
    }
}
