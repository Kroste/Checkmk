using Microsoft.EntityFrameworkCore;
using NLog;

namespace Checkmk.Data;

/// <summary>Ein Team samt Mitgliedern, wie es die Oberfläche zeigt.</summary>
public sealed record TeamRow(int TeamId, string Name, string? Description,
    IReadOnlyList<string> Members)
{
    public bool Contains(string user)
        => Members.Any(m => m.Equals(user, StringComparison.OrdinalIgnoreCase));

    public string Display => Members.Count == 0
        ? $"{Name} (keine Mitglieder)"
        : $"{Name} ({Members.Count})";
}

/// <summary>Momentaufnahme der Teams. Leere Admin-Liste = jeder ist Admin.</summary>
public sealed record TeamSnapshot(
    IReadOnlyList<TeamRow> Teams,
    IReadOnlyList<string> Admins)
{
    public static readonly TeamSnapshot Empty = new([], []);

    /// <summary>
    /// Darf dieser Anwender Teams und geteilte Filter verwalten?
    ///
    /// <b>Ist die Admin-Tabelle leer, darf es jeder.</b> Eine leere Tabelle
    /// heißt „noch nicht eingerichtet", und die Alternative wäre eine Funktion,
    /// die ohne einen SQL-Eingriff niemand benutzen kann. Sobald der erste
    /// Eintrag steht, greift die Liste.
    /// </summary>
    public bool IsAdmin(string user)
        => Admins.Count == 0
        || Admins.Any(a => a.Equals(user, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Teams, in denen der Anwender Mitglied ist. <b>Leer heißt „sieht alles"</b> —
    /// wer in keinem Team ist, bekommt alle Team-Filter zu sehen, statt keinen.
    /// </summary>
    public IReadOnlyList<TeamRow> TeamsOf(string user)
        => [.. Teams.Where(t => t.Contains(user))];

    public TeamRow? ById(int teamId) => Teams.FirstOrDefault(t => t.TeamId == teamId);

    public string? NameOf(int? teamId)
        => teamId is { } id ? ById(id)?.Name : null;
}

public interface ITeamStore
{
    TeamSnapshot Current { get; }
    Task RefreshAsync(CancellationToken ct = default);

    Task<int> CreateAsync(string name, string? description, CancellationToken ct = default);
    Task RenameAsync(int teamId, string name, string? description, CancellationToken ct = default);
    Task DeleteAsync(int teamId, CancellationToken ct = default);
    Task SetMembersAsync(int teamId, IReadOnlyList<string> users, CancellationToken ct = default);
}

/// <summary>
/// Teams und Admin-Zuordnung aus der zentralen Datenbank.
///
/// Wie <c>AreaStore</c> und <c>DbHostDomainStore</c> hält der Store eine
/// Momentaufnahme im Speicher: Die Frage „ist dieser Filter meiner" wird bei
/// jedem Aufbau der Filterliste gestellt, das darf kein Datenbank-Roundtrip
/// sein. Schlägt das Lesen fehl, bleibt der alte Stand stehen.
/// </summary>
public sealed class TeamStore(CockpitDatabase database) : ITeamStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private volatile TeamSnapshot _current = TeamSnapshot.Empty;

    public TeamSnapshot Current => _current;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = database.CreateContext();

            var teams = await db.Teams.AsNoTracking()
                .OrderBy(t => t.Name)
                .Select(t => new { t.TeamId, t.Name, t.Description })
                .ToListAsync(ct).ConfigureAwait(false);

            var members = await db.TeamMembers.AsNoTracking()
                .Select(m => new { m.TeamId, m.UserName })
                .ToListAsync(ct).ConfigureAwait(false);

            var byTeam = members.GroupBy(m => m.TeamId)
                .ToDictionary(g => g.Key,
                              g => (IReadOnlyList<string>)[.. g.Select(x => x.UserName)
                                  .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)]);

            // Eigener Versuch: Fehlt die Tabelle, sollen die Teams trotzdem
            // erscheinen — dann ist eben niemand als Admin eingetragen, was
            // nach der Regel oben heisst: jeder darf.
            var admins = new List<string>();
            try
            {
                admins = await db.AppAdmins.AsNoTracking()
                    .Select(a => a.UserName).ToListAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Admin-Liste nicht lesbar — jeder gilt als Admin.");
            }

            _current = new TeamSnapshot(
                [.. teams.Select(t => new TeamRow(t.TeamId, t.Name, t.Description,
                    byTeam.GetValueOrDefault(t.TeamId, [])))],
                admins);

            Log.Info("Teams gelesen: {Teams} Teams, {Members} Mitgliedschaften, {Admins} Admins.",
                teams.Count, members.Count, admins.Count);
        }
        catch (Exception ex)
        {
            // Alte Momentaufnahme stehen lassen: eine leere Teamliste saehe aus,
            // als haette jemand alle Teams geloescht — und wuerde nebenbei jeden
            // zum Admin machen.
            Log.Warn(ex, "Teams konnten nicht gelesen werden — behalte den vorherigen Stand.");
        }
    }

    public async Task<int> CreateAsync(string name, string? description,
        CancellationToken ct = default)
    {
        await using var db = database.CreateContext();

        var team = new Team
        {
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Teams.Add(team);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await RefreshAsync(ct).ConfigureAwait(false);
        return team.TeamId;
    }

    public async Task RenameAsync(int teamId, string name, string? description,
        CancellationToken ct = default)
    {
        await using var db = database.CreateContext();

        var team = await db.Teams.FirstOrDefaultAsync(t => t.TeamId == teamId, ct)
            .ConfigureAwait(false);
        if (team is null) return;

        team.Name = name.Trim();
        team.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await RefreshAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Löscht ein Team samt Mitgliedschaften und seinen geteilten Filtern.
    ///
    /// Die Filter <b>mitzulöschen</b> ist hier richtig, anders als bei
    /// <c>AreaStore.DeleteAsync</c>: Ein Filter ohne Team wäre eine Zeile, die
    /// niemandem gehört und die der CHECK in der Datenbank ohnehin verbietet.
    /// Deshalb fragt die Oberfläche vorher nach und nennt die Zahl.
    /// </summary>
    public async Task DeleteAsync(int teamId, CancellationToken ct = default)
    {
        await using var db = database.CreateContext();

        // Die Filter muessen ausdruecklich weg: Auf FK_HostFilter_Team liegt
        // bewusst kein Cascade. Ihre Include-Listen und die Mitgliedschaften
        // raeumt die Datenbank dagegen selbst — sie hier zusaetzlich zu
        // entfernen brachte EF-DELETEs, die nach dem Cascade nichts mehr
        // trafen, und damit einen vorgeblichen Nebenlaeufigkeitskonflikt.
        var filters = await db.HostFilters.Where(f => f.TeamId == teamId)
            .ToListAsync(ct).ConfigureAwait(false);
        db.HostFilters.RemoveRange(filters);

        if (await db.Teams.FirstOrDefaultAsync(t => t.TeamId == teamId, ct)
                .ConfigureAwait(false) is { } team)
            db.Teams.Remove(team);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        Log.Info("Team {Team} geloescht ({Filters} geteilte Filter mit).", teamId, filters.Count);

        await RefreshAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Anzahl geteilter Filter eines Teams — für die Rückfrage vor dem Löschen.</summary>
    public async Task<int> CountFiltersAsync(int teamId, CancellationToken ct = default)
    {
        await using var db = database.CreateContext();
        return await db.HostFilters.CountAsync(f => f.TeamId == teamId, ct).ConfigureAwait(false);
    }

    public async Task SetMembersAsync(int teamId, IReadOnlyList<string> users,
        CancellationToken ct = default)
    {
        await using var db = database.CreateContext();

        var existing = await db.TeamMembers.Where(m => m.TeamId == teamId)
            .ToListAsync(ct).ConfigureAwait(false);

        var wanted = users.Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Diffen statt loeschen-und-neu-schreiben: Das war der Fehler der alten
        // hosts.json, und hier haengen ausserdem Fremdschluessel dran.
        foreach (var gone in existing.Where(e =>
            !wanted.Any(w => w.Equals(e.UserName, StringComparison.OrdinalIgnoreCase))))
            db.TeamMembers.Remove(gone);

        foreach (var added in wanted.Where(w =>
            !existing.Any(e => e.UserName.Equals(w, StringComparison.OrdinalIgnoreCase))))
            db.TeamMembers.Add(new TeamMember { TeamId = teamId, UserName = added });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await RefreshAsync(ct).ConfigureAwait(false);
    }
}
