using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Checkmk.App.ViewModels;
using Checkmk.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Checkmk.App;

public partial class App : Application
{
    /// <summary>Wird in Program.Main gesetzt, bevor Avalonia startet.</summary>
    public static IServiceProvider Services { get; set; } = default!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = Services.GetRequiredService<MainWindow>();
            var vm = Services.GetRequiredService<MainWindowViewModel>();

            vm.OpenSettingsRequested += async (_, _) =>
            {
                var settings = Services.GetRequiredService<SettingsWindow>();
                await settings.ShowDialog(window);
                await vm.ReconnectAsync();
            };

            vm.OpenAboutRequested += async (_, _) =>
            {
                var about = Services.GetRequiredService<AboutWindow>();
                await about.ShowDialog(window);
            };

            window.DataContext = vm;
            window.Opened += async (_, _) => await vm.InitializeAsync();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
