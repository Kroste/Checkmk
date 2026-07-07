using Checkmk.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Checkmk.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IConnectionSettingsStore _store;
    private readonly ICheckmkClientProvider _clients;

    public StatusViewModel Status { get; }
    public ConfigViewModel Config { get; }

    [ObservableProperty]
    private string _connectionInfo = "Nicht verbunden";

    /// <summary>Wird ausgeloest, wenn der Nutzer die Einstellungen oeffnen will.</summary>
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? OpenAboutRequested;

    public MainWindowViewModel(
        StatusViewModel status,
        ConfigViewModel config,
        IConnectionSettingsStore store,
        ICheckmkClientProvider clients)
    {
        Status = status;
        Config = config;
        _store = store;
        _clients = clients;
    }

    /// <summary>Wird nach dem Anzeigen des Fensters aufgerufen.</summary>
    public async Task InitializeAsync()
    {
        var settings = _store.Load();
        var secret = _store.LoadSecret(settings);

        if (_store.IsConfigured(settings) && secret is not null)
        {
            _clients.Configure(settings, secret);
            var scheme = settings.UseHttps ? "https" : "http";
            ConnectionInfo = $"{scheme}://{settings.Host}/{settings.Site} ({settings.Username})";
            await Status.RefreshCommand.ExecuteAsync(null);
            await Config.RefreshHostsCommand.ExecuteAsync(null);
        }
        else
        {
            ConnectionInfo = "Nicht konfiguriert";
            StatusMessage = "Bitte zuerst die Verbindung in den Einstellungen einrichten.";
        }
    }

    /// <summary>Nach dem Schliessen der Settings erneut verbinden/aktualisieren.</summary>
    public async Task ReconnectAsync()
    {
        var settings = _store.Load();
        if (_clients.IsReady)
        {
            var scheme = settings.UseHttps ? "https" : "http";
            ConnectionInfo = $"{scheme}://{settings.Host}/{settings.Site} ({settings.Username})";
            await Status.RefreshCommand.ExecuteAsync(null);
            await Config.RefreshHostsCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private void OpenSettings() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenAbout() => OpenAboutRequested?.Invoke(this, EventArgs.Empty);
}
