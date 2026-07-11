using Checkmk.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;

namespace Checkmk.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IConnectionSettingsStore _store;
    private readonly ICheckmkClientProvider _clients;
    private readonly IUpdateChecker _updateChecker;
    private readonly IUpdatePreferences _updatePrefs;

    public StatusViewModel Status { get; }
    public ConfigViewModel Config { get; }
    public DashboardViewModel Dashboard { get; }

    [ObservableProperty]
    private string _connectionInfo = "Nicht verbunden";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateBadge))]
    private UpdateInfo? _availableUpdate;

    public bool HasUpdate => AvailableUpdate is not null;
    public string UpdateBadge => AvailableUpdate is { } u
        ? $"Update auf {u.Version} verfügbar"
        : "";

    partial void OnAvailableUpdateChanged(UpdateInfo? value)
        => OnPropertyChanged(nameof(HasUpdate));

    /// <summary>Wird ausgeloest, wenn der Nutzer die Einstellungen oeffnen will.</summary>
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? OpenAboutRequested;
    public event EventHandler<UpdateInfo>? OpenUpdateRequested;

    public MainWindowViewModel(
        StatusViewModel status,
        ConfigViewModel config,
        DashboardViewModel dashboard,
        IConnectionSettingsStore store,
        ICheckmkClientProvider clients,
        IUpdateChecker updateChecker,
        IUpdatePreferences updatePrefs)
    {
        Status = status;
        Config = config;
        Dashboard = dashboard;
        _store = store;
        _clients = clients;
        _updateChecker = updateChecker;
        _updatePrefs = updatePrefs;
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
            await Dashboard.RefreshCommand.ExecuteAsync(null);
        }
        else
        {
            ConnectionInfo = "Nicht konfiguriert";
            StatusMessage = "Bitte zuerst die Verbindung in den Einstellungen einrichten.";
        }

        // Update-Check laeuft im Hintergrund, blockiert das UI nicht.
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var info = await _updateChecker.CheckAsync();
            if (info is not null)
            {
                AvailableUpdate = info;
                Log.Info("Update verfuegbar: {Version} ({Tag})", info.Version, info.TagName);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Update-Check hat eine unerwartete Ausnahme geworfen.");
        }
    }

    [RelayCommand]
    private void OpenUpdate()
    {
        if (AvailableUpdate is { } u)
            OpenUpdateRequested?.Invoke(this, u);
    }

    /// <summary>Wird vom Update-Dialog aufgerufen, wenn der Nutzer "Diese Version ueberspringen" waehlt.</summary>
    public void SkipCurrentUpdate()
    {
        if (AvailableUpdate is { } u)
        {
            _updatePrefs.SaveSkippedVersion(u.Version);
            AvailableUpdate = null;
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
            await Dashboard.RefreshCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private void OpenSettings() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenAbout() => OpenAboutRequested?.Invoke(this, EventArgs.Empty);
}
