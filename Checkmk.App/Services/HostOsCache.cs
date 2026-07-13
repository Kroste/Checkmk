using System.Collections.Concurrent;
using Checkmk.Core.Models;
using NLog;

namespace Checkmk.App.Services;

/// <summary>
/// Prozessweiter Cache OS-Familie je Host — gefuellt aus Custom-/Effective-
/// Host-Attributes (z. B. Folder-Vererbung "Operation System" = Windows/Linux).
/// Wird typischerweise vom <see cref="ViewModels.ConfigViewModel"/> beim Refresh
/// aktualisiert; der Status-Tab liest daraus.
/// </summary>
public interface IHostOsCache
{
    /// <summary>Attributes vom Server anwenden — pro Host wird die OS-Familie
    /// aus der Bootstrap-Kandidatenliste extrahiert und im Cache abgelegt.</summary>
    void ApplyFromHostConfigs(IEnumerable<CheckmkObject<HostConfigExtensions>> hosts);

    OsFamily OsFor(string hostName);

    bool IsEmpty { get; }
}

public sealed class HostOsCache : IHostOsCache
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<string, OsFamily> _byHost = new(StringComparer.OrdinalIgnoreCase);
    private bool _keysLoggedOnce;

    public bool IsEmpty => _byHost.IsEmpty;

    public OsFamily OsFor(string hostName)
        => _byHost.TryGetValue(hostName, out var os) ? os : OsFamily.Unknown;

    public void ApplyFromHostConfigs(IEnumerable<CheckmkObject<HostConfigExtensions>> hosts)
    {
        var keys = Bootstrap.LoadOrCreate().HostOsAttributeKeys;

        var count = 0;
        var updated = 0;
        var sample = default(CheckmkObject<HostConfigExtensions>);
        foreach (var h in hosts)
        {
            sample ??= h;
            count++;
            if (string.IsNullOrEmpty(h.Id)) continue;
            var os = OsDetection.ResolveFromAttributes(h.Extensions?.Attributes, keys);
            if (os == OsFamily.Unknown)
            {
                _byHost.TryRemove(h.Id, out _);
                continue;
            }
            _byHost[h.Id] = os;
            updated++;
        }

        // Einmalig die tatsaechlich gesehenen Attribute-Keys ins Debug-Log —
        // damit im Fehlerfall (kein Kandidat greift) klar ist, wie das Attribut
        // wirklich heisst und in bootstrap.json angepasst werden kann.
        if (!_keysLoggedOnce && sample?.Extensions?.Attributes?.AdditionalProperties is { Count: > 0 } props)
        {
            Log.Debug("HostOsCache: Beispiel-Host '{Host}' hat Attribut-Keys: {Keys}. Aktive Kandidaten: {Cands}.",
                sample.Id,
                string.Join(", ", props.Keys.OrderBy(k => k)),
                string.Join(", ", keys));
            _keysLoggedOnce = true;
        }

        Log.Info("HostOsCache aktualisiert: {Updated}/{Total} Hosts mit OS-Familie.", updated, count);
    }
}
