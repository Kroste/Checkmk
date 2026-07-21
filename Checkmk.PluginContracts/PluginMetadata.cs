namespace Checkmk.PluginContracts;

/// <summary>Metadaten eines Plugins — vom Cockpit fuer About-Anzeige, Log und
/// Konflikt-Detection genutzt.</summary>
public sealed record PluginMetadata(
    string Id,
    string Name,
    string Version,
    string Description = "",
    string Author = "");
