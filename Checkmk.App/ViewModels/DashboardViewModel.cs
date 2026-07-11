using System.Collections.ObjectModel;
using Checkmk.App.Models;
using Checkmk.App.Services;
using Checkmk.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;

namespace Checkmk.App.ViewModels;

/// <summary>
/// Dashboard-Kachel pro Favorit — Aggregat-Sicht: „DB-Server: 2 CRIT / 3 WARN / 42 OK".
/// Klick aktiviert diesen Filter im Status-Tab und wechselt dorthin.
/// </summary>
public sealed partial class DashboardTile : ObservableObject
{
    public required string FavoriteName { get; init; }
    public required HostFilter Filter { get; init; }

    [ObservableProperty] private int _hostsTotal;
    [ObservableProperty] private int _servicesOk;
    [ObservableProperty] private int _servicesWarn;
    [ObservableProperty] private int _servicesCrit;
    [ObservableProperty] private int _servicesUnknown;

    /// <summary>Fuer die Kachel-Randfarbe: schlechtester Status im Scope.</summary>
    public ServiceState WorstState =>
        ServicesCrit > 0 ? ServiceState.Critical :
        ServicesWarn > 0 ? ServiceState.Warning :
        ServicesUnknown > 0 ? ServiceState.Unknown :
        ServiceState.Ok;
}

public sealed partial class DashboardViewModel : ViewModelBase
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly ICheckmkClientProvider _clients;
    private readonly HostFilterCollection _filters;

    public ObservableCollection<DashboardTile> Tiles { get; } = [];

    public HostFilterCollection Filters => _filters;

    public event EventHandler<HostFilter>? TileClicked;

    public DashboardViewModel(ICheckmkClientProvider clients, HostFilterCollection filters)
    {
        _clients = clients;
        _filters = filters;
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

        var candidates = _filters.Filters.ToList();
        if (candidates.Count == 0)
        {
            StatusMessage = "Keine Favoriten angelegt — nichts zu zeigen.";
            Tiles.Clear();
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Aktualisiere Dashboard…";

            Tiles.Clear();
            foreach (var fav in candidates)
            {
                var lsf = fav.ToLivestatus();
                var hosts = await client.GetHostStatusesAsync(lsf);
                var services = await client.GetServiceStatusesAsync(lsf);
                Tiles.Add(new DashboardTile
                {
                    FavoriteName = fav.Name,
                    Filter = fav,
                    HostsTotal = hosts.Count,
                    ServicesOk = services.Count(s => s.ServiceState == ServiceState.Ok),
                    ServicesWarn = services.Count(s => s.ServiceState == ServiceState.Warning),
                    ServicesCrit = services.Count(s => s.ServiceState == ServiceState.Critical),
                    ServicesUnknown = services.Count(s => s.ServiceState == ServiceState.Unknown),
                });
            }

            StatusMessage = $"Aktualisiert {DateTime.Now:HH:mm:ss} — {candidates.Count} Favoriten.";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Dashboard-Refresh fehlgeschlagen.");
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    public void OnTileClicked(DashboardTile tile) => TileClicked?.Invoke(this, tile.Filter);
}
