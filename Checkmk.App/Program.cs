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

        services.AddSingleton<ISecretProtector>(_ => SecretProtectorFactory.Create());
        services.AddSingleton<IConnectionSettingsStore>(_ =>
            new ConnectionSettingsStore(SecretProtectorFactory.CreateForSharedConnection()));
        services.AddSingleton<ICheckmkClientProvider, CheckmkClientProvider>();
        services.AddSingleton<IToastNotifier, WindowsToastNotifier>();
        services.AddSingleton<IHostFilterStore, HostFilterStore>();
        services.AddSingleton<HostFilterCollection>();
        services.AddSingleton<IStatusViewStateStore, StatusViewStateStore>();
        services.AddSingleton<CheckmkWebLinker>();
        services.AddSingleton<IHostDomainStore, HostDomainStore>();
        services.AddSingleton<HostContext>();
        services.AddSingleton<ISshCredentialStore, SshCredentialStore>();
        services.AddSingleton<RemoteTools>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<IUpdatePreferences, UpdatePreferences>();
        services.AddSingleton<IUpdateChecker>(sp =>
        {
            // Update-Check laeuft ins Internet -> ueber den Firmen-Proxy. Ohne
            // Proxy-Credentials gibt der FortiProxy 407. DefaultCredentials nutzt
            // den angemeldeten Windows-User (Negotiate/NTLM).
            var handler = new HttpClientHandler
            {
                UseProxy = true,
                DefaultProxyCredentials = System.Net.CredentialCache.DefaultCredentials
            };
            var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            var url = Bootstrap.LoadOrCreate().UpdateChannelUrl;
            return new GitHubReleasesUpdateChecker(http, url,
                sp.GetRequiredService<IUpdatePreferences>());
        });

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
