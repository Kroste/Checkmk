using System.Collections.Specialized;
using Checkmk.App.Models;
using Checkmk.App.Services;
using Checkmk.App.ViewModels;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

public class FilterManagerApplyTests
{
    private const string TestSite = "TestSite";

    private sealed class FakeStore : IHostFilterStore
    {
        public HostFilterState State { get; init; } = new();
        public HostFilterState? LastSaved { get; private set; }
        public string FilePath => "(memory)";
        public HostFilterState Load(string site) => State;
        public void Save(string site, HostFilterState state) => LastSaved = state;
    }

    private sealed class FakeSettingsStore : IConnectionSettingsStore
    {
        public ConnectionSettings Settings { get; init; } = new() { Site = TestSite };
        public string SettingsFilePath => "(memory)";
        public ConnectionSettings Load() => Settings;
        public string? LoadSecret(ConnectionSettings settings) => null;
        public void Save(ConnectionSettings settings, string plainSecret) { }
        public bool IsConfigured(ConnectionSettings settings) => true;
        public void UpdateActiveSite(string newSite) => Settings.Site = newSite;
    }

    [Fact]
    public void Apply_does_not_insert_null_when_selection_clears_on_remove()
    {
        var store = new FakeStore
        {
            State = new HostFilterState
            {
                Filters = [new HostFilter { Name = "A" }, new HostFilter { Name = "B" }]
            }
        };
        var collection = new HostFilterCollection(store, new FakeSettingsStore());
        var vm = new FilterManagerViewModel(collection)
        {
            Selected = collection.Filters.First(f => f.Name == "A")
        };

        // ListBox-Verhalten nachstellen: beim Entfernen des markierten Items schreibt
        // die two-way-Bindung SelectedItem -> null zurueck ins ViewModel.
        collection.Filters.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Remove)
                vm.Selected = null;
        };

        vm.EditName = "A-renamed";
        vm.EditRegex = "web.*";
        vm.ApplyCommand.Execute(null);

        collection.Filters.Should().HaveCount(2);
        collection.Filters.Should().NotContainNulls();
        collection.Filters.Should().Contain(f => f.Name == "A-renamed" && f.HostNameRegex == "web.*");
        store.LastSaved!.Filters.Should().NotContainNulls();
        store.LastSaved!.Filters.Should().Contain(f => f.Name == "A-renamed");
    }

    [Fact]
    public void Load_skips_null_entries_from_a_corrupted_file()
    {
        var store = new FakeStore
        {
            State = new HostFilterState
            {
                Filters = [new HostFilter { Name = "Good" }, null!]
            }
        };

        var collection = new HostFilterCollection(store, new FakeSettingsStore());

        collection.Filters.Should().ContainSingle(f => f.Name == "Good");
        collection.Filters.Should().NotContainNulls();
    }
}
