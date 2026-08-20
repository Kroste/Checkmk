using System.Collections.Specialized;
using Checkmk.App.ViewModels;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Der Komplett-Austausch muss <b>ein</b> Event feuern. Frueher war es
/// <c>Clear()</c> + N-mal <c>Add()</c>: bei ungefiltertem Blick auf ~32.000
/// Checks 32.000 Zustellungen ans DataGrid, jede mit Layout- und
/// Selektions-Bookkeeping — genau der mehrsekuendige Freeze.
/// </summary>
public class BulkObservableCollectionTests
{
    [Fact]
    public void ReplaceAll_raises_exactly_one_reset()
    {
        var collection = new BulkObservableCollection<string>(["alt1", "alt2"]);
        var events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => events.Add(e);

        collection.ReplaceAll(Enumerable.Range(0, 5000).Select(i => $"neu{i}"));

        events.Should().ContainSingle();
        events[0].Action.Should().Be(NotifyCollectionChangedAction.Reset);
        collection.Should().HaveCount(5000);
        collection[0].Should().Be("neu0");
    }

    [Fact]
    public void ReplaceAll_reports_count_change()
    {
        var collection = new BulkObservableCollection<int>([1, 2, 3]);
        var properties = new List<string?>();
        ((System.ComponentModel.INotifyPropertyChanged)collection).PropertyChanged +=
            (_, e) => properties.Add(e.PropertyName);

        collection.ReplaceAll([9]);

        properties.Should().Contain("Count");
        collection.Should().Equal(9);
    }

    [Fact]
    public void ReplaceAll_with_empty_source_clears()
    {
        var collection = new BulkObservableCollection<int>([1, 2, 3]);

        collection.ReplaceAll([]);

        collection.Should().BeEmpty();
    }
}
