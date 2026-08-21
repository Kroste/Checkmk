using Microsoft.EntityFrameworkCore;
using NLog;

namespace Checkmk.Data;

/// <summary>
/// Ein Filter, wie ihn die Anwendung sieht. <see cref="TeamId"/> gesetzt =
/// geteilt, sonst persönlich.
/// </summary>
public sealed record SharedFilter(
    int HostFilterId,
    int? TeamId,
    string? OwnerUserName,
    string Site,
    string Name,
    string? HostNameRegex,
    IReadOnlyList<string> Hosts)
{
    public bool IsShared => TeamId is not null;
}

public interface IFilterStore
{
    /// <summary>
    /// Filter, die dieser Anwender in dieser Site sehen darf: die eigenen
    /// persönlichen plus die Filter seiner Teams. <b>Wer in keinem Team ist,
    /// sieht alle Team-Filter</b> — dieselbe Regel wie überall sonst hier.
    /// </summary>
    Task<IReadOnlyList<SharedFilter>> LoadAsync(string site, string user,
        CancellationToken ct = default);

    /// <summary>Legt an oder aktualisiert. Gibt die Id zurück.</summary>
    Task<int> SaveAsync(SharedFilter filter, string changedBy, CancellationToken ct = default);

    Task DeleteAsync(int hostFilterId, CancellationToken ct = default);

    /// <summary>
    /// Übernimmt die persönlichen Filter aus <c>filter.json</c> — <b>genau
    /// einmal</b>, nämlich nur wenn dieser Anwender in dieser Site noch keinen
    /// einzigen persönlichen Filter in der Datenbank hat. Danach ist die
    /// Tabelle die Wahrheit; sonst überschriebe ein Rechner mit altem
    /// Dateistand später zentrale Änderungen.
    /// </summary>
    Task<int> ImportLegacyIfEmptyAsync(string site, string user,
        IReadOnlyList<SharedFilter> fromFile, CancellationToken ct = default);
}

/// <summary>
/// Host-Filter aus der zentralen Datenbank.
///
/// Der Grund, sie aus <c>filter.json</c> herauszuholen: Heute baut sich jeder
/// der 48 seinen eigenen Filter, und wenn der Netzwerkkollege im Urlaub ist,
/// fängt die Vertretung bei null an. Ein Team-Filter wird einmal gebaut und
/// gilt für alle im Team.
///
/// <b>Geschrieben wird immer einzeln, nie der ganze Satz.</b> Ein
/// Read-Modify-Write über alle Filter würde bei zwei gleichzeitigen Bearbeitern
/// lautlos Einträge verlieren — genau der Fehler, an dem die geteilte
/// <c>hosts.json</c> gestorben ist.
/// </summary>
public sealed class FilterStore(CockpitDatabase database) : IFilterStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly ITeamStore _teams = new TeamStore(database);

    /// <summary>Für den Fall, dass der Aufrufer schon einen Team-Store hat.</summary>
    public FilterStore(CockpitDatabase database, ITeamStore teams) : this(database)
        => _teams = teams;

    public async Task<IReadOnlyList<SharedFilter>> LoadAsync(string site, string user,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(site)) return [];

        await using var db = database.CreateContext();

        var rows = await db.HostFilters.AsNoTracking()
            .Where(f => f.Site == site)
            .OrderBy(f => f.Name)
            .ToListAsync(ct).ConfigureAwait(false);

        var myTeams = _teams.Current.TeamsOf(user).Select(t => t.TeamId).ToHashSet();

        var visible = rows.Where(f =>
                (f.OwnerUserName != null
                 && f.OwnerUserName.Equals(user, StringComparison.OrdinalIgnoreCase))
                // Wer in keinem Team ist, sieht alle Team-Filter statt keiner.
                || (f.TeamId is { } t && (myTeams.Count == 0 || myTeams.Contains(t))))
            .ToList();

        var ids = visible.Select(f => f.HostFilterId).ToList();
        var hosts = await db.HostFilterHosts.AsNoTracking()
            .Where(h => ids.Contains(h.HostFilterId))
            .ToListAsync(ct).ConfigureAwait(false);

        var byFilter = hosts.GroupBy(h => h.HostFilterId)
            .ToDictionary(g => g.Key,
                          g => (IReadOnlyList<string>)[.. g.Select(x => x.HostName)
                              .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)]);

        return [.. visible.Select(f => new SharedFilter(
            f.HostFilterId, f.TeamId, f.OwnerUserName, f.Site, f.Name, f.HostNameRegex,
            byFilter.GetValueOrDefault(f.HostFilterId, [])))];
    }

    public async Task<int> SaveAsync(SharedFilter filter, string changedBy,
        CancellationToken ct = default)
    {
        await using var db = database.CreateContext();

        HostFilterRow row;
        if (filter.HostFilterId > 0)
        {
            var found = await db.HostFilters
                .FirstOrDefaultAsync(f => f.HostFilterId == filter.HostFilterId, ct)
                .ConfigureAwait(false);

            // Von jemand anderem geloescht, waehrend der Dialog offen stand:
            // dann neu anlegen statt still nichts zu tun.
            if (found is null) { row = New(); db.HostFilters.Add(row); }
            else row = found;
        }
        else
        {
            row = New();
            db.HostFilters.Add(row);
        }

        row.TeamId = filter.TeamId;
        row.OwnerUserName = filter.TeamId is null ? filter.OwnerUserName : null;
        row.Site = filter.Site;
        row.Name = filter.Name.Trim();
        row.HostNameRegex = string.IsNullOrWhiteSpace(filter.HostNameRegex)
            ? null : filter.HostNameRegex.Trim();
        row.ChangedAtUtc = DateTime.UtcNow;
        row.ChangedBy = changedBy;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await ReplaceHostsAsync(db, row.HostFilterId, filter.Hosts, ct).ConfigureAwait(false);

        Log.Info("Filter gespeichert: '{Name}' ({Scope}, Site {Site}).",
            row.Name, row.TeamId is null ? "persoenlich" : $"Team {row.TeamId}", row.Site);
        return row.HostFilterId;

        HostFilterRow New() => new();
    }

    /// <summary>
    /// Die Include-Liste wird komplett ersetzt — anders als die Filter selbst.
    /// Sie gehört zu <i>einem</i> Filter und wird immer als Ganzes bearbeitet;
    /// hier gibt es keine zwei Bearbeiter, die sich Einträge wegnehmen könnten.
    /// </summary>
    private static async Task ReplaceHostsAsync(CockpitDbContext db, int filterId,
        IReadOnlyList<string> hosts, CancellationToken ct)
    {
        var existing = await db.HostFilterHosts.Where(h => h.HostFilterId == filterId)
            .ToListAsync(ct).ConfigureAwait(false);
        db.HostFilterHosts.RemoveRange(existing);

        foreach (var host in hosts.Where(h => !string.IsNullOrWhiteSpace(h))
                                  .Select(h => h.Trim())
                                  .Distinct(StringComparer.OrdinalIgnoreCase))
            db.HostFilterHosts.Add(new HostFilterHostRow
            {
                HostFilterId = filterId,
                HostName = host
            });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Löscht einen Filter. Die Include-Liste nimmt die Datenbank per
    /// <c>ON DELETE CASCADE</c> mit — sie hier zusätzlich zu entfernen wäre
    /// nicht nur überflüssig, sondern falsch: EF würde DELETEs schicken, die
    /// nach dem Cascade keine Zeile mehr treffen, und das als
    /// Nebenläufigkeitskonflikt melden.
    /// </summary>
    public async Task DeleteAsync(int hostFilterId, CancellationToken ct = default)
    {
        await using var db = database.CreateContext();

        if (await db.HostFilters.FirstOrDefaultAsync(f => f.HostFilterId == hostFilterId, ct)
                .ConfigureAwait(false) is not { } row) return;

        db.HostFilters.Remove(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> ImportLegacyIfEmptyAsync(string site, string user,
        IReadOnlyList<SharedFilter> fromFile, CancellationToken ct = default)
    {
        if (fromFile.Count == 0 || string.IsNullOrWhiteSpace(site)) return 0;

        await using var db = database.CreateContext();

        var any = await db.HostFilters
            .AnyAsync(f => f.Site == site && f.OwnerUserName == user, ct)
            .ConfigureAwait(false);
        if (any) return 0;

        foreach (var f in fromFile)
            await SaveAsync(f with { HostFilterId = 0, TeamId = null, OwnerUserName = user, Site = site },
                user, ct).ConfigureAwait(false);

        Log.Info("{Count} persoenliche Filter aus filter.json uebernommen (Site {Site}).",
            fromFile.Count, site);
        return fromFile.Count;
    }
}
