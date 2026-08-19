using Checkmk.App.Models;
using Checkmk.App.Services;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Der aus <c>viewer.json</c> vorgegebene Filter ist ein Startwert: auswaehlbar,
/// aenderbar — aber er darf nicht in die persoenliche Favoritenbibliothek
/// (<c>filter.json</c>) einsickern, sonst bliebe er dort stehen, nachdem der Admin
/// das Profil laengst geaendert hat.
/// </summary>
public class HostFilterPresetTests
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
        public ConnectionSettings Settings { get; } = new() { Site = TestSite };
        public string SettingsFilePath => "(memory)";
        public ConnectionSettings Load() => Settings;
        public string? LoadSecret(ConnectionSettings settings) => null;
        public void Save(ConnectionSettings settings, string plainSecret) { }
        public bool IsConfigured(ConnectionSettings settings) => true;
        public void UpdateActiveSite(string newSite) => Settings.Site = newSite;
    }

    private static HostFilterCollection Build(FakeStore store)
        => new(store, new FakeSettingsStore());

    [Fact]
    public void Preset_is_activated_and_listed_first()
    {
        var store = new FakeStore
        {
            State = new HostFilterState { Filters = [new HostFilter { Name = "Eigener" }] }
        };
        var collection = Build(store);

        collection.ApplyPreset(new HostFilter { Name = "DB-Server", HostNameRegex = ".*sql.*" });

        collection.Filters[0].Name.Should().Be("DB-Server");
        collection.Active!.Name.Should().Be("DB-Server");
        collection.Filters.Should().Contain(f => f.Name == "Eigener");
    }

    [Fact]
    public void Applying_a_preset_does_not_persist()
    {
        var store = new FakeStore();
        var collection = Build(store);

        collection.ApplyPreset(new HostFilter { Name = "Vorgabe", HostNameRegex = "web.*" });

        store.LastSaved.Should().BeNull();
    }

    [Fact]
    public void Preset_stays_out_of_the_file_when_the_user_saves_a_favorite_later()
    {
        var store = new FakeStore();
        var collection = Build(store);
        collection.ApplyPreset(new HostFilter { Name = "Vorgabe", HostNameRegex = "web.*" });

        collection.Add(new HostFilter { Name = "Meine DBs", ExplicitHosts = ["DBSQL01"] });

        store.LastSaved!.Filters.Should().ContainSingle().Which.Name.Should().Be("Meine DBs");
    }

    /// <summary>Solange die Vorgabe aktiv ist, darf kein Filtername als „zuletzt
    /// aktiv" gespeichert werden — sonst waere beim naechsten Start ein Filter
    /// vorgewaehlt, den es in der Datei gar nicht gibt.</summary>
    [Fact]
    public void Active_preset_is_not_written_as_the_remembered_selection()
    {
        var store = new FakeStore();
        var collection = Build(store);
        collection.ApplyPreset(new HostFilter { Name = "Vorgabe" });

        collection.Add(new HostFilter { Name = "Meine DBs" });

        store.LastSaved!.ActiveFilterName.Should().BeNull();
    }

    [Fact]
    public void Switching_away_from_the_preset_persists_the_users_choice()
    {
        var store = new FakeStore
        {
            State = new HostFilterState { Filters = [new HostFilter { Name = "Eigener" }] }
        };
        var collection = Build(store);
        collection.ApplyPreset(new HostFilter { Name = "Vorgabe" });

        collection.Active = collection.Filters.First(f => f.Name == "Eigener");

        store.LastSaved!.ActiveFilterName.Should().Be("Eigener");
        store.LastSaved!.Filters.Should().NotContain(f => f.Name == "Vorgabe");
    }

    [Fact]
    public void Preset_replaces_an_equally_named_favorite_instead_of_duplicating_it()
    {
        var store = new FakeStore
        {
            State = new HostFilterState
            {
                Filters = [new HostFilter { Name = "DB-Server", HostNameRegex = "alt" }]
            }
        };
        var collection = Build(store);

        collection.ApplyPreset(new HostFilter { Name = "db-server", HostNameRegex = "neu" });

        collection.Filters.Should().ContainSingle();
        collection.Filters[0].HostNameRegex.Should().Be("neu");
    }
}
