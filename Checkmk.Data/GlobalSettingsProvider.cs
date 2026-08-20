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

    /// <summary>Kurzfassung fuer die Statusleiste, oder <c>null</c> wenn alles
    /// normal ist (frisch aus der Datenbank).</summary>
    string? StatusHint { get; }

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
///
/// Der Konstruktor liest den Cache <b>synchron</b>: Verbraucher wie
/// <c>HostContext.DefaultDomain</c> fragen gleich beim Start und dürfen nicht
/// auf einen Netzwerk-Roundtrip warten. Die Datenbank kommt danach per
/// <see cref="LoadAsync"/> im Hintergrund dazu. Ohne Datenbank
/// (<paramref name="database"/> = null, etwa auf einem Rechner ohne
/// Verbindungsangabe) bleibt es beim Cache bzw. bei den Vorgaben.
/// </summary>
public sealed class GlobalSettingsProvider : IGlobalSettingsProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly CockpitDatabase? _database;
    private readonly GlobalSettingsCache _cache;

    public GlobalSettingsProvider(CockpitDatabase? database, GlobalSettingsCache cache)
    {
        _database = database;
        _cache = cache;

        if (cache.Read() is { } rows)
        {
            Current = CockpitGlobals.FromRows(rows);
            Origin = SettingsOrigin.Cache;
        }
        else
        {
            Current = new CockpitGlobals();
            Origin = SettingsOrigin.Defaults;
        }
    }

    public GlobalSettingsProvider(CockpitDatabase? database, string cachePath)
        : this(database, new GlobalSettingsCache(cachePath)) { }

    public CockpitGlobals Current { get; private set; }

    public SettingsOrigin Origin { get; private set; }

    public string? Problem { get; private set; }

    /// <summary>Kurzfassung fuer die Statusleiste, oder <c>null</c> wenn alles
    /// normal ist (frisch aus der Datenbank).</summary>
    public string? StatusHint => Origin switch
    {
        SettingsOrigin.Database => null,
        SettingsOrigin.Cache => "Zentrale Einstellungen aus lokalem Cache — FOC-SQL01 nicht erreichbar.",
        _ => "Zentrale Einstellungen nicht verfuegbar — eingebaute Vorgaben aktiv."
    };

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_database is null)
        {
            Log.Debug("Keine Datenbank konfiguriert — bleibe bei {Origin}.", Origin);
            return;
        }

        try
        {
            await using var db = _database.CreateContext();
            var rows = await db.GlobalSettings
                .AsNoTracking()
                .ToDictionaryAsync(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase, ct)
                .ConfigureAwait(false);

            Current = CockpitGlobals.FromRows(rows);
            Origin = SettingsOrigin.Database;
            Problem = null;
            _cache.Write(rows);
            Log.Info("Globale Einstellungen aus der Datenbank gelesen ({Count} Eintraege).", rows.Count);
        }
        catch (Exception ex)
        {
            // Current/Origin bleiben, wie der Konstruktor sie aus dem Cache
            // gesetzt hat — ein Ausfall darf einen brauchbaren Stand nicht
            // gegen Vorgaben eintauschen.
            Log.Warn(ex, "Globale Einstellungen konnten nicht aus der Datenbank gelesen werden — "
                       + "bleibe bei {Origin}.", Origin);
            Problem = ex.Message;
        }
    }

    public async Task SaveAsync(CockpitGlobals globals, string changedBy, CancellationToken ct = default)
    {
        if (_database is null)
            throw new InvalidOperationException(
                "Ohne Datenbankverbindung koennen zentrale Einstellungen nicht gespeichert werden.");

        await using var db = _database.CreateContext();

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
        _cache.Write(globals.ToRows());
    }
}
