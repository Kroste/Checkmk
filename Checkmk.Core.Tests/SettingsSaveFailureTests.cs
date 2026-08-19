using Checkmk.App.Services;
using Checkmk.App.ViewModels;
using Checkmk.Core;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

/// <summary>
/// Regression zu dem Absturz, der Nutzern mit einer bootstrap.json auf ein fremdes
/// Benutzerprofil passiert ist: <c>File.WriteAllText</c> warf
/// <c>DirectoryNotFoundException</c>, die Exception lief aus dem RelayCommand in den
/// Avalonia-Dispatcher und beendete den Prozess. Speichern darf niemals die App
/// mitreissen — egal warum das Ziel nicht beschreibbar ist.
/// </summary>
public class SettingsSaveFailureTests
{
    private sealed class ThrowingStore(Exception toThrow) : IConnectionSettingsStore
    {
        public string SettingsFilePath => @"C:\Users\Fremd\AppData\Roaming\Kroste\Checkmk\settings.json";
        public ConnectionSettings Load() => new() { Host = "cmk", Site = "LHP", Username = "u" };
        public string? LoadSecret(ConnectionSettings settings) => "geheim";
        public void Save(ConnectionSettings settings, string plainSecret) => throw toThrow;
        public bool IsConfigured(ConnectionSettings settings) => true;
        public void UpdateActiveSite(string newSite) { }
    }

    private sealed class OkStore : IConnectionSettingsStore
    {
        public ConnectionSettings? Saved { get; private set; }
        public string SettingsFilePath => "(memory)";
        public ConnectionSettings Load() => new() { Host = "cmk", Site = "LHP", Username = "u" };
        public string? LoadSecret(ConnectionSettings settings) => "geheim";
        public void Save(ConnectionSettings settings, string plainSecret) => Saved = settings;
        public bool IsConfigured(ConnectionSettings settings) => true;
        public void UpdateActiveSite(string newSite) { }
    }

    private sealed class FakeClients : ICheckmkClientProvider
    {
        public int ConfigureCalls { get; private set; }
        public CheckmkClient? Current => null;
        public bool IsReady => false;
        public void Configure(ConnectionSettings settings, string plainSecret) => ConfigureCalls++;
    }

    [Theory]
    [MemberData(nameof(Schreibfehler))]
    public void Save_survives_an_unwritable_target(Exception boom)
    {
        var clients = new FakeClients();
        var vm = new SettingsViewModel(new ThrowingStore(boom), clients);
        var closed = false;
        vm.RequestClose += (_, _) => closed = true;

        // Darf nicht werfen — sonst stirbt im echten Betrieb die Anwendung.
        vm.SaveCommand.Invoking(c => c.Execute(null)).Should().NotThrow();

        vm.Saved.Should().BeFalse();
        closed.Should().BeFalse("der Dialog muss offen bleiben, sonst sind die Eingaben weg");
        vm.StatusMessage.Should().Contain("Speichern fehlgeschlagen");
        vm.StatusMessage.Should().Contain(@"C:\Users\Fremd", "der Zielpfad gehoert in die Meldung");
        clients.ConfigureCalls.Should().Be(0, "eine nicht gespeicherte Verbindung darf nicht aktiv werden");
    }

    public static TheoryData<Exception> Schreibfehler() =>
    [
        new DirectoryNotFoundException(
            @"Could not find a part of the path 'C:\Users\Fremd\AppData\Roaming\Kroste\Checkmk\settings.json'."),
        new UnauthorizedAccessException(
            @"Access to the path 'C:\Users\Fremd\AppData\Roaming\Kroste\Checkmk' is denied."),
        new IOException("Der Prozess kann nicht auf die Datei zugreifen.")
    ];

    [Fact]
    public void Successful_save_still_activates_and_closes()
    {
        var store = new OkStore();
        var clients = new FakeClients();
        var vm = new SettingsViewModel(store, clients) { Host = "cmk2", Site = "S2", Username = "u2" };
        var closed = false;
        vm.RequestClose += (_, _) => closed = true;

        vm.SaveCommand.Execute(null);

        vm.Saved.Should().BeTrue();
        closed.Should().BeTrue();
        clients.ConfigureCalls.Should().Be(1);
        store.Saved!.Host.Should().Be("cmk2");
    }
}
