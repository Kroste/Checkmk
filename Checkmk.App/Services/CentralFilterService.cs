using System.Text.Json;
using Checkmk.App.Models;
using Checkmk.Data;
using NLog;

namespace Checkmk.App.Services;

/// <summary>Woher der aktuelle Filtersatz stammt.</summary>
public enum FilterOrigin
{
    /// <summary>Kein Datenbankzugang — rein persönlich aus <c>filter.json</c>.</summary>
    Local,

    /// <summary>Aus der zentralen Datenbank, schreibbar.</summary>
    Central,

    /// <summary>Datenbank nicht erreichbar — letzter bekannter Stand, nur lesbar.</summary>
    Cache
}

/// <summary>
/// Host-Filter aus der zentralen Datenbank, mit Ausfall-Cache.
///
/// Der Alltagsgewinn: Heute baut sich jeder der 48 seinen eigenen Filter, und
/// wenn der Netzwerkkollege im Urlaub ist, fängt die Vertretung bei null an.
/// Ein Team-Filter wird einmal gebaut und gilt für alle im Team.
///
/// <para><b>Bei Ausfall wird nicht geschrieben.</b> Anders als bei den globalen
/// Einstellungen, die nur gelesen werden, sind Filter bearbeitbar — und eine
/// Änderung, die nur im lokalen Cache landet, wäre beim nächsten erfolgreichen
/// Laden lautlos wieder weg. Lieber sagen „gerade nur lesbar" als eine Änderung
/// annehmen, die niemand wiedersieht.</para>
/// </summary>
public sealed class CentralFilterService(
    IFilterStore filters,
    ITeamStore teams,
    string cachePath,
    string userName)
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>Zuletzt geladener Stand — Grundlage für den Diff beim Speichern.</summary>
    private List<HostFilter> _loaded = [];
    private string _site = "";

    public FilterOrigin Origin { get; private set; } = FilterOrigin.Cache;

    /// <summary>Schreiben geht nur gegen die echte Datenbank.</summary>
    public bool CanWrite => Origin == FilterOrigin.Central;

    /// <summary>Alle Teams — für die Auswahl „gehört zu" im Filter-Manager.</summary>
    public IReadOnlyList<TeamRow> Teams => teams.Current.Teams;

    /// <summary>Darf dieser Anwender Teams verwalten? Leere Admin-Tabelle = jeder.</summary>
    public bool IsAdmin => teams.Current.IsAdmin(userName);

    public string UserName => userName;

    /// <summary>Kurzfassung für die Statuszeile.</summary>
    public string StatusHint => Origin switch
    {
        FilterOrigin.Central => "Filter: zentral",
        FilterOrigin.Cache => "Filter: Cache (nur lesbar)",
        _ => "Filter: lokal"
    };

    /// <summary>
    /// Lädt die Filter dieser Site. <paramref name="legacy"/> sind die aus
    /// <c>filter.json</c>; sie werden genau einmal übernommen, nämlich wenn
    /// dieser Anwender in dieser Site noch keinen persönlichen Filter in der
    /// Datenbank hat.
    /// </summary>
    public async Task<IReadOnlyList<HostFilter>> LoadAsync(string site,
        IReadOnlyList<HostFilter> legacy, CancellationToken ct = default)
    {
        _site = site;

        try
        {
            await teams.RefreshAsync(ct).ConfigureAwait(false);

            var imported = await filters.ImportLegacyIfEmptyAsync(site, userName,
                [.. legacy.Select(f => ToShared(f, site))], ct).ConfigureAwait(false);
            if (imported > 0)
                Log.Info("{Count} Filter aus filter.json uebernommen — ab jetzt gilt die Datenbank.",
                    imported);

            var rows = await filters.LoadAsync(site, userName, ct).ConfigureAwait(false);
            _loaded = [.. rows.Select(ToModel)];
            Origin = FilterOrigin.Central;
            WriteCache(site, _loaded);
            return Snapshot();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Zentrale Filter nicht lesbar — greife auf den Cache zurueck.");
            _loaded = ReadCache(site);
            Origin = FilterOrigin.Cache;
            return Snapshot();
        }
    }

    /// <summary>
    /// Schreibt die Unterschiede zwischen <paramref name="current"/> und dem
    /// zuletzt geladenen Stand.
    ///
    /// <b>Immer einzeln, nie der ganze Satz.</b> Ein Read-Modify-Write über alle
    /// Filter würde bei zwei gleichzeitigen Bearbeitern lautlos Einträge
    /// verlieren — genau der Fehler, an dem die geteilte <c>hosts.json</c>
    /// gestorben ist. Gelöscht wird ausschließlich, was in <i>meinem</i>
    /// Ausgangsstand stand: Ein Filter, den ein Kollege inzwischen angelegt hat,
    /// ist mir unbekannt und bleibt deshalb unangetastet.
    /// </summary>
    public async Task<string?> PersistAsync(IReadOnlyList<HostFilter> current,
        CancellationToken ct = default)
    {
        if (!CanWrite)
            return "Die Datenbank ist nicht erreichbar — Filter sind gerade nur lesbar.";

        try
        {
            var keep = current.Where(f => !f.IsTransient).ToList();

            foreach (var gone in _loaded.Where(l =>
                l.Id > 0 && !keep.Any(k => k.Id == l.Id)))
                await filters.DeleteAsync(gone.Id, ct).ConfigureAwait(false);

            foreach (var f in keep)
            {
                var before = _loaded.FirstOrDefault(l => l.Id == f.Id && f.Id > 0);
                if (before is not null && !Differs(before, f)) continue;

                var id = await filters.SaveAsync(ToShared(f, _site), userName, ct)
                    .ConfigureAwait(false);
                f.Id = id;
            }

            _loaded = [.. keep.Select(Clone)];
            WriteCache(_site, _loaded);
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Filter konnten nicht zentral gespeichert werden.");
            return $"Speichern fehlgeschlagen: {ex.Message}";
        }
    }

    private IReadOnlyList<HostFilter> Snapshot() => [.. _loaded.Select(Clone)];

    /// <summary>
    /// Der Team-Name ist Anzeige, kein Datenbestand — er kommt bei jedem Klonen
    /// frisch aus dem Team-Store. Sonst zeigt ein umbenanntes Team im
    /// Filter-Manager weiter den alten Namen.
    /// </summary>
    private HostFilter Clone(HostFilter f) => new()
    {
        Id = f.Id,
        TeamId = f.TeamId,
        TeamName = teams.Current.NameOf(f.TeamId),
        Name = f.Name,
        HostNameRegex = f.HostNameRegex,
        ExplicitHosts = [.. f.ExplicitHosts]
    };

    private HostFilter ToModel(SharedFilter s) => new()
    {
        Id = s.HostFilterId,
        TeamId = s.TeamId,
        TeamName = teams.Current.NameOf(s.TeamId),
        Name = s.Name,
        HostNameRegex = s.HostNameRegex,
        ExplicitHosts = [.. s.Hosts]
    };

    private SharedFilter ToShared(HostFilter f, string site) => new(
        f.Id, f.TeamId, f.TeamId is null ? userName : null, site,
        string.IsNullOrWhiteSpace(f.Name) ? "unbenannt" : f.Name,
        f.HostNameRegex, f.ExplicitHosts);

    private static bool Differs(HostFilter a, HostFilter b)
        => a.TeamId != b.TeamId
        || !string.Equals(a.Name, b.Name, StringComparison.Ordinal)
        || !string.Equals(a.HostNameRegex, b.HostNameRegex, StringComparison.Ordinal)
        || !a.ExplicitHosts.OrderBy(h => h, StringComparer.OrdinalIgnoreCase)
              .SequenceEqual(b.ExplicitHosts.OrderBy(h => h, StringComparer.OrdinalIgnoreCase),
                             StringComparer.OrdinalIgnoreCase);

    // --- Ausfall-Cache ---------------------------------------------------

    private sealed class CacheDoc
    {
        public Dictionary<string, List<HostFilter>> Sites { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private void WriteCache(string site, IReadOnlyList<HostFilter> list)
    {
        try
        {
            var doc = ReadCacheDoc();
            doc.Sites[site] = [.. list];
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllText(cachePath, JsonSerializer.Serialize(doc, JsonOpts));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Filter-Cache konnte nicht geschrieben werden: {Path}", cachePath);
        }
    }

    private List<HostFilter> ReadCache(string site)
        => ReadCacheDoc().Sites.TryGetValue(site, out var l) ? l : [];

    private CacheDoc ReadCacheDoc()
    {
        try
        {
            if (!File.Exists(cachePath)) return new CacheDoc();
            var doc = JsonSerializer.Deserialize<CacheDoc>(File.ReadAllText(cachePath))
                      ?? new CacheDoc();
            doc.Sites = new Dictionary<string, List<HostFilter>>(doc.Sites,
                StringComparer.OrdinalIgnoreCase);
            return doc;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Filter-Cache nicht lesbar: {Path}", cachePath);
            return new CacheDoc();
        }
    }
}
