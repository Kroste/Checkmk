using System.Collections.Specialized;
using Checkmk.App.Models;
using Checkmk.App.Services;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

public class HostFilterCollectionSiteTests
{
    private sealed class FakeFilterStore : IHostFilterStore
    {
        public Dictionary<string, HostFilterState> Sites { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string FilePath => "(memory)";
        public HostFilterState Load(string site) =>
            Sites.TryGetValue(site, out var s)
                ? new HostFilterState { Filters = s.Filters.ToList(), ActiveFilterName = s.ActiveFilterName }
                : new HostFilterState();
        public void Save(string site, HostFilterState state) => Sites[site] = state;
    }

    private sealed class FakeSettingsStore(string site) : IConnectionSettingsStore
    {
        public ConnectionSettings Load() => new() { Site = site };
        public string? LoadSecret(ConnectionSettings settings) => null;
        public void Save(ConnectionSettings settings, string plainSecret) => throw new NotSupportedException();
        public bool IsConfigured(ConnectionSettings settings) => true;
        public string SettingsFilePath => "(memory)";
        public void UpdateActiveSite(string newSite) => throw new NotSupportedException();
    }

    private static HostFilter F(string name) => new() { Name = name };

    [Fact]
    public void Switching_sites_does_not_wipe_the_active_sites_filters()
    {
        var store = new FakeFilterStore();
        store.Sites["LHP"] = new HostFilterState
        {
            Filters = [F("Datenbanken"), F("Web")],
            ActiveFilterName = "Datenbanken"
        };
        store.Sites["schul_it"] = new HostFilterState
        {
            Filters = [F("Clients")],
            ActiveFilterName = "Clients"
        };

        var collection = new HostFilterCollection(store, new FakeSettingsStore("LHP"));

        // ComboBox-Verhalten nachstellen: beim Leeren der Liste (Clear) schreibt die
        // two-way-gebundene SelectedItem-Bindung Active=null zurueck.
        collection.Filters.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset && collection.Filters.Count == 0)
                collection.Active = null;
        };

        collection.SwitchSite("schul_it");
        collection.SwitchSite("LHP");

        store.Sites["LHP"].Filters.Should().HaveCount(2);
        store.Sites["schul_it"].Filters.Should().ContainSingle();
    }
}
