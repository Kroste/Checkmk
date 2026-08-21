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
    string? MapLayerKey,
    double? Lat = null,
    double? Lon = null,
    string? Address = null,
    string? ExternalSource = null,
    string? ExternalId = null,
    string? HostPattern = null,
    string? ExternalCode = null)
{
    /// <summary>Hat der Bereich ueberhaupt eine Lage auf der Karte?</summary>
    public bool HasPlace => !string.IsNullOrWhiteSpace(GeometryJson) || (Lat is not null && Lon is not null);
}

/// <summary>Ein zu importierender Standort aus einer externen Quelle.</summary>
/// <param name="Code">Kennzahl aus der Quelle (z. B. SCHULNUM). Steckt bei
/// Schulen im Hostnamen und liefert damit das Zuordnungsmuster.</param>
public sealed record ExternalPlace(
    string ExternalId,
    string Name,
    double Lat,
    double Lon,
    string? Address,
    string? Code = null);

/// <summary>Ergebnis eines Imports — fuer die Rueckmeldung an den Anwender.</summary>
public sealed record ImportResult(int Created, int Updated, int Unchanged);

/// <summary>
/// Momentaufnahme des Bereichsbaums samt Host-Zuordnung. Als Wert kopiert,
/// damit der Rollup auf einem Hintergrund-Thread laufen kann.
/// </summary>
public sealed record AreaSnapshot(
    IReadOnlyList<AreaRow> Areas,
    IReadOnlyDictionary<string, int> HostToArea,
    IReadOnlyDictionary<int, IReadOnlyList<string>> SitesByArea)
{
    public static readonly AreaSnapshot Empty = new(
        [],
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<int, IReadOnlyList<string>>());

    /// <summary>
    /// Ist der Bereich in dieser Site sichtbar? <b>Ohne Eintrag: ja.</b>
    /// Keine Zuordnung heisst „gilt ueberall" — so bleiben Bereiche aus der
    /// Zeit vor Schema 4 unveraendert sichtbar, und das Zusammenfuehren der
    /// Sites ist spaeter ein Loeschen der Zuordnungen.
    /// </summary>
    public bool IsVisibleIn(int areaId, string? site)
    {
        if (string.IsNullOrWhiteSpace(site)) return true;
        if (!SitesByArea.TryGetValue(areaId, out var sites) || sites.Count == 0) return true;
        return sites.Contains(site, StringComparer.OrdinalIgnoreCase);
    }
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

    /// <param name="sites">Sites, in denen der neue Bereich sichtbar ist.
    /// Leer/<c>null</c> = überall.</param>
    Task<int> CreateAsync(string name, int? parentAreaId,
        IReadOnlyList<string>? sites = null, CancellationToken ct = default);

    /// <summary>Sites, in denen ein Bereich sichtbar ist. Leer = überall.</summary>
    IReadOnlyList<string> SitesOf(int areaId);

    /// <summary>Speichert die Punktlage. <c>null</c> entfernt sie.</summary>
    Task SavePointAsync(int areaId, double? lat, double? lon, CancellationToken ct = default);

    /// <summary>Speichert das Host-Namensmuster. <c>null</c> entfernt es.</summary>
    Task SaveHostPatternAsync(int areaId, string? pattern, CancellationToken ct = default);

    /// <summary>
    /// Legt Bereiche aus einer externen Standortliste an bzw. gleicht sie ab.
    /// Der Abgleich läuft über <c>ExternalSource</c>+<c>ExternalId</c>, ein
    /// zweiter Lauf erzeugt also keine Dubletten.
    /// </summary>
    /// <param name="sites">Checkmk-Sites, in denen die neuen Bereiche sichtbar
    /// sein sollen. Leer = in allen.</param>
    /// <param name="patternFor">Erzeugt aus dem <c>Code</c> eines Standorts das
    /// Host-Namensmuster. <c>null</c> = kein Muster. Wird als Funktion
    /// hereingereicht, weil die Regex-Logik in der Anwendungsschicht sitzt und
    /// <c>Checkmk.Data</c> davon nichts wissen muss.</param>
    Task<ImportResult> ImportPlacesAsync(string source, IReadOnlyList<ExternalPlace> places,
        int? parentAreaId, IReadOnlyList<string> sites,
        Func<string?, string?>? patternFor = null, CancellationToken ct = default);

    /// <summary>
    /// Verschiebt <b>alle</b> Hosts eines Bereichs in einen anderen.
    /// <paramref name="toAreaId"/> = null löst die Zuordnung.
    /// </summary>
    Task<int> MoveHostsAsync(int fromAreaId, int? toAreaId, CancellationToken ct = default);

    /// <summary>Hostnamen, die diesem Bereich zugeordnet sind — ohne I/O.</summary>
    IReadOnlyList<string> HostsIn(int areaId);

    /// <summary>Setzt die Sites, in denen ein Bereich sichtbar ist. Leer = überall.</summary>
    Task SaveSitesAsync(int areaId, IReadOnlyList<string> sites, CancellationToken ct = default);

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

            List<AreaRow> areas;
            try
            {
                areas = await db.Areas.AsNoTracking()
                    .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
                    .Select(a => new AreaRow(a.AreaId, a.ParentAreaId, a.Name, a.SortOrder,
                                             a.GeometryJson, a.MapLayerKey,
                                             a.Lat, a.Lon, a.Address, a.ExternalSource, a.ExternalId,
                                             a.HostPattern, a.ExternalCode))
                    .ToListAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Skript 005 noch nicht gefahren: ohne diesen Rueckfall waere
                // der Bereichsbaum bis dahin komplett leer — das sieht aus wie
                // Datenverlust, obwohl nur eine Spalte fehlt.
                Log.Debug(ex, "Spalten aus Schema 5 nicht lesbar — lade Bereiche ohne Muster.");
                areas = await db.Areas.AsNoTracking()
                    .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
                    .Select(a => new AreaRow(a.AreaId, a.ParentAreaId, a.Name, a.SortOrder,
                                             a.GeometryJson, a.MapLayerKey,
                                             a.Lat, a.Lon, a.Address, a.ExternalSource, a.ExternalId))
                    .ToListAsync(ct).ConfigureAwait(false);
            }

            var assignments = await db.HostAreas.AsNoTracking()
                .Select(h => new { h.HostName, h.AreaId })
                .ToListAsync(ct).ConfigureAwait(false);

            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in assignments) map[a.HostName] = a.AreaId;

            // Eigener Versuch: Fehlt die Tabelle (Skript 004 noch nicht
            // gefahren), sollen die Bereiche trotzdem erscheinen. Ohne
            // Zuordnungen sind sie in allen Sites sichtbar — genau das
            // Verhalten von vorher.
            var siteRows = new List<(int AreaId, string Site)>();
            try
            {
                siteRows = await db.AreaSites.AsNoTracking()
                    .Select(s => new ValueTuple<int, string>(s.AreaId, s.Site))
                    .ToListAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Site-Zuordnungen nicht lesbar — Bereiche gelten ueberall.");
            }

            var sites = siteRows
                .GroupBy(s => s.AreaId)
                .ToDictionary(g => g.Key,
                              g => (IReadOnlyList<string>)[.. g.Select(x => x.Site).Distinct()]);

            _current = new AreaSnapshot(areas, map, sites);
            Log.Info("Bereiche gelesen: {Areas} Bereiche, {Hosts} zugeordnete Hosts, "
                   + "{Sites} Site-Zuordnungen.", areas.Count, map.Count, siteRows.Count);
        }
        catch (Exception ex)
        {
            // Alte Momentaufnahme stehen lassen: eine leere Zuordnung saehe aus,
            // als haette jemand alle Zuweisungen geloescht.
            Log.Warn(ex, "Bereiche konnten nicht gelesen werden — behalte den vorherigen Stand.");
        }
    }

    public IReadOnlyList<string> SitesOf(int areaId)
        => _current.SitesByArea.TryGetValue(areaId, out var s) ? s : [];

    public async Task<int> CreateAsync(string name, int? parentAreaId,
        IReadOnlyList<string>? sites = null, CancellationToken ct = default)
    {
        await using var db = database.CreateContext();

        var now = DateTime.UtcNow;
        var area = new Area
        {
            ParentAreaId = parentAreaId,
            Name = name.Trim(),
            ChangedAtUtc = now,
            ChangedBy = Environment.UserName
        };
        db.Areas.Add(area);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Site-Zuordnung erst nach dem Speichern — vorher gibt es keine AreaId.
        var wanted = (sites ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (wanted.Count > 0)
        {
            foreach (var site in wanted)
                db.AreaSites.Add(new AreaSite { AreaId = area.AreaId, Site = site, AddedAtUtc = now });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

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

    public async Task SavePointAsync(int areaId, double? lat, double? lon,
        CancellationToken ct = default)
    {
        await using var db = database.CreateContext();

        var area = await db.Areas.FirstOrDefaultAsync(a => a.AreaId == areaId, ct)
            .ConfigureAwait(false);
        if (area is null) return;

        area.Lat = lat;
        area.Lon = lon;
        area.ChangedAtUtc = DateTime.UtcNow;
        area.ChangedBy = Environment.UserName;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await RefreshAsync(ct).ConfigureAwait(false);
    }

    public IReadOnlyList<string> HostsIn(int areaId)
        => [.. _current.HostToArea.Where(kv => kv.Value == areaId)
                                  .Select(kv => kv.Key)
                                  .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Verschiebt alle Hosts eines Bereichs. Das ist der Alltagsfall, den das
    /// Zuweisen einzelner Hosts nicht abdeckt: Ein Haus wird aufgeloest, die
    /// Technik wandert in den Container — und spaeter vielleicht zurueck.
    /// </summary>
    public async Task<int> MoveHostsAsync(int fromAreaId, int? toAreaId,
        CancellationToken ct = default)
    {
        if (fromAreaId == toAreaId) return 0;

        await using var db = database.CreateContext();

        var rows = await db.HostAreas.Where(h => h.AreaId == fromAreaId)
            .ToListAsync(ct).ConfigureAwait(false);
        if (rows.Count == 0) return 0;

        if (toAreaId is null)
        {
            db.HostAreas.RemoveRange(rows);
        }
        else
        {
            var now = DateTime.UtcNow;
            var who = Environment.UserName;
            foreach (var r in rows)
            {
                r.AreaId = toAreaId.Value;
                r.AssignedAtUtc = now;
                r.AssignedBy = who;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        Log.Info("{Count} Hosts verschoben: Bereich {From} -> {To}.",
            rows.Count, fromAreaId, toAreaId?.ToString() ?? "(ohne Bereich)");

        await RefreshAsync(ct).ConfigureAwait(false);
        return rows.Count;
    }

    public async Task SaveSitesAsync(int areaId, IReadOnlyList<string> sites,
        CancellationToken ct = default)
    {
        await using var db = database.CreateContext();

        var existing = await db.AreaSites.Where(s => s.AreaId == areaId)
            .ToListAsync(ct).ConfigureAwait(false);
        db.AreaSites.RemoveRange(existing);

        foreach (var site in sites.Where(s => !string.IsNullOrWhiteSpace(s))
                                  .Select(s => s.Trim())
                                  .Distinct(StringComparer.OrdinalIgnoreCase))
            db.AreaSites.Add(new AreaSite { AreaId = areaId, Site = site, AddedAtUtc = DateTime.UtcNow });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await RefreshAsync(ct).ConfigureAwait(false);
    }

    public async Task SaveHostPatternAsync(int areaId, string? pattern, CancellationToken ct = default)
    {
        await using var db = database.CreateContext();

        var area = await db.Areas.FirstOrDefaultAsync(a => a.AreaId == areaId, ct)
            .ConfigureAwait(false);
        if (area is null) return;

        area.HostPattern = string.IsNullOrWhiteSpace(pattern) ? null : pattern.Trim();
        area.ChangedAtUtc = DateTime.UtcNow;
        area.ChangedBy = Environment.UserName;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await RefreshAsync(ct).ConfigureAwait(false);
    }

    public async Task<ImportResult> ImportPlacesAsync(string source,
        IReadOnlyList<ExternalPlace> places, int? parentAreaId,
        IReadOnlyList<string> sites, Func<string?, string?>? patternFor = null,
        CancellationToken ct = default)
    {
        if (places.Count == 0) return new ImportResult(0, 0, 0);

        await using var db = database.CreateContext();

        var ids = places.Select(p => p.ExternalId).ToList();
        var existing = await db.Areas
            .Where(a => a.ExternalSource == source && ids.Contains(a.ExternalId!))
            .ToListAsync(ct).ConfigureAwait(false);
        var byId = existing
            .Where(a => a.ExternalId is not null)
            .ToDictionary(a => a.ExternalId!, StringComparer.OrdinalIgnoreCase);

        // Bereichsnamen sind je Ebene eindeutig (Index aus 002-map-teams.sql).
        // Die amtlichen Listen halten sich nicht daran — „Musikschule" steht
        // zweimal drin, an der Galileistrasse und in der Jaegerstrasse. Ohne
        // Entschaerfung scheitert der ganze Import an SQL-Fehler 2601, und der
        // Anwender sieht nur „Import fehlgeschlagen".
        var takenNames = await db.Areas
            .Where(a => a.ParentAreaId == parentAreaId)
            .Select(a => a.Name)
            .ToListAsync(ct).ConfigureAwait(false);
        var taken = new HashSet<string>(takenNames, StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        var who = Environment.UserName;
        int created = 0, updated = 0, unchanged = 0;
        var added = new List<Area>();

        foreach (var p in places)
        {
            if (byId.TryGetValue(p.ExternalId, out var area))
            {
                // Nur nachziehen, was sich geaendert hat. Der Name bleibt
                // absichtlich anfassbar: Wer einen importierten Standort
                // umbenannt hat ("Stadthaus" statt der amtlichen Bezeichnung),
                // soll das beim naechsten Abgleich nicht verlieren.
                var moved = area.Lat != p.Lat || area.Lon != p.Lon;
                var addressChanged = area.Address != p.Address;

                // Muster nur setzen, wenn noch keines da ist ODER sich der Code
                // geaendert hat. Ein von Hand angepasstes Muster darf ein
                // erneuter Import nicht wegwerfen.
                var codeChanged = area.ExternalCode != p.Code;
                if (codeChanged || string.IsNullOrWhiteSpace(area.HostPattern))
                {
                    area.ExternalCode = p.Code;
                    if (patternFor?.Invoke(p.Code) is { } fresh) area.HostPattern = fresh;
                }

                // Der bestehende Name bleibt seiner Ebene erhalten, damit ein
                // neuer Eintrag nicht darauf ausweicht.
                taken.Add(area.Name);

                if (!moved && !addressChanged && !codeChanged) { unchanged++; continue; }

                area.Lat = p.Lat;
                area.Lon = p.Lon;
                area.Address = p.Address;
                area.ChangedAtUtc = now;
                area.ChangedBy = who;
                updated++;
            }
            else
            {
                var name = UniqueName(p, taken);
                taken.Add(name);

                var fresh = new Area
                {
                    ParentAreaId = parentAreaId,
                    Name = name,
                    Lat = p.Lat,
                    Lon = p.Lon,
                    Address = p.Address,
                    ExternalSource = source,
                    ExternalId = p.ExternalId,
                    ExternalCode = p.Code,
                    HostPattern = patternFor?.Invoke(p.Code),
                    ChangedAtUtc = now,
                    ChangedBy = who
                };
                db.Areas.Add(fresh);
                added.Add(fresh);
                created++;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Site-Zuordnung erst nach dem Speichern: vorher gibt es keine AreaId.
        // Leere Liste = ueberall sichtbar, dann bleibt die Tabelle leer.
        var wantedSites = sites.Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (wantedSites.Count > 0 && added.Count > 0)
        {
            foreach (var area in added)
                foreach (var site in wantedSites)
                    db.AreaSites.Add(new AreaSite { AreaId = area.AreaId, Site = site, AddedAtUtc = now });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        Log.Info("Standort-Import aus {Source}: {Created} neu, {Updated} aktualisiert, "
               + "{Unchanged} unveraendert.", source, created, updated, unchanged);

        await RefreshAsync(ct).ConfigureAwait(false);
        return new ImportResult(created, updated, unchanged);
    }

    /// <summary>
    /// Macht den Namen auf seiner Ebene eindeutig. Zuerst über die Anschrift —
    /// „Musikschule (Galileistraße 6)" sagt einem Menschen etwas, „Musikschule
    /// (2)" nicht. Erst wenn auch das kollidiert, wird durchgezählt.
    /// </summary>
    internal static string UniqueName(ExternalPlace place, ISet<string> taken)
    {
        if (!taken.Contains(place.Name)) return Trim(place.Name);

        if (!string.IsNullOrWhiteSpace(place.Address))
        {
            // Nur den Strassenteil, nicht die PLZ — die hilft beim Unterscheiden nicht.
            var street = place.Address.Split(',')[0].Trim();
            var withStreet = $"{place.Name} ({street})";
            if (!taken.Contains(withStreet)) return Trim(withStreet);
        }

        for (var i = 2; i < 1000; i++)
        {
            var numbered = $"{place.Name} ({i})";
            if (!taken.Contains(numbered)) return Trim(numbered);
        }
        return Trim($"{place.Name} ({place.ExternalId})");

        // Die Spalte fasst 200 Zeichen; amtliche Schulnamen werden lang
        // („Berufliche Schule fuer Sport und Gesundheit der …").
        static string Trim(string s) => s.Length <= 200 ? s : s[..200];
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
