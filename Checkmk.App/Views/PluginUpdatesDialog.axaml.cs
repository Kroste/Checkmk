using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Checkmk.App.Controls;
using Checkmk.App.Services;
using NLog;

namespace Checkmk.App.Views;

public partial class PluginUpdatesDialog : ChromeWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly PluginUpdateService? _service;
    private readonly PluginUpdateInstaller? _installer;
    private List<PluginUpdateInfo> _lastResults = new();

    public PluginUpdatesDialog(PluginUpdateService service, PluginUpdateInstaller? installer)
    {
        AvaloniaXamlLoader.Load(this);
        _service = service;
        _installer = installer;

        Opened += async (_, _) => await CheckAsync();
    }

    // Parameterloser Ctor nur fuer den XAML-Designer.
    public PluginUpdatesDialog() => AvaloniaXamlLoader.Load(this);

    private async Task CheckAsync()
    {
        if (_service is null) return;
        var status = this.FindControl<TextBlock>("StatusText")!;
        var list = this.FindControl<ItemsControl>("UpdatesList")!;
        var install = this.FindControl<Button>("InstallButton")!;

        status.Text = "Prüfe Plugin-Updates…";
        install.IsEnabled = false;

        List<PluginUpdateInfo> results;
        try
        {
            results = (await _service.CheckAllAsync()).ToList();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Plugin-Update-Check fehlgeschlagen.");
            status.Text = $"Fehler beim Prüfen: {ex.Message}";
            return;
        }

        _lastResults = results;
        list.ItemsSource = results;

        if (results.Count == 0)
        {
            status.Text = "Kein installiertes Plugin hat eine Update-URL — nichts zu prüfen.";
            return;
        }

        var available = results.Count(r => r.UpdateAvailable);
        if (available == 0)
        {
            status.Text = $"Alle {results.Count} Plugins sind aktuell.";
            return;
        }

        status.Text = $"{available} von {results.Count} Plugins haben ein Update verfügbar.";
        install.IsEnabled = _installer is not null && OperatingSystem.IsWindows();
    }

    private async void OnRecheckClick(object? sender, RoutedEventArgs e) => await CheckAsync();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (_installer is null) return;
        var updates = _lastResults.Where(r => r.UpdateAvailable).ToList();
        if (updates.Count == 0) return;

        var status = this.FindControl<TextBlock>("StatusText")!;
        var install = this.FindControl<Button>("InstallButton")!;
        var recheck = this.FindControl<Button>("RecheckButton")!;
        var progressPanel = this.FindControl<StackPanel>("ProgressPanel")!;
        var bar = this.FindControl<ProgressBar>("DownloadBar")!;
        var progressText = this.FindControl<TextBlock>("ProgressText")!;

        install.IsEnabled = false;
        recheck.IsEnabled = false;
        progressPanel.IsVisible = true;
        status.Text = $"Lade {updates.Count} Plugin-Update(s) herunter…";

        var progress = new Progress<double>(p =>
            Dispatcher.UIThread.Post(() =>
            {
                bar.Value = p;
                progressText.Text = $"Download… {p * 100:F0} %";
            }));

        bool ok;
        try
        {
            ok = await _installer.DownloadAndApplyAsync(updates, progress);
        }
        catch (Exception ex)
        {
            status.Text = $"Fehler: {ex.Message}";
            install.IsEnabled = true;
            recheck.IsEnabled = true;
            return;
        }

        if (!ok)
        {
            status.Text = "Update konnte nicht angewendet werden — siehe Log.";
            install.IsEnabled = true;
            recheck.IsEnabled = true;
            return;
        }

        status.Text = "Update wird angewendet, Cockpit wird neu gestartet…";
        await Task.Delay(300);
        Environment.Exit(0);
    }
}
