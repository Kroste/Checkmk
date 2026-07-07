using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;

namespace Checkmk.App.Views;

public partial class AboutWindow : ChromeWindow
{
    private const string GithubUrl = "https://github.com/Kroste/Checkmk";

    public AboutWindow()
    {
        AvaloniaXamlLoader.Load(this);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        this.FindControl<TextBlock>("VersionText")!.Text = $"Version {version}";
    }

    private void OnGithubClick(object? sender, RoutedEventArgs e) => Launch(GithubUrl);
    private void OnDismissClick(object? sender, RoutedEventArgs e) => Close();

    private void Launch(string url) => TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(new Uri(url));
}
