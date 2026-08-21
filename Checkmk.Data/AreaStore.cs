using Microsoft.EntityFrameworkCore;
using NLog;

namespace Checkmk.Data;

/// <summary>Ein Bereich, wie ihn die Oberflaeche braucht — ohne EF-Ballast.</summary>
public sealed record AreaRow(
    int AreaId,
    int? ParentAreaId,
    string Name,
    int SortOrder,
    string? GeometryJson,
    string? MapLayerKey);

/// <summary>
/// Momentaufnahme des Bereichsbaums samt Host-Zuordnung. Als Wert kopiert,
/// damit der Rollup auf einem Hintergrund-Thread laufen kann.
/// </summary>
public sealed record AreaSnapshot(
    IReadOnlyList<AreaRow> Areas,
    IReadOnlyDictionary<string, int> HostToArea)
{
    public static readonly AreaSnapshot Empty =
        new([], new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>Warum ein Loeschen abgelehnt wurde — der Aufrufer soll es dem
/// Anwender sagen koennen, nicht nur „hat nicht geklappt".</summary>
public sealed record AreaDeleteResult(bool Deleted, int ChildCount, int HostCount)
{
    public static AreaDeleteResult Ok => new(true, 0, 0);
}

public interface IAreaStore
{
    /// <summary>Momentaufnahme — kein I/O.</summary>
    AreaSnapshot Current { get; }

    Task RefreshAsync(CancellationToken ct = default);

    Task<int> CreateAsync(string name, int? parentAreaId, CancellationToken ct = default);

    Task RenameAsync(int areaId, string name, CancellationToken ct = default);

    Task<AreaDeleteResult> DeleteAsync(int areaId, CancellationToken ct = default);

    /// <summary>Speichert die Fläche eines Bereichs (GeoJSON-Polygon, WGS84).
    /// <c>null</c> löscht sie wieder.</summary>
    Task SaveGeometryAsync(int areaId, string? geoJson, CancellationToken ct = default);

    /// <summary>Ordnet Hosts einem Bereich zu. <paramref name="areaId"/> = null
    /// entfernt die Zuordnung.</summary>
    Task AssignAsync(IReadOnlyList<string> hostNames, int? areaId, CancellationToken ct = default);
}

/// <summary>
/// Bereiche und Host-Zuordnung aus der zentralen Datenbank.
///
/// Wie <c>DbHostDomainStore</c> haelt der Store eine Momentaufnahme im Speicher:
/// Der Rollup fragt fuer jeden der ~1100 Hosts nach seinem Bereich, das darf
/// kein Datenbank-Roundtrip sein. Aktualisiert wird beim Start und nach jeder
/// Aenderung.
/// </summary>
public sealed class AreaStore(CockpitDatabase database) : IAreaStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private volatile AreaSnapshot _current = AreaSnapshot.Empty;

    public AreaSnapshot Current => _current;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = database.CreateContext();

            var areas = await db.Areas.AsNoTracking()
                .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
                .Select(a => new AreaRow(a.AreaId, a.ParentAreaId, a.Name, a.SortOrder,
                                         a.GeometryJson, a.MapLayerKey))
                .ToListAsync(ct).ConfigureAwait(false);

            var assignments = await db.HostAreas.AsNoTracking()
                .Select(h => new { h.HostName, h.AreaId })
                .ToListAsync(ct).ConfigureAwait(false);

            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in assignments) map[a.HostName] = a.AreaId;

            _current = new AreaSnapshot(areas, map);
            Log.Info("Bereiche gelesen: {Areas} Bereiche, {Hosts} zugeordnete Hosts.",
                areas.Count, map.Count);
        }
        catch (Exception ex)
        {
            // Alte Momentaufnahme stehen lassen: eine leere Zuordnung saehe aus,
            // als haette jemand alle Zuweisungen geloescht.
            Log.Warn(ex, "Bereiche konnten nicht gelesen werden — behalte den vorherigen Stand.");
        }
    }

    public async Task<int> CreateAsync(string name, int? parentAreaId, CancellationToken ct = default)
    {
        await using var db = database.CreateContext();

        var area = new Area
        {
            ParentAreaId = parentAreaId,
            Name = name.Trim(),
            ChangedAtUtc = DateTime.UtcNow,
            ChangedBy = Environment.UserName
        };
        db.Areas.Add(area);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await RefreshAsync(ct).ConfigureAwait(false);
        return area.AreaId;
    }

    public async Task RenameAsync(int areaId, string name, CancellationToken ct = default)
    {
        await using var db = database.CreateContext();

        var area = await db.Areas.FirstOrDefaultAsync(a => a.AreaId == areaId, ct)
            .ConfigureAwait(false);
        if (area is null) return;

        area.Name = name.Trim();
        area.ChangedAtUtc = DateTime.UtcNow;
        area.ChangedBy = Environment.UserName;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await RefreshAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Loescht einen Bereich — aber nur, wenn er leer ist. Unterbereiche
    /// mitzuloeschen oder Hosts stillschweigend freizusetzen waere ein
    /// Datenverlust, den niemand kommen sieht; der Aufrufer bekommt stattdessen
    /// die Zahlen und kann sie dem Anwender zeigen.
    /// </summary>
    public async Task<AreaDeleteResult> DeleteAsync(int areaId, CancellationToken ct = default)
    {
        await using var db = database.CreateContext();

        var children = await db.Areas.CountAsync(a => a.ParentAreaId == areaId, ct)
            .ConfigureAwait(false);
        var hosts = await db.HostAreas.CountAsync(h => h.AreaId == areaId, ct)
            .ConfigureAwait(false);
        if (children > 0 || hosts > 0)
            return new AreaDeleteResult(false, children, hosts);

        var area = await db.Areas.FirstOrDefaultAsync(a => a.AreaId == areaId, ct)
            .ConfigureAwait(false);
        if (area is null) return AreaDeleteResult.Ok;

        db.Areas.Remove(area);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await RefreshAsync(ct).ConfigureAwait(false);
        return AreaDeleteResult.Ok;
    }

    public async Task SaveGeometryAsync(int areaId, string? geoJson, CancellationToken ct = default)
    {
        await using var db = database.CreateContext();

        var area = await db.Areas.FirstOrDefaultAsync(a => a.AreaId == areaId, ct)
            .ConfigureAwait(false);
        if (area is null) return;

        area.GeometryJson = geoJson;
        area.ChangedAtUtc = DateTime.UtcNow;
        area.ChangedBy = Environment.UserName;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        Log.Info("Flaeche fuer Bereich {AreaId} {Action}.",
            areaId, geoJson is null ? "geloescht" : "gespeichert");

        await RefreshAsync(ct).ConfigureAwait(false);
    }

    public async Task AssignAsync(IReadOnlyList<string> hostNames, int? areaId,
        CancellationToken ct = default)
    {
        if (hostNames.Count == 0) return;

        await using var db = database.CreateContext();

        var names = hostNames
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existing = await db.HostAreas
            .Where(h => names.Contains(h.HostName))
            .ToListAsync(ct).ConfigureAwait(false);
        var byHost = existing.ToDictionary(h => h.HostName, StringComparer.OrdinalIgnoreCase);

        if (areaId is null)
        {
            db.HostAreas.RemoveRange(existing);
        }
        else
        {
            var now = DateTime.UtcNow;
            var who = Environment.UserName;

            foreach (var host in names)
            {
                if (byHost.TryGetValue(host, out var row))
                {
                    if (row.AreaId == areaId.Value) continue;   // kein Audit-Rauschen
                    row.AreaId = areaId.Value;
                    row.AssignedAtUtc = now;
                    row.AssignedBy = who;
                }
                else
                {
                    db.HostAreas.Add(new HostArea
                    {
                        HostName = host, AreaId = areaId.Value,
                        AssignedAtUtc = now, AssignedBy = who
                    });
                }
            }
        }

        var changed = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        Log.Info("Bereichszuordnung: {Count} Hosts -> {Area} ({Changed} Aenderungen).",
            names.Count, areaId?.ToString() ?? "(entfernt)", changed);

        await RefreshAsync(ct).ConfigureAwait(false);
    }
}
