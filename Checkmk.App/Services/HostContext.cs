using Checkmk.Data;

namespace Checkmk.App.Services;

/// <summary>
/// Liefert zu einem in Checkmk registrierten Hostnamen den fuer OS-Tools
/// (Ping, RDP, SSH) verwendbaren FQDN: <c>host.domain.tld</c>. Die
/// Zuordnung Host->Domain kommt zentral aus <see cref="IHostDomainStore"/>;
/// fehlt sie, greift die zentrale Default-Domain aus
/// <see cref="IGlobalSettingsProvider"/>.
/// </summary>
public sealed class HostContext
{
    private readonly IHostDomainStore _store;
    private readonly IGlobalSettingsProvider _globals;

    public HostContext(IHostDomainStore store, IGlobalSettingsProvider globals)
    {
        _store = store;
        _globals = globals;
    }

    /// <summary>Aktueller Default aus den zentralen Einstellungen. Kein I/O —
    /// der Provider haelt den Stand im Speicher (Datenbank, sonst Cache,
    /// sonst Vorgabe).</summary>
    public string DefaultDomain => _globals.Current.HostDefaultDomain;

    /// <summary>Domain fuer einen Host (explizit gespeichert oder Default).</summary>
    public string DomainFor(string host)
    {
        var explicitDomain = _store.Load().Hosts
            .FirstOrDefault(h => string.Equals(h.Host, host, StringComparison.OrdinalIgnoreCase))
            ?.Domain;
        return string.IsNullOrWhiteSpace(explicitDomain) ? DefaultDomain : explicitDomain!;
    }

    /// <summary>host.domain.tld — nur wenn der Hostname nicht schon Punkte
    /// enthaelt (dann ist er wahrscheinlich bereits ein FQDN, oder eine IP).</summary>
    public string FqdnFor(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return "";
        if (host.Contains('.')) return host;    // schon FQDN oder IP
        return $"{host}.{DomainFor(host)}";
    }
}
