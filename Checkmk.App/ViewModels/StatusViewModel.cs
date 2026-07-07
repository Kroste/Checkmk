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

    public HostFilterCollection Filters { get; }

    public ObservableCollection<ServiceStatus> Services { get; } = [];

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

    public StatusViewModel(ICheckmkClientProvider clients, HostFilterCollection filters)
    {
        _clients = clients;
        Filters = filters;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(RefreshSeconds) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        Filters.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HostFilterCollection.Active))
                ApplyFilter();
        };
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnOnlyProblemsChanged(bool value) => ApplyFilter();

    partial void OnAutoRefreshChanged(bool value)
    {
        if (value) _timer.Start();
        else _timer.Stop();
    }

    partial void OnRefreshSecondsChanged(int value)
        => _timer.Interval = TimeSpan.FromSeconds(Math.Max(5, value));

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

            var hosts = await client.GetHostStatusesAsync();
            var services = await client.GetServiceStatusesAsync();

            HostsUp = hosts.Count(h => h.HostState == HostState.Up);
            HostsDown = hosts.Count(h => h.HostState != HostState.Up);
            ServicesOk = services.Count(s => s.ServiceState == ServiceState.Ok);
            ServicesWarn = services.Count(s => s.ServiceState == ServiceState.Warning);
            ServicesCrit = services.Count(s => s.ServiceState == ServiceState.Critical);

            _allServices = [.. services];
            ApplyFilter();

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
                s.Description.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        Services.Clear();
        foreach (var s in q.OrderByDescending(s => s.State).ThenBy(s => s.HostName))
            Services.Add(s);
    }
}
