using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Checkmk.App.Controls;

namespace Checkmk.App.Views;

public partial class AboutWindow : ChromeWindow
{
    private const string GithubUrl = "https://github.com/Kroste/Checkmk";
    private const string BuyMeCoffeeUrl = "https://www.buymeacoffee.com/kroste";

    public AboutWindow()
    {
        AvaloniaXamlLoader.Load(this);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        this.FindControl<TextBlock>("VersionText")!.Text = $"Version {version}";
    }

    private void OnGithubClick(object? sender, RoutedEventArgs e) => Launch(GithubUrl);
    private void OnBuyMeCoffeeClick(object? sender, RoutedEventArgs e) => Launch(BuyMeCoffeeUrl);

    private void Launch(string url) => TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(new Uri(url));
}
