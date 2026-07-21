using Checkmk.App.Services;
using Checkmk.Core;
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
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _secret = "";
    [ObservableProperty] private bool _useHttps = true;
    [ObservableProperty] private bool _ignoreCertificateErrors;

    /// <summary>Weitere Sites am selben Server (kommasepariert) — z. B. "LHP-Prod, Schul_IT".</summary>
    [ObservableProperty] private string _knownSitesCsv = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUserBasic))]
    [NotifyPropertyChangedFor(nameof(IsAutomationBearer))]
    [NotifyPropertyChangedFor(nameof(UsernameLabel))]
    [NotifyPropertyChangedFor(nameof(SecretLabel))]
    [NotifyPropertyChangedFor(nameof(UsernameHint))]
    [NotifyPropertyChangedFor(nameof(SecretHint))]
    private CheckmkAuthMode _authMode;

    /// <summary>Two-way-bindable Convenience fuer die RadioButtons.</summary>
    public bool IsUserBasic
    {
        get => AuthMode == CheckmkAuthMode.UserBasic;
        set { if (value) AuthMode = CheckmkAuthMode.UserBasic; }
    }

    public bool IsAutomationBearer
    {
        get => AuthMode == CheckmkAuthMode.AutomationBearer;
        set { if (value) AuthMode = CheckmkAuthMode.AutomationBearer; }
    }

    public string UsernameLabel => IsUserBasic ? "Windows-/LDAP-Anmeldename" : "Automation-User";
    public string SecretLabel => IsUserBasic ? "Windows-Passwort (LDAP)" : "Automation-Secret";
    public string UsernameHint => IsUserBasic
        ? $"Default: dein Windows-User ({Environment.UserName}). Damit taucht dein Name in Checkmks Audit-Log auf."
        : "Dedizierter Automation-User (nicht personengebunden).";
    public string SecretHint => IsUserBasic
        ? "Dein AD-Passwort (nicht das GUI-Passwort eines Automation-Users). Wird DPAPI-verschlüsselt lokal gespeichert."
        : "Automation-Secret aus der User-Verwaltung in Checkmk — nicht das GUI-Passwort.";

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
        AuthMode = s.AuthMode;
        // Bei erstmaliger Einrichtung (kein User gespeichert) Windows-User vorbelegen.
        Username = string.IsNullOrWhiteSpace(s.Username) ? Environment.UserName : s.Username;
        UseHttps = s.UseHttps;
        IgnoreCertificateErrors = s.IgnoreCertificateErrors;
        Secret = _store.LoadSecret(s) ?? "";
        KnownSitesCsv = string.Join(", ", s.KnownSites);

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
        AuthMode = AuthMode,
        UseHttps = UseHttps,
        IgnoreCertificateErrors = IgnoreCertificateErrors,
        KnownSites = ParseSitesCsv(KnownSitesCsv)
    };

    private static List<string> ParseSitesCsv(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return csv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
