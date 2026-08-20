using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Checkmk.App.ViewModels;

/// <summary>
/// <see cref="ObservableCollection{T}"/> mit einem Komplett-Austausch, der
/// <b>ein</b> Reset-Event feuert statt N Einzel-Events.
///
/// Warum das noetig ist: der alte Weg war <c>Clear()</c> + <c>Add()</c> je
/// Zeile. Bei ungefiltertem Blick auf ~32.000 Checks sind das 32.000
/// <c>CollectionChanged</c>-Zustellungen ans DataGrid, jede mit Layout- und
/// Selektions-Bookkeeping — die App stand mehrere Sekunden, obwohl die Daten
/// laengst da waren. Ein Reset laesst das Grid die Ansicht einmal neu aufbauen;
/// dank Zeilen-Virtualisierung kostet das unabhaengig von der Zeilenzahl
/// ungefaehr gleich viel.
///
/// Preis des Resets: das Grid verliert die Selektion. Der Aufrufer stellt sie
/// bei Bedarf danach wieder her (siehe <c>StatusViewModel.ReplaceServices</c>).
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public BulkObservableCollection() { }

    public BulkObservableCollection(IEnumerable<T> items) : base(items) { }

    /// <summary>Ersetzt den kompletten Inhalt und meldet genau einen Reset.</summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();

        Items.Clear();
        foreach (var item in items)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
