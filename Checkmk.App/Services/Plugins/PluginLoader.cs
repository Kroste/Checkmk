using System.Reflection;
using Checkmk.App.Services;
using Checkmk.PluginContracts;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace Checkmk.App.Services.Plugins;

/// <summary>
/// Ergebnis eines Plugin-Load-Versuchs. Kein Wurf bei einzelnem Fehler —
/// der Cockpit soll auch bei einem kaputten Plugin starten koennen, dann eben
/// ohne dessen Beitraege.
/// </summary>
public sealed record LoadedPlugin(
    IPlugin Instance,
    PluginMetadata Metadata,
    string SourceFile);

/// <summary>
/// Sucht neben der Exe im Unterordner <c>plugins/</c> nach DLLs, laedt jede
/// gefundene <see cref="IPlugin"/>-Impl und ruft ihre <c>Register(...)</c>-
/// Methode. Der Loader ist bewusst minimal: keine AssemblyLoadContext-
/// Isolation, kein Auto-Update — Plugins liegen als DLLs im Ordner, das
/// reicht fuer die interne Verwendung.
/// </summary>
public static class PluginLoader
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Registriert alle im <paramref name="pluginsDirectory"/> gefundenen
    /// Plugins gegen <paramref name="services"/>. Gibt die geladenen Plugins zurueck
    /// (fuer About-Box, Log). Ist der Ordner nicht da: leere Liste, kein Fehler.</summary>
    public static IReadOnlyList<LoadedPlugin> DiscoverAndRegister(
        string pluginsDirectory,
        IServiceCollection services,
        Func<IPlugin, IPluginContext> contextFactory)
    {
        if (!Directory.Exists(pluginsDirectory))
        {
            Log.Debug("Plugin-Ordner nicht vorhanden: {Dir} — keine Plugins geladen.", pluginsDirectory);
            return [];
        }

        var loaded = new List<LoadedPlugin>();
        foreach (var dll in Directory.GetFiles(pluginsDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            LoadFromFile(dll, services, contextFactory, loaded);
        }

        Log.Info("Plugin-Discovery in {Dir}: {Count} Plugin(s) geladen.", pluginsDirectory, loaded.Count);
        return loaded;
    }

    private static void LoadFromFile(
        string dllPath,
        IServiceCollection services,
        Func<IPlugin, IPluginContext> contextFactory,
        List<LoadedPlugin> sink)
    {
        Assembly asm;
        try
        {
            // Load-From, damit Abhaengigkeiten neben der DLL im plugins-Ordner
            // gefunden werden. Fuer echte Isolation braeuchte man einen eigenen
            // AssemblyLoadContext — bewusst NICHT, weil das Debugging + die
            // Contracts-Version-Reconciliation verkompliziert.
            asm = Assembly.LoadFrom(dllPath);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Plugin-DLL konnte nicht geladen werden: {Path}", dllPath);
            return;
        }

        var pluginTypes = SafeGetTypes(asm)
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IPlugin).IsAssignableFrom(t))
            .ToList();

        if (pluginTypes.Count == 0) return;

        foreach (var type in pluginTypes)
        {
            try
            {
                if (Activator.CreateInstance(type) is not IPlugin plugin)
                    continue;

                var meta = plugin.Metadata;
                var context = contextFactory(plugin);
                plugin.Register(services, context);
                sink.Add(new LoadedPlugin(plugin, meta, dllPath));
                Log.Info("Plugin registriert: {Name} {Version} ({Id}) aus {File}",
                    meta.Name, meta.Version, meta.Id, Path.GetFileName(dllPath));
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Plugin-Registration fehlgeschlagen: {Type} in {File}",
                    type.FullName, Path.GetFileName(dllPath));
            }
        }
    }

    /// <summary>Reflection.GetTypes wirft bei Load-Problemen; wir wollen die
    /// erfolgreich geladenen Typen trotzdem sehen.</summary>
    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        {
            Log.Debug(ex, "Teilweise Ladeprobleme in {Asm} — nutze verfuegbare Typen.", asm.FullName);
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
