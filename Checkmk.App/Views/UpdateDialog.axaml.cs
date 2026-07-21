using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;
using Checkmk.App.Services;

namespace Checkmk.App.Views;

public enum UpdateDialogResult
{
    Later,
    Skip,
    OpenReleasePage
}

public partial class UpdateDialog : ChromeWindow
{
    private readonly UpdateInfo? _info;

    public UpdateDialog(UpdateInfo info)
    {
        AvaloniaXamlLoader.Load(this);
        _info = info;

        var current = AppVersion.Display;
        this.FindControl<TextBlock>("VersionText")!.Text = $"Version {info.Version}";
        this.FindControl<TextBlock>("CurrentVersionText")!.Text = $"(installiert: {current})";
        this.FindControl<TextBlock>("NotesText")!.Text = string.IsNullOrWhiteSpace(info.ReleaseNotes)
            ? "(keine Release-Notes)"
            : info.ReleaseNotes;
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
}
