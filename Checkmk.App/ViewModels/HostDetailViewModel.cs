using System.Collections.ObjectModel;
using Checkmk.App.Services;
using Checkmk.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;

namespace Checkmk.App.ViewModels;

/// <summary>
/// State fuer das Host-Detailfenster: laedt Host-Config + Live-Status + Services
/// eines einzelnen Hosts und exponiert Ack/Downtime — auch fuer den kompletten Host.
/// Wird direkt instanziert (nicht per DI), weil der Hostname zur Laufzeit vorliegt.
/// </summary>
public sealed partial class HostDetailViewModel : ViewModelBase
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly ICheckmkClientProvider _clients;

    public string HostName { get; }

    public ObservableCollection<ServiceStatus> Services { get; } = [];
    public ObservableCollection<CheckmkObject<CommentExtensions>> Comments { get; } = [];

    [ObservableProperty] private ServiceStatus? _selectedService;
    [ObservableProperty] private HostStatus? _hostStatus;
    [ObservableProperty] private CheckmkObject<HostConfigExtensions>? _hostConfig;

    // Aggregierte Zahlen fuer den Header
    [ObservableProperty] private int _servicesOk;
    [ObservableProperty] private int _servicesWarn;
    [ObservableProperty] private int _servicesCrit;
    [ObservableProperty] private int _servicesUnknown;

    public HostDetailViewModel(ICheckmkClientProvider clients, string hostName)
    {
        _clients = clients;
        HostName = hostName;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var client = _clients.Current;
        if (client is null)
        {
            StatusMessage = "Nicht konfiguriert.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Aktualisiere…";

            // Parallel: die vier Endpunkte sind unabhaengig.
            var configTask = SafeGetConfigAsync(client);
            var statusTask = client.GetHostStatusAsync(HostName);
            var servicesTask = client.GetServiceStatusesAsync(HostName);
            var commentsTask = SafeGetCommentsAsync(client);

            await Task.WhenAll(configTask, statusTask, servicesTask, commentsTask);

            HostConfig = configTask.Result;
            HostStatus = statusTask.Result;

            Comments.Clear();
            foreach (var c in commentsTask.Result.OrderByDescending(c => c.Extensions?.EntryTime))
                Comments.Add(c);

            var services = servicesTask.Result;
            ServicesOk = services.Count(s => s.ServiceState == ServiceState.Ok);
            ServicesWarn = services.Count(s => s.ServiceState == ServiceState.Warning);
            ServicesCrit = services.Count(s => s.ServiceState == ServiceState.Critical);
            ServicesUnknown = services.Count(s => s.ServiceState == ServiceState.Unknown);

            Services.Clear();
            foreach (var s in services.OrderByDescending(s => s.State).ThenBy(s => s.Description))
                Services.Add(s);

            StatusMessage = $"Aktualisiert {DateTime.Now:HH:mm:ss} — {services.Count} Services.";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Host-Detail-Refresh fuer {Host} fehlgeschlagen.", HostName);
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>Ack fuer den aktuell gewaehlten Service.</summary>
    public async Task PerformServiceAcknowledgeAsync(string comment)
    {
        var client = _clients.Current;
        var svc = SelectedService;
        if (client is null || svc is null) return;

        try
        {
            IsBusy = true;
            await client.AcknowledgeServiceProblemAsync(svc.HostName, svc.Description, comment);
            StatusMessage = $"Acknowledged: {svc.Description}.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Service-Ack fehlgeschlagen.");
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>Downtime fuer den aktuell gewaehlten Service.</summary>
    public async Task PerformServiceDowntimeAsync(string comment, DateTimeOffset start, DateTimeOffset end)
    {
        var client = _clients.Current;
        var svc = SelectedService;
        if (client is null || svc is null) return;

        try
        {
            IsBusy = true;
            await client.ScheduleServiceDowntimeAsync(svc.HostName, svc.Description, start, end, comment);
            StatusMessage = $"Downtime bis {end:HH:mm} gesetzt: {svc.Description}.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Service-Downtime fehlgeschlagen.");
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>Ack fuer mehrere gewaehlte Services (Bulk). Fehler werden gesammelt.</summary>
    public async Task PerformBulkServiceAcknowledgeAsync(IReadOnlyList<ServiceStatus> services, string comment)
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
                    StatusMessage = $"Ack {done}/{services.Count}: {svc.Description}";
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

    /// <summary>Downtime fuer mehrere gewaehlte Services (Bulk). Fehler werden gesammelt.</summary>
    public async Task PerformBulkServiceDowntimeAsync(IReadOnlyList<ServiceStatus> services,
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
                    StatusMessage = $"Downtime {done}/{services.Count}: {svc.Description}";
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

    /// <summary>Ack fuer den kompletten Host (nur sinnvoll, wenn Host-Status != UP).</summary>
    public async Task PerformHostAcknowledgeAsync(string comment)
    {
        var client = _clients.Current;
        if (client is null) return;

        try
        {
            IsBusy = true;
            await client.AcknowledgeHostProblemAsync(HostName, comment);
            StatusMessage = $"Host-Problem acknowledged: {HostName}.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Host-Ack fehlgeschlagen.");
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>Downtime auf den kompletten Host — „ganzer Host in Wartung".</summary>
    public async Task PerformHostDowntimeAsync(string comment, DateTimeOffset start, DateTimeOffset end)
    {
        var client = _clients.Current;
        if (client is null) return;

        try
        {
            IsBusy = true;
            await client.ScheduleHostDowntimeAsync(HostName, start, end, comment);
            StatusMessage = $"Host-Downtime bis {end:HH:mm} gesetzt.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Host-Downtime fehlgeschlagen.");
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>Legt einen neuen Kommentar auf dem Host oder einem gewaehlten Service an.</summary>
    public async Task PerformAddCommentAsync(string comment, bool persistent, bool onSelectedService)
    {
        var client = _clients.Current;
        if (client is null) return;

        try
        {
            IsBusy = true;
            if (onSelectedService && SelectedService is { } svc)
            {
                await client.AddServiceCommentAsync(svc.HostName, svc.Description, comment, persistent);
                StatusMessage = $"Kommentar gespeichert für {svc.Description}.";
            }
            else
            {
                await client.AddHostCommentAsync(HostName, comment, persistent);
                StatusMessage = $"Host-Kommentar gespeichert für {HostName}.";
            }
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Kommentar-Anlage fehlgeschlagen.");
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    // Config kann 404 werfen, wenn der Host nicht (mehr) im Setup ist — Detail-Fenster
    // soll trotzdem oeffnen. In dem Fall bleibt HostConfig null und die UI zeigt "-".
    private async Task<CheckmkObject<HostConfigExtensions>?> SafeGetConfigAsync(
        Checkmk.Core.CheckmkClient client)
    {
        try { return await client.GetHostConfigAsync(HostName); }
        catch (Exception ex)
        {
            Log.Debug(ex, "GetHostConfig fuer {Host} nicht verfuegbar.", HostName);
            return null;
        }
    }

    // Kommentare koennen bei Rechte-Problemen ebenfalls 4xx werfen — dann leere Liste,
    // damit das Detail-Fenster benutzbar bleibt.
    private async Task<IReadOnlyList<CheckmkObject<CommentExtensions>>> SafeGetCommentsAsync(
        Checkmk.Core.CheckmkClient client)
    {
        try { return await client.GetCommentsForHostAsync(HostName); }
        catch (Exception ex)
        {
            Log.Debug(ex, "GetCommentsForHost fuer {Host} nicht verfuegbar.", HostName);
            return [];
        }
    }
}
