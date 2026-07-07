using Avalonia;
using Checkmk.App.Services;
using Checkmk.App.ViewModels;
using Checkmk.App.Views;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace Checkmk.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var logger = LogManager.Setup()
            .LoadConfigurationFromFile("nlog.config", optional: true)
            .GetCurrentClassLogger();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.Error(e.ExceptionObject as Exception, "Unbehandelte Ausnahme (AppDomain).");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.Error(e.Exception, "Unbeobachtete Task-Ausnahme.");
            e.SetObserved();
        };

        try
        {
            App.Services = BuildServiceProvider();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "App wurde durch eine Ausnahme beendet.");
        }
        finally
        {
            LogManager.Shutdown();
        }
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IConnectionSettingsStore, ConnectionSettingsStore>();
        services.AddSingleton<ICheckmkClientProvider, CheckmkClientProvider>();

        services.AddSingleton<StatusViewModel>();
        services.AddSingleton<ConfigViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<SettingsViewModel>();

        services.AddSingleton<MainWindow>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<AboutWindow>();

        return services.BuildServiceProvider();
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
