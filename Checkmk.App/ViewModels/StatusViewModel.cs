using System.Collections.ObjectModel;
using Avalonia.Threading;
using Checkmk.App.Services;
using Checkmk.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;

namespace Checkmk.App.ViewModels;

public sealed partial class StatusViewModel : ViewModelBase
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly ICheckmkClientProvider _clients;
    private readonly DispatcherTimer _timer;
    private List<ServiceStatus> _allServices = [];
    private Dictionary<string, OsFamily> _osByHost = [];

    public HostFilterCollection Filters { get; }

    public ObservableCollection<ServiceStatus> Services { get; } = [];

    /// <summary>Baum-Ansicht: Hosts als Knoten (OS-Pictogram + Problem-Zaehler), Services als Kinder.</summary>
    public ObservableCollection<HostNodeViewModel> HostTree { get; } = [];

    /// <summary>false = Tabelle, true = Baum.</summary>
    [ObservableProperty]
    private bool _treeView;

    /// <summary>Aktuell im Baum gewaehlter Knoten (HostNodeViewModel oder ServiceStatus).</summary>
    [ObservableProperty]
    private object? _selectedTreeItem;

    /// <summary>Nach jedem Refresh: Services beschraenkt auf den aktiven Filter + Filtername.
    /// Fuer Tray-Signal und Notifications.</summary>
    public event Action<IReadOnlyList<ServiceStatus>, string?>? Refreshed;

    [ObservableProperty]
    private ServiceStatus? _selectedService;

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private bool _onlyProblems = true;

    [ObservableProperty]
    private bool _autoRefresh;

    [ObservableProperty]
    private int _refreshSeconds = 30;

    [ObservableProperty]
    private int _hostsUp;

    [ObservableProperty]
    private int _hostsDown;

    [ObservableProperty]
    private int _servicesOk;

    [ObservableProperty]
    private int _servicesWarn;

    [ObservableProperty]
    private int _servicesCrit;

    private readonly IStatusViewStateStore _stateStore;
    private bool _loadingState;

    public StatusViewModel(ICheckmkClientProvider clients, HostFilterCollection filters,
        IStatusViewStateStore stateStore)
    {
        _clients = clients;
        Filters = filters;
        _stateStore = stateStore;

        // UI-Praeferenzen aus letzter Sitzung wieder herstellen, BEVOR die
        // Property-Change-Handler aktiv werden. _loadingState verhindert, dass die
        // Load-Zuweisungen ihrerseits ein Save triggern.
        _loadingState = true;
        var s = _stateStore.Load();
        TreeView = s.TreeView;
        FilterText = s.FilterText;
        OnlyProblems = s.OnlyProblems;
        AutoRefresh = s.AutoRefresh;
        RefreshSeconds = s.RefreshSeconds;
        _loadingState = false;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(RefreshSeconds) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        if (AutoRefresh) _timer.Start();

        Filters.PropertyChanged += async (_, e) =>
        {
            // Filter-Wechsel triggert einen neuen Server-Call — sonst blieben in
            // _allServices die Services der VORHERIGEN Filter-Menge, und die
            // clientseitige ApplyFilter()-Filterung liefe ins Leere.
            if (e.PropertyName == nameof(HostFilterCollection.Active))
                await RefreshAsync();
        };
    }

    private void PersistState()
    {
        if (_loadingState) return;
        _stateStore.Save(new StatusViewState
        {
            TreeView = TreeView,
            FilterText = FilterText,
            OnlyProblems = OnlyProblems,
            AutoRefresh = AutoRefresh,
            RefreshSeconds = RefreshSeconds
        });
    }

    partial void OnFilterTextChanged(string value) { ApplyFilter(); PersistState(); }
    partial void OnOnlyProblemsChanged(bool value) { ApplyFilter(); PersistState(); }
    partial void OnTreeViewChanged(bool value) => PersistState();

    partial void OnAutoRefreshChanged(bool value)
    {
        if (value) _timer.Start();
        else _timer.Stop();
        PersistState();
    }

    partial void OnRefreshSecondsChanged(int value)
    {
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(5, value));
        PersistState();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var client = _clients.Current;
        if (client is null)
        {
            StatusMessage = "Nicht konfiguriert — bitte Verbindung in den Einstellungen setzen.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Aktualisiere…";

            // Serverseitig filtern — bei grossen Installationen (Zehntausende Checks)
            // spart das ein Vielfaches an Netzwerklast. Regex/Include-Liste geht
            // direkt in die Livestatus-Query, Freitext + „Nur Probleme" bleiben
            // clientside (das sind reine Ansichtsfilter, keine Beschraenkung des
            // Datensatzes).
            var livestatusFilter = Filters.Active?.ToLivestatus();

            var hosts = await client.GetHostStatusesAsync(livestatusFilter);
            var services = await client.GetServiceStatusesAsync(livestatusFilter);

            HostsUp = hosts.Count(h => h.HostState == HostState.Up);
            HostsDown = hosts.Count(h => h.HostState != HostState.Up);
            ServicesOk = services.Count(s => s.ServiceState == ServiceState.Ok);
            ServicesWarn = services.Count(s => s.ServiceState == ServiceState.Warning);
            ServicesCrit = services.Count(s => s.ServiceState == ServiceState.Critical);

            _allServices = [.. services];

            // OS-Familie je Host aus der "Check_MK Agent"-Service-Ausgabe (z. B. "OS: windows").
            _osByHost = _allServices
                .Where(s => s.Description == "Check_MK Agent")
                .Select(s => (s.HostName, Os: OsDetection.ParseFamily(s.PluginOutput)))
                .Where(x => x.Os != OsFamily.Unknown)
                .GroupBy(x => x.HostName)
                .ToDictionary(g => g.Key, g => g.First().Os);

            ApplyFilter();

            // Fuer Tray/Notifications: die Services sind bereits auf den aktiven
            // Filter beschraenkt (serverseitig gefiltert oben) — direkt weiterreichen.
            Refreshed?.Invoke(_allServices, Filters.Active?.Name);

            StatusMessage = $"Aktualisiert {DateTime.Now:HH:mm:ss} — "
                          + $"{services.Count} Services, {hosts.Count} Hosts.";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Status-Refresh fehlgeschlagen.");
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Acknowledged das aktuell gewaehlte Service-Problem und aktualisiert.</summary>
    public async Task PerformAcknowledgeAsync(string comment)
    {
        var client = _clients.Current;
        var svc = SelectedService;
        if (client is null || svc is null) return;

        try
        {
            IsBusy = true;
            await client.AcknowledgeServiceProblemAsync(svc.HostName, svc.Description, comment);
            StatusMessage = $"Acknowledged: {svc.HostName} / {svc.Description}.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Acknowledge fehlgeschlagen.");
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>Setzt eine Downtime auf dem gewaehlten Service und aktualisiert.</summary>
    public async Task PerformDowntimeAsync(string comment, DateTimeOffset start, DateTimeOffset end)
    {
        var client = _clients.Current;
        var svc = SelectedService;
        if (client is null || svc is null) return;

        try
        {
            IsBusy = true;
            await client.ScheduleServiceDowntimeAsync(svc.HostName, svc.Description, start, end, comment);
            StatusMessage = $"Downtime bis {end:HH:mm} gesetzt: {svc.HostName} / {svc.Description}.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Downtime fehlgeschlagen.");
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>Legt einen Kommentar auf dem gewaehlten Service an.</summary>
    public async Task PerformAddCommentAsync(string comment, bool persistent)
    {
        var client = _clients.Current;
        var svc = SelectedService;
        if (client is null || svc is null) return;

        try
        {
            IsBusy = true;
            await client.AddServiceCommentAsync(svc.HostName, svc.Description, comment, persistent);
            StatusMessage = $"Kommentar gespeichert: {svc.HostName} / {svc.Description}.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Kommentar-Anlage fehlgeschlagen.");
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>Ack fuer alle uebergebenen Services (Bulk). Fehler werden gesammelt, nicht abgebrochen.</summary>
    public async Task PerformBulkAcknowledgeAsync(IReadOnlyList<ServiceStatus> services, string comment)
    {
        var client = _clients.Current;
        if (client is null || services.Count == 0) return;

        var errors = 0;
        var done = 0;
        try
        {
            IsBusy = true;
            foreach (var svc in services)
            {
                try
                {
                    done++;
                    StatusMessage = $"Ack {done}/{services.Count}: {svc.HostName} / {svc.Description}";
                    await client.AcknowledgeServiceProblemAsync(svc.HostName, svc.Description, comment);
                }
                catch (Exception ex)
                {
                    errors++;
                    Log.Warn(ex, "Bulk-Ack fehlgeschlagen fuer {Host}/{Service}.", svc.HostName, svc.Description);
                }
            }
            StatusMessage = errors == 0
                ? $"Acknowledged: {done} Services."
                : $"Acknowledged: {done - errors}/{done} — {errors} Fehler (siehe Log).";
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    /// <summary>Downtime fuer alle uebergebenen Services (Bulk). Fehler werden gesammelt.</summary>
    public async Task PerformBulkDowntimeAsync(IReadOnlyList<ServiceStatus> services,
        string comment, DateTimeOffset start, DateTimeOffset end)
    {
        var client = _clients.Current;
        if (client is null || services.Count == 0) return;

        var errors = 0;
        var done = 0;
        try
        {
            IsBusy = true;
            foreach (var svc in services)
            {
                try
                {
                    done++;
                    StatusMessage = $"Downtime {done}/{services.Count}: {svc.HostName} / {svc.Description}";
                    await client.ScheduleServiceDowntimeAsync(svc.HostName, svc.Description, start, end, comment);
                }
                catch (Exception ex)
                {
                    errors++;
                    Log.Warn(ex, "Bulk-Downtime fehlgeschlagen fuer {Host}/{Service}.", svc.HostName, svc.Description);
                }
            }
            StatusMessage = errors == 0
                ? $"Downtime bis {end:HH:mm} gesetzt: {done} Services."
                : $"Downtime: {done - errors}/{done} — {errors} Fehler (siehe Log).";
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    private void ApplyFilter()
    {
        IEnumerable<ServiceStatus> q = _allServices;

        if (Filters.Active is { } activeFilter)
            q = q.Where(s => activeFilter.Matches(s.HostName));

        if (OnlyProblems)
            q = q.Where(s => s.ServiceState != ServiceState.Ok);

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var f = FilterText.Trim();
            q = q.Where(s =>
                s.HostName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                s.Description.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                (s.HostAlias?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.PluginOutput?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        Services.Clear();
        foreach (var s in q.OrderByDescending(s => s.State).ThenBy(s => s.HostName))
            Services.Add(s);

        BuildTree();
    }

    /// <summary>
    /// Baut den Host-Baum: oberste Knoten = Hosts (Host-Filter + Freitext), Kinder = deren
    /// Services. "Nur Probleme" filtert auch hier (dann nur Problem-Services + Hosts mit Problemen).
    /// </summary>
    private void BuildTree()
    {
        IEnumerable<ServiceStatus> q = _allServices;

        if (Filters.Active is { } activeFilter)
            q = q.Where(s => activeFilter.Matches(s.HostName));

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var f = FilterText.Trim();
            q = q.Where(s =>
                s.HostName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                s.Description.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                (s.HostAlias?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.PluginOutput?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        HostTree.Clear();
        foreach (var group in q.GroupBy(s => s.HostName).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var all = group.ToList();
            var hasProblems = all.Any(s => s.ServiceState != ServiceState.Ok);

            if (OnlyProblems && !hasProblems)
                continue;

            IEnumerable<ServiceStatus> children = OnlyProblems
                ? all.Where(s => s.ServiceState != ServiceState.Ok)
                : all;

            children = children.OrderByDescending(s => s.State).ThenBy(s => s.Description);

            HostTree.Add(new HostNodeViewModel(
                group.Key,
                _osByHost.GetValueOrDefault(group.Key, OsFamily.Unknown),
                children));
        }
    }
}
