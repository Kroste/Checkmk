using Microsoft.EntityFrameworkCore;
using NLog;

namespace Checkmk.Data;

public interface IGlobalSettingsProvider
{
    /// <summary>Zuletzt geladener Stand. Nie null — notfalls Vorgaben.</summary>
    CockpitGlobals Current { get; }

    SettingsOrigin Origin { get; }

    /// <summary>Klartext-Grund, wenn nicht aus der Datenbank gelesen werden
    /// konnte. Gehoert in die Statusleiste, nicht nur ins Log.</summary>
    string? Problem { get; }

    Task LoadAsync(CancellationToken ct = default);

    Task SaveAsync(CockpitGlobals globals, string changedBy, CancellationToken ct = default);
}

/// <summary>
/// Liest die geteilten Vorgaben aus der Datenbank und legt nach jedem Erfolg
/// eine Kopie neben die uebrigen Benutzerdateien.
///
/// Der Cache ist kein Beiwerk: Der Grund, vom Fileshare wegzugehen, war dessen
/// Verfuegbarkeit — dann darf die Datenbank nicht der naechste Engpass werden.
/// Ist FOC-SQL01 nicht erreichbar, startet das Cockpit mit dem letzten bekannten
/// Stand weiter und sagt es in der Statusleiste. Erst wenn auch der Cache fehlt
/// (frische Installation, Datenbank aus), greifen die eingebauten Vorgaben.
/// </summary>
public sealed class GlobalSettingsProvider(CockpitDatabase database, GlobalSettingsCache cache)
    : IGlobalSettingsProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public GlobalSettingsProvider(CockpitDatabase database, string cachePath)
        : this(database, new GlobalSettingsCache(cachePath)) { }

    public CockpitGlobals Current { get; private set; } = new();

    public SettingsOrigin Origin { get; private set; } = SettingsOrigin.Defaults;

    public string? Problem { get; private set; }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = database.CreateContext();
            var rows = await db.GlobalSettings
                .AsNoTracking()
                .ToDictionaryAsync(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase, ct)
                .ConfigureAwait(false);

            Current = CockpitGlobals.FromRows(rows);
            Origin = SettingsOrigin.Database;
            Problem = null;
            cache.Write(rows);
            Log.Info("Globale Einstellungen aus der Datenbank gelesen ({Count} Eintraege).", rows.Count);
            return;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Globale Einstellungen konnten nicht aus der Datenbank gelesen werden.");
            Problem = ex.Message;
        }

        if (cache.Read() is { } cached)
        {
            Current = CockpitGlobals.FromRows(cached);
            Origin = SettingsOrigin.Cache;
            Log.Info("Globale Einstellungen aus dem lokalen Ausfall-Cache ({Path}).", cache.Path);
            return;
        }

        Current = new CockpitGlobals();
        Origin = SettingsOrigin.Defaults;
        Log.Warn("Weder Datenbank noch Cache verfuegbar — eingebaute Vorgaben aktiv.");
    }

    public async Task SaveAsync(CockpitGlobals globals, string changedBy, CancellationToken ct = default)
    {
        await using var db = database.CreateContext();

        var existing = await db.GlobalSettings.ToDictionaryAsync(
            x => x.Key, StringComparer.OrdinalIgnoreCase, ct).ConfigureAwait(false);

        foreach (var (key, value) in globals.ToRows())
        {
            if (existing.TryGetValue(key, out var row))
            {
                if (row.Value == value) continue;   // nichts zu tun, kein Audit-Rauschen
                row.Value = value;
                row.ChangedAtUtc = DateTime.UtcNow;
                row.ChangedBy = changedBy;
            }
            else
            {
                db.GlobalSettings.Add(new GlobalSetting
                {
                    Key = key,
                    Value = value,
                    ChangedAtUtc = DateTime.UtcNow,
                    ChangedBy = changedBy
                });
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        Current = globals;
        Origin = SettingsOrigin.Database;
        Problem = null;
        cache.Write(globals.ToRows());
    }
}
