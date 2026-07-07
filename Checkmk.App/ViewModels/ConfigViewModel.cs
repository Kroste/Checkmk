using System.Collections.ObjectModel;
using Checkmk.App.Services;
using Checkmk.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;

namespace Checkmk.App.ViewModels;

public sealed partial class ConfigViewModel : ViewModelBase
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly ICheckmkClientProvider _clients;

    public ObservableCollection<CheckmkObject<HostConfigExtensions>> Hosts { get; } = [];

    [ObservableProperty]
    private string _newHostName = "";

    [ObservableProperty]
    private string _newHostFolder = "/";

    [ObservableProperty]
    private string _newHostIp = "";

    [ObservableProperty]
    private string _newHostAlias = "";

    public ConfigViewModel(ICheckmkClientProvider clients) => _clients = clients;

    [RelayCommand]
    private async Task RefreshHostsAsync()
    {
        var client = _clients.Current;
        if (client is null) { StatusMessage = "Nicht konfiguriert."; return; }

        try
        {
            IsBusy = true;
            var hosts = await client.GetHostConfigsAsync();
            Hosts.Clear();
            foreach (var h in hosts.OrderBy(h => h.Id))
                Hosts.Add(h);
            StatusMessage = $"{hosts.Count} Hosts geladen.";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Host-Liste laden fehlgeschlagen.");
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CreateHostAsync()
    {
        var client = _clients.Current;
        if (client is null) { StatusMessage = "Nicht konfiguriert."; return; }
        if (string.IsNullOrWhiteSpace(NewHostName)) { StatusMessage = "Hostname fehlt."; return; }

        try
        {
            IsBusy = true;
            var attrs = new HostAttributes
            {
                IpAddress = string.IsNullOrWhiteSpace(NewHostIp) ? null : NewHostIp,
                Alias = string.IsNullOrWhiteSpace(NewHostAlias) ? null : NewHostAlias
            };
            await client.CreateHostAsync(NewHostName.Trim(), NewHostFolder.Trim(), attrs);
            StatusMessage = $"Host '{NewHostName}' angelegt — nicht vergessen: Änderungen aktivieren.";
            NewHostName = NewHostIp = NewHostAlias = "";
            await RefreshHostsAsync();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Host anlegen fehlgeschlagen.");
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ActivateChangesAsync()
    {
        var client = _clients.Current;
        if (client is null) { StatusMessage = "Nicht konfiguriert."; return; }

        try
        {
            IsBusy = true;
            StatusMessage = "Aktiviere Änderungen…";
            await client.ActivateChangesAsync();
            StatusMessage = "Änderungen aktiviert.";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Activate Changes fehlgeschlagen.");
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }
}
