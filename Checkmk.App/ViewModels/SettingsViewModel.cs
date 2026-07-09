using Checkmk.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;

namespace Checkmk.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IConnectionSettingsStore _store;
    private readonly ICheckmkClientProvider _clients;

    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _site = "";
    [ObservableProperty] private string _username = "automation";
    [ObservableProperty] private string _secret = "";
    [ObservableProperty] private bool _useHttps = true;
    [ObservableProperty] private bool _ignoreCertificateErrors;
    [ObservableProperty] private string _agentShare = "";
    [ObservableProperty] private string _agentUpdateScript = "";

    public string StorageLocationLabel { get; }

    /// <summary>Wird true, sobald erfolgreich gespeichert wurde (Fenster kann schliessen).</summary>
    public bool Saved { get; private set; }

    public event EventHandler? RequestClose;

    public SettingsViewModel(IConnectionSettingsStore store, ICheckmkClientProvider clients)
    {
        _store = store;
        _clients = clients;

        var s = _store.Load();
        Host = s.Host;
        Site = s.Site;
        Username = s.Username;
        UseHttps = s.UseHttps;
        IgnoreCertificateErrors = s.IgnoreCertificateErrors;
        Secret = _store.LoadSecret(s) ?? "";
        AgentShare = s.AgentShare;
        AgentUpdateScript = s.AgentUpdateScript;

        var isShared = _store.SettingsFilePath.StartsWith(@"\\", StringComparison.Ordinal);
        StorageLocationLabel = isShared
            ? $"Zentrale Datei: {_store.SettingsFilePath}"
            : $"Lokale Datei: {_store.SettingsFilePath}";
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Teste Verbindung…";
            var settings = BuildSettings();
            _clients.Configure(settings, Secret);
            var ver = await _clients.Current!.GetVersionAsync();
            StatusMessage = $"OK — {ver.Edition} {ver.Versions?.Checkmk} (Site {ver.Site}).";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Verbindungstest fehlgeschlagen.");
            StatusMessage = $"Fehlgeschlagen: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Save()
    {
        var settings = BuildSettings();
        _store.Save(settings, Secret);
        _clients.Configure(settings, Secret);
        Saved = true;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, EventArgs.Empty);

    private ConnectionSettings BuildSettings() => new()
    {
        Host = Host.Trim(),
        Site = Site.Trim(),
        Username = Username.Trim(),
        UseHttps = UseHttps,
        IgnoreCertificateErrors = IgnoreCertificateErrors,
        AgentShare = AgentShare.Trim(),
        AgentUpdateScript = AgentUpdateScript
    };
}
