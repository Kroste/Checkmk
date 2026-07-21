namespace Checkmk.PluginContracts;

/// <summary>
/// Runtime-Kontext, den das Cockpit an Plugins uebergibt. Zugriff auf
/// Cockpit-Services (via <see cref="Services"/>), Metadaten des laufenden
/// Cockpits und einen plugin-eigenen Datenordner fuer Persistenz.
/// </summary>
public interface IPluginContext
{
    /// <summary>DI-Container des laufenden Cockpits. Plugins koennen daraus
    /// Cockpit-Services aufloesen — z. B. <c>ICheckmkClientProvider</c>,
    /// <c>ISshCredentialStore</c>, <c>IHostDomainStore</c>. Konkrete Contract-
    /// Typen dieser Services stehen in <c>Checkmk.PluginContracts.Services</c>.</summary>
    IServiceProvider Services { get; }

    /// <summary>Version des laufenden Cockpits (SemVer, z. B. "1.7.0").
    /// Plugins pruefen damit Kompatibilitaet und lehnen ggf. das Laden ab.</summary>
    string CockpitVersion { get; }

    /// <summary>Verzeichnis fuer plugin-eigene Persistenz (Settings, Cache),
    /// user-lokal. Wird bei Bedarf angelegt. Typisch:
    /// <c>%APPDATA%\Kroste\Checkmk\plugins\&lt;plugin-id&gt;\</c>.</summary>
    string PluginDataDirectory { get; }
}
