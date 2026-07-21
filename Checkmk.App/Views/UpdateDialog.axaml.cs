using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Checkmk.App.Controls;
using Checkmk.App.Services;

namespace Checkmk.App.Views;

public enum UpdateDialogResult
{
    Later,
    Skip,
    OpenReleasePage,
    Installed
}

public partial class UpdateDialog : ChromeWindow
{
    private readonly UpdateInfo? _info;
    private readonly UpdateInstaller? _installer;

    public UpdateDialog(UpdateInfo info, UpdateInstaller? installer = null)
    {
        AvaloniaXamlLoader.Load(this);
        _info = info;
        _installer = installer;

        var current = AppVersion.Display;
        this.FindControl<TextBlock>("VersionText")!.Text = $"Version {info.Version}";
        this.FindControl<TextBlock>("CurrentVersionText")!.Text = $"(installiert: {current})";
        this.FindControl<TextBlock>("NotesText")!.Text = string.IsNullOrWhiteSpace(info.ReleaseNotes)
            ? "(keine Release-Notes)"
            : info.ReleaseNotes;

        // Ohne Windows-ZIP im Release oder ohne Installer: "Jetzt installieren"
        // ausblenden und "Release-Seite oeffnen" als Primary markieren.
        var install = this.FindControl<Button>("InstallButton")!;
        var release = this.FindControl<Button>("ReleaseButton")!;
        var canInstall = _installer is not null
                         && !string.IsNullOrEmpty(info.WindowsZipUrl)
                         && OperatingSystem.IsWindows();
        install.IsVisible = canInstall;
        if (!canInstall)
        {
            release.Background = Avalonia.Media.Brushes.SteelBlue;
            release.IsDefault = true;
        }
    }

    // Parameterloser ctor nur fuer den XAML-Designer.
    public UpdateDialog() => AvaloniaXamlLoader.Load(this);

    private void OnLaterClick(object? sender, RoutedEventArgs e) => Close(UpdateDialogResult.Later);
    private void OnSkipClick(object? sender, RoutedEventArgs e) => Close(UpdateDialogResult.Skip);

    private void OnOpenReleaseClick(object? sender, RoutedEventArgs e)
    {
        if (_info is not null && !string.IsNullOrEmpty(_info.ReleasePageUrl))
            TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(new Uri(_info.ReleasePageUrl));
        Close(UpdateDialogResult.OpenReleasePage);
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (_info is null || _installer is null) return;

        var buttonRow = this.FindControl<StackPanel>("ButtonRow")!;
        var progressPanel = this.FindControl<StackPanel>("ProgressPanel")!;
        var bar = this.FindControl<ProgressBar>("DownloadBar")!;
        var text = this.FindControl<TextBlock>("ProgressText")!;

        foreach (var b in buttonRow.Children.OfType<Button>())
            b.IsEnabled = false;
        progressPanel.IsVisible = true;
        text.Text = "Lade Update herunter…";

        var progress = new Progress<double>(p =>
            Dispatcher.UIThread.Post(() =>
            {
                bar.Value = p;
                text.Text = $"Lade Update herunter… {p * 100:F0} %";
            }));

        bool ok;
        try
        {
            ok = await _installer.DownloadAndApplyAsync(_info, progress);
        }
        catch (Exception ex)
        {
            text.Text = $"Fehler: {ex.Message}";
            foreach (var b in buttonRow.Children.OfType<Button>())
                b.IsEnabled = true;
            return;
        }

        if (!ok)
        {
            text.Text = "Update konnte nicht angewendet werden — siehe Log. Fallback: Release-Seite öffnen.";
            foreach (var b in buttonRow.Children.OfType<Button>())
                b.IsEnabled = true;
            return;
        }

        // Der externe Austausch-Prozess wartet auf das Prozess-Ende dieser
        // App. Wir geben dem Fenster einen Moment zum Schliessen und beenden
        // dann hart — die .bat ersetzt und startet neu.
        text.Text = "Update wird angewendet, App wird beendet…";
        Close(UpdateDialogResult.Installed);
        await Task.Delay(300);
        Environment.Exit(0);
    }
}
