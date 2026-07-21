using Microsoft.Extensions.DependencyInjection;

namespace Checkmk.PluginContracts;

/// <summary>
/// Entry-Point eines Plugins. Der Cockpit-<c>PluginLoader</c> instanziiert
/// jede Klasse mit parameterlosem Ctor, die dieses Interface implementiert,
/// liest <see cref="Metadata"/> und ruft <see cref="Register"/> auf.
/// </summary>
/// <remarks>
/// Registration passiert VOR dem Aufbau der Haupt-UI — Plugins registrieren
/// Services im DI-Container und melden Kontributionen (Kontextmenues, Tabs)
/// an. Zur UI-Laufzeit koennen Plugins auf keinen Cockpit-State mehr
/// zugreifen, ausser via die im Register injizierten Services.
/// </remarks>
public interface IPlugin
{
    PluginMetadata Metadata { get; }

    /// <summary>
    /// Wird EINMAL beim Cockpit-Start aufgerufen. Plugin registriert eigene
    /// Services in <paramref name="services"/> (die dann auch fuer andere
    /// Plugins verfuegbar sind, wichtig fuer Plugin-zu-Plugin-Kommunikation)
    /// und meldet Kontributionen ueber die Contribution-Interfaces an.
    /// Der DI-Container wird nach diesem Call gebaut, danach ist die
    /// Registrierung eingefroren.
    /// </summary>
    void Register(IServiceCollection services, IPluginContext context);
}
