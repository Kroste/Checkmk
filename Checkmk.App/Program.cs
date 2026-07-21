using Avalonia;
using Checkmk.App.Services;
using Checkmk.App.Services.Plugins;
using Checkmk.App.ViewModels;
using Checkmk.App.Views;
using Checkmk.PluginContracts;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace Checkmk.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var logger = LogManager.Setup()
            .SetupExtensions(e => e.RegisterLayoutRenderer<Checkmk.App.Services.MaskedLayoutRenderer>("masked"))
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
            new ConnectionSettingsStore(SecretProtectorFactory.Create()));
        services.AddSingleton<ICheckmkClientProvider, CheckmkClientProvider>();
        services.AddSingleton<IToastNotifier, WindowsToastNotifier>();
        services.AddSingleton<IHostFilterStore, HostFilterStore>();
        services.AddSingleton<HostFilterCollection>();
        services.AddSingleton<IStatusViewStateStore, StatusViewStateStore>();
        services.AddSingleton<CheckmkWebLinker>();
        services.AddSingleton<IHostDomainStore, HostDomainStore>();
        services.AddSingleton<IHostOsCache, HostOsCache>();
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

        // ---- Plugins entdecken und registrieren ----
        // Ordner "plugins/" neben der Exe. Plugins koennen im Register eigene
        // Services im DI-Container registrieren und Contributions (IContextMenu-,
        // ITabContribution) beisteuern. Auf Cockpit-Services zugreifen sollten
        // sie erst zur Laufzeit ueber IPluginContext.Services — der Late-Bind-
        // Wrapper macht den DI-Container nach BuildServiceProvider verfuegbar.
        var appDir = AppContext.BaseDirectory;
        var pluginsDir = Path.Combine(appDir, "plugins");
        var pluginDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kroste", "Checkmk", "plugins");

        var lateBound = new LateBoundServiceProvider();
        var contextFactory = (IPlugin plugin) =>
        {
            var dataDir = Path.Combine(pluginDataRoot, plugin.Metadata.Id);
            Directory.CreateDirectory(dataDir);
            return (IPluginContext)new PluginContext(
                lateBound,
                Services.AppVersion.Display,
                dataDir);
        };

        var loadedPlugins = PluginLoader.DiscoverAndRegister(pluginsDir, services, contextFactory);
        services.AddSingleton<IReadOnlyList<LoadedPlugin>>(loadedPlugins);

        var provider = services.BuildServiceProvider();
        lateBound.SetInner(provider);
        return provider;
    }

    /// <summary>
    /// Provider-Wrapper, den der <c>PluginContext</c> beim Register bekommt.
    /// Der echte DI-Container existiert erst NACH <c>BuildServiceProvider</c>;
    /// bis dahin blockt <c>GetService</c>-Aufruf durch Plugins mit NRE (was ein
    /// Bug im Plugin waere — Register darf keine anderen Services aufloesen).
    /// </summary>
    private sealed class LateBoundServiceProvider : IServiceProvider
    {
        private IServiceProvider? _inner;
        public void SetInner(IServiceProvider inner) => _inner = inner;
        public object? GetService(Type serviceType)
        {
            if (_inner is null)
                throw new InvalidOperationException(
                    "Plugin greift auf Cockpit-Services zu, bevor der DI-Container fertig gebaut wurde. " +
                    "IPluginContext.Services darf im Register(...) nicht benutzt werden.");
            return _inner.GetService(serviceType);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
