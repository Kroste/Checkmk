using Checkmk.App.Services;
using Checkmk.PluginContracts;

namespace Checkmk.App.Services.Plugins;

/// <summary>Konkrete <see cref="IPluginContext"/>-Impl, die dem Plugin waehrend
/// <c>Register(...)</c> uebergeben wird.</summary>
internal sealed class PluginContext : IPluginContext
{
    public PluginContext(IServiceProvider services, string cockpitVersion, string pluginDataDirectory)
    {
        Services = services;
        CockpitVersion = cockpitVersion;
        PluginDataDirectory = pluginDataDirectory;
    }

    public IServiceProvider Services { get; }
    public string CockpitVersion { get; }
    public string PluginDataDirectory { get; }
}
