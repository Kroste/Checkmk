using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;
using Checkmk.App.Services;

namespace Checkmk.App.Views;

public partial class AboutWindow : ChromeWindow
{
    private const string GithubUrl = "https://github.com/Kroste/Checkmk";

    private readonly IUpdateChecker? _updateChecker;
    private readonly IUpdatePreferences? _updatePrefs;

    // Von der DI aufgeloest (Program.cs: AddTransient<AboutWindow>()).
    public AboutWindow(IUpdateChecker updateChecker, IUpdatePreferences updatePrefs)
        : this()
    {
        _updateChecker = updateChecker;
        _updatePrefs = updatePrefs;
    }

    // Parameterloser ctor nur fuer den XAML-Designer.
    public AboutWindow()
    {
        AvaloniaXamlLoader.Load(this);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        this.FindControl<TextBlock>("VersionText")!.Text = $"Version {version}";
    }

    private void OnGithubClick(object? sender, RoutedEventArgs e) => Launch(GithubUrl);
    private void OnDismissClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnCheckUpdatesClick(object? sender, RoutedEventArgs e)
    {
        if (_updateChecker is null)
            return;

        var button = this.FindControl<Button>("CheckUpdatesButton")!;
        var status = this.FindControl<TextBlock>("UpdateStatusText")!;

        button.IsEnabled = false;
        status.IsVisible = true;
        status.Text = "Suche nach Updates\u2026";
        try
        {
            var result = await _updateChecker.CheckManuallyAsync();
            switch (result.Outcome)
            {
                case UpdateCheckOutcome.UpdateAvailable when result.Info is { } info:
                    status.Text = $"Update verf\u00fcgbar: Version {info.Version}.";
                    var choice = await new UpdateDialog(info).ShowDialog<UpdateDialogResult>(this);
                    // Bei "Diese Version ueberspringen" merken, damit der automatische
                    // Startup-Check sie kuenftig nicht erneut meldet.
                    if (choice == UpdateDialogResult.Skip)
                        _updatePrefs?.SaveSkippedVersion(info.Version);
                    break;
                case UpdateCheckOutcome.UpToDate:
                    status.Text = "Du bist auf dem aktuellen Stand.";
                    break;
                default:
                    status.Text = "Update-Pr\u00fcfung fehlgeschlagen \u2013 Details im Log.";
                    break;
            }
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void Launch(string url) => TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(new Uri(url));
}
