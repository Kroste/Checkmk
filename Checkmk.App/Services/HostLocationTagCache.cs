using System.Collections.Concurrent;
using Checkmk.Core.Models;
using Checkmk.Data;
using NLog;

namespace Checkmk.App.Services;

/// <summary>Ein vorkommender Tag-Wert und wie viele Hosts ihn tragen.</summary>
public sealed record HostTagValue(string Value, int HostCount)
{
    /// <summary>Beschriftung für Auswahllisten.</summary>
    public string Display => $"{Value} ({HostCount} Hosts)";
}

/// <summary>
/// Prozessweiter Cache „Ortstag je Host" — gefüllt aus denselben
/// Host-Attributes, aus denen auch <see cref="IHostOsCache"/> die OS-Familie
/// zieht.
///
/// Der Sinn: Checkmk pflegt bei uns auf der Site <c>schul_it</c> das Attribut
/// <c>tag_location_school</c> mit Werten wie <c>schule_46</c> — gemessen am
/// 2026-08-21 auf 553 von 654 Hosts, 51 verschiedene Werte, keiner mehrdeutig.
/// Das ist eine gepflegte Angabe und damit eine bessere Zuordnungsquelle als
/// ein Regex auf den Hostnamen, der dieselbe Information nur <i>erschließt</i>.
/// Er trifft auch Hosts, die aus der Namenskonvention fallen: <c>WLC-01SL-01</c>
/// gehört zu <c>schule_01</c>, ohne dass „01" als eigenständige Zahl im Namen
/// steht.
///
/// Auf der Site <c>LHP</c> gibt es solche Tags praktisch nicht (<c>tag_location</c>
/// steht auf 9 von 1438 Hosts) — dort bleibt das Namensmuster der Weg. Beide
/// Verfahren stehen deshalb nebeneinander.
/// </summary>
public interface IHostLocationTags
{
    /// <summary>Attributes vom Server anwenden.</summary>
    void ApplyFromHostConfigs(IEnumerable<CheckmkObject<HostConfigExtensions>> hosts);

    /// <summary>Ortstag des Hosts, oder <c>null</c>.</summary>
    string? TagFor(string hostName);

    /// <summary>Alle vorkommenden Werte mit Host-Zahl, absteigend nach Häufigkeit.</summary>
    IReadOnlyList<HostTagValue> Values { get; }

    bool IsEmpty { get; }
}

public sealed class HostLocationTagCache(IGlobalSettingsProvider globals) : IHostLocationTags
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<string, string> _byHost = new(StringComparer.OrdinalIgnoreCase);
    private volatile IReadOnlyList<HostTagValue> _values = [];

    public bool IsEmpty => _byHost.IsEmpty;

    public IReadOnlyList<HostTagValue> Values => _values;

    public string? TagFor(string hostName)
        => _byHost.TryGetValue(hostName, out var tag) ? tag : null;

    public void ApplyFromHostConfigs(IEnumerable<CheckmkObject<HostConfigExtensions>> hosts)
    {
        var keys = globals.Current.HostLocationTagKeys;
        if (keys.Count == 0) return;

        // Nicht in den bestehenden Bestand hineinschreiben, sondern neu
        // aufbauen: Ein Host, dem jemand das Tag wegnimmt, muss es auch hier
        // verlieren — sonst schlaegt das Cockpit ewig eine Zuordnung vor, die
        // in Checkmk schon aufgehoben ist.
        var fresh = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var total = 0;

        foreach (var h in hosts)
        {
            total++;
            if (string.IsNullOrEmpty(h.Id)) continue;
            if (ResolveTag(h.Extensions?.Attributes, keys) is { } tag)
                fresh[h.Id] = tag;
        }

        _byHost.Clear();
        foreach (var (host, tag) in fresh) _byHost[host] = tag;

        _values = [.. fresh.GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Select(g => new HostTagValue(g.Key, g.Count()))
            .OrderByDescending(v => v.HostCount)
            .ThenBy(v => v.Value, StringComparer.OrdinalIgnoreCase)];

        Log.Info("Ortstags gelesen: {Tagged}/{Total} Hosts, {Distinct} verschiedene Werte "
               + "(Schluessel: {Keys}).", fresh.Count, total, _values.Count, string.Join(", ", keys));
    }

    /// <summary>
    /// Erster Schlüssel aus der Kandidatenliste, den der Host trägt, gewinnt —
    /// dasselbe Muster wie bei den OS-Attributen. Die Reihenfolge in
    /// <c>HostLocationTagKeys</c> ist damit die Rangfolge: Ein Host mit
    /// <c>tag_location_school</c> <b>und</b> <c>tag_location</c> zählt als Schule.
    /// </summary>
    internal static string? ResolveTag(HostAttributes? attributes, IReadOnlyList<string> keys)
    {
        var props = attributes?.AdditionalProperties;
        if (props is null || props.Count == 0) return null;

        foreach (var key in keys)
        {
            foreach (var (name, value) in props)
            {
                if (!name.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                var text = value.ValueKind == System.Text.Json.JsonValueKind.String
                    ? value.GetString()
                    : value.ToString();
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
        }

        return null;
    }
}
