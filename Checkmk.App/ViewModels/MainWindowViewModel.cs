using Checkmk.App.Services;
using Checkmk.Data;
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
    private readonly ViewerMode _viewer;
    private readonly IGlobalSettingsProvider _globals;

    /// <summary>Nur gesetzt, wenn eine zentrale Datenbank konfiguriert ist.</summary>
    private readonly DbHostDomainStore? _hostDomains;

    // Verhindert, dass der Site-Setter waehrend Initialize/Reconnect einen echten
    // Switch triggert — wir wollen nur bei User-Auswahl reagieren.
    private bool _suppressSiteSwitch;

    public StatusViewModel Status { get; }
    public ConfigViewModel Config { get; }
    public DashboardViewModel Dashboard { get; }

    // Kein ObservableCollection + Clear/Add, weil Avalonias ComboBox unter
    // TwoWay-SelectedItem-Binding beim Zwischenzustand "Collection ist leer"
    // die Selection fallen laesst und den Refresh danach nicht sauber re-synced.
    // Stattdessen ersetzen wir die Liste als ganzes — ItemsSource re-bindet.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSiteSwitcherVisible))]
    private IReadOnlyList<string> _knownSites = [];

    [ObservableProperty]
    private string? _activeSite;

    /// <summary>Im Viewer-Modus gibt das Profil genau eine Site vor — kein Umschalter.</summary>
    public bool IsSiteSwitcherVisible => KnownSites.Count > 1 && !IsViewerMode;

    /// <summary>true, wenn <c>viewer.json</c> neben der Exe liegt.</summary>
    public bool IsViewerMode => _viewer.IsActive;

    /// <summary>Steuert den „Einstellungen"-Button in der Titelleiste — im
    /// Viewer-Modus weg, weil die Verbindung aus dem Profil kommt.</summary>
    public bool ShowSettings => !IsViewerMode;

    /// <summary>Fenster-/Titelleisten-Text. Aus dem Viewer-Profil uebernehmbar,
    /// damit der Anwender sieht, welche Sicht er vor sich hat.</summary>
    public string AppTitle => _viewer.Profile is { } p && !string.IsNullOrWhiteSpace(p.Title)
        ? p.Title.Trim()
        : "Checkmk Cockpit";

    [ObservableProperty]
    private string _connectionInfo = "Nicht verbunden";

    /// <summary>
    /// Hinweis zur Herkunft der zentralen Einstellungen — leer, solange sie
    /// frisch aus der Datenbank kommen. Gehoert sichtbar in die Statusleiste und
    /// nicht nur ins Log: Wer mit einem Cache-Stand arbeitet, soll es wissen,
    /// bevor er sich wundert, warum eine zentrale Aenderung nicht ankommt.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCentralInfo))]
    private string _centralInfo = "";

    public bool HasCentralInfo => !string.IsNullOrEmpty(CentralInfo);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateBadge))]
    private UpdateInfo? _availableUpdate;

    public bool HasUpdate => AvailableUpdate is not null;
    public string UpdateBadge => AvailableUpdate is { } u
        ? $"Update auf {u.Version} verfügbar"
        : "";

    partial void OnAvailableUpdateChanged(UpdateInfo? value)
        => OnPropertyChanged(nameof(HasUpdate));

    partial void OnActiveSiteChanged(string? oldValue, string? newValue)
    {
        if (_suppressSiteSwitch) return;
        if (string.IsNullOrWhiteSpace(newValue)) return;
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) return;
        _ = SwitchSiteAsync(newValue);
    }

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
        IUpdatePreferences updatePrefs,
        ViewerMode viewer,
        IGlobalSettingsProvider globals,
        DbHostDomainStore? hostDomains = null)
    {
        Status = status;
        Config = config;
        Dashboard = dashboard;
        _store = store;
        _clients = clients;
        _updateChecker = updateChecker;
        _updatePrefs = updatePrefs;
        _viewer = viewer;
        _globals = globals;
        _hostDomains = hostDomains;
    }

    /// <summary>
    /// Holt die zentralen Daten von FOC-SQL01 nach. Laeuft im Hintergrund und
    /// blockiert den Start nicht: Der Provider hat aus dem lokalen Cache bereits
    /// einen brauchbaren Stand, hier wird er nur aktuell gemacht.
    /// </summary>
    private async Task LoadCentralDataAsync()
    {
        try
        {
            await _globals.LoadAsync().ConfigureAwait(true);

            if (_hostDomains is not null)
            {
                // Einmalige Uebernahme vor dem ersten Lesen — sonst startet der
                // erste Client mit leerer Zuordnung und jeder Host fiele auf die
                // Default-Domain zurueck.
                await _hostDomains.ImportLegacyIfEmptyAsync().ConfigureAwait(true);
                await _hostDomains.RefreshAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            // Zentrale Daten sind Komfort, kein Startkriterium.
            Log.Warn(ex, "Zentrale Daten konnten nicht geladen werden.");
        }
        finally
        {
            CentralInfo = _globals.StatusHint ?? "";
        }
    }

    /// <summary>Wird nach dem Anzeigen des Fensters aufgerufen.</summary>
    public async Task InitializeAsync()
    {
        // Zentrale Daten zuerst und abgewartet: HostContext.DefaultDomain und
        // die OS-Attribut-Kandidaten haengen daran, und der erste Tab-Refresh
        // gleich darunter benutzt beides. Der Provider hat aus dem Cache schon
        // einen Stand — hier geht es nur um die paar hundert Millisekunden,
        // die den aktuellen daraus machen.
        await LoadCentralDataAsync();

        var settings = _store.Load();
        var secret = _store.LoadSecret(settings);

        RefreshKnownSitesFrom(settings);

        if (_store.IsConfigured(settings) && secret is not null)
        {
            _clients.Configure(settings, secret);
            var scheme = settings.UseHttps ? "https" : "http";
            ConnectionInfo = $"{scheme}://{settings.Host}/{settings.Site} ({settings.Username})";
            await RefreshAllTabsAsync();
        }
        else if (_viewer.Profile is { } profile)
        {
            // Viewer-Modus ohne brauchbare Verbindung: der Anwender kann das nicht
            // reparieren (kein Einstellungen-Dialog), also muss die Meldung sagen,
            // WO der Admin nachsehen muss.
            ConnectionInfo = "Profil unvollständig";
            StatusMessage = profile.LoadError is { } err
                ? $"Viewer-Profil fehlerhaft: {err} — Datei: {profile.FilePath}"
                : $"Viewer-Profil ohne vollständige Verbindung — Datei: {profile.FilePath}";
        }
        else
        {
            ConnectionInfo = "Nicht konfiguriert";
            StatusMessage = "Bitte zuerst die Verbindung in den Einstellungen einrichten.";
        }

        // Update-Check laeuft im Hintergrund, blockiert das UI nicht.
        _ = CheckForUpdatesAsync();
    }

    /// <summary>Refresh aller sichtbaren Tabs. Im Viewer-Modus existieren Hosts- und
    /// Dashboard-Tab nicht — deren Server-Calls waeren nur unnoetige Last (und der
    /// Hosts-Endpunkt braucht Setup-Rechte, die der Viewer-User nicht haben soll).</summary>
    private async Task RefreshAllTabsAsync()
    {
        await Status.RefreshCommand.ExecuteAsync(null);
        if (_viewer.IsActive) return;
        await Config.RefreshHostsCommand.ExecuteAsync(null);
        await Dashboard.RefreshCommand.ExecuteAsync(null);
    }

    private void RefreshKnownSitesFrom(ConnectionSettings settings)
    {
        _suppressSiteSwitch = true;
        try
        {
            var list = settings.KnownSites
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            // Stelle sicher, dass die aktuelle Site in der Liste ist — sonst hat die
            // ComboBox kein selected item. Vergleich case-insensitive, damit "LHP" und
            // "lhp" nicht doppelt erscheinen.
            if (!string.IsNullOrWhiteSpace(settings.Site) &&
                !list.Any(s => string.Equals(s, settings.Site, StringComparison.OrdinalIgnoreCase)))
            {
                list.Insert(0, settings.Site);
            }

            // Reihenfolge wichtig: erst die Liste ersetzen (ItemsSource re-bindet),
            // dann ActiveSite setzen (SelectedItem findet ein passendes Item).
            KnownSites = list;
            ActiveSite = settings.Site;
        }
        finally { _suppressSiteSwitch = false; }
    }

    private async Task SwitchSiteAsync(string newSite)
    {
        try
        {
            _store.UpdateActiveSite(newSite);
            var settings = _store.Load();
            var secret = _store.LoadSecret(settings);
            if (secret is null)
            {
                StatusMessage = "Site-Wechsel fehlgeschlagen — kein Secret verfuegbar.";
                return;
            }
            _clients.Configure(settings, secret);

            // Filter-Set fuer die neue Site laden — vor dem Refresh, damit die
            // Views sofort die richtigen Favoriten sehen.
            Status.Filters.SwitchSite(newSite);

            var scheme = settings.UseHttps ? "https" : "http";
            ConnectionInfo = $"{scheme}://{settings.Host}/{settings.Site} ({settings.Username})";
            StatusMessage = $"Site gewechselt auf {newSite} — lade Daten…";
            await RefreshAllTabsAsync();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Site-Wechsel auf {Site} fehlgeschlagen.", newSite);
            StatusMessage = $"Site-Wechsel fehlgeschlagen: {ex.Message}";
        }
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
        RefreshKnownSitesFrom(settings);

        if (_clients.IsReady)
        {
            var scheme = settings.UseHttps ? "https" : "http";
            ConnectionInfo = $"{scheme}://{settings.Host}/{settings.Site} ({settings.Username})";
            await RefreshAllTabsAsync();
        }
    }

    [RelayCommand]
    private void OpenSettings() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenAbout() => OpenAboutRequested?.Invoke(this, EventArgs.Empty);
}
