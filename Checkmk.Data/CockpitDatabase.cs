using Microsoft.EntityFrameworkCore;
using NLog;

namespace Checkmk.Data;

/// <summary>Wie die zuletzt gelieferten globalen Einstellungen zustande kamen.</summary>
public enum SettingsOrigin
{
    /// <summary>Frisch aus der Datenbank.</summary>
    Database,

    /// <summary>Aus dem lokalen Ausfall-Cache — die Datenbank war nicht erreichbar.</summary>
    Cache,

    /// <summary>Weder Datenbank noch Cache: eingebaute Vorgaben.</summary>
    Defaults
}

/// <summary>Ergebnis eines Verbindungsversuchs, fuer Statusleiste und Log.</summary>
public sealed record DatabaseHealth(
    bool Reachable,
    int? SchemaVersion,
    string? Problem)
{
    public bool SchemaMatches => SchemaVersion == CockpitDbContext.ExpectedSchemaVersion;
}

/// <summary>
/// Fabrik fuer <see cref="CockpitDbContext"/>. Bewusst kein DbContext im DI als
/// Singleton: ein DbContext ist nicht threadsicher, und im Cockpit greifen
/// Hintergrund-Refresh und UI gleichzeitig zu. Jeder Vorgang bekommt seinen eigenen.
/// </summary>
public sealed class CockpitDatabase
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly DbContextOptions<CockpitDbContext> _options;

    public CockpitDatabase(string connectionString)
    {
        ConnectionString = connectionString;
        _options = new DbContextOptionsBuilder<CockpitDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                // Ein Cluster failt gelegentlich ueber. Drei Versuche mit
                // Backoff kosten nichts und ersparen dem Anwender eine
                // Fehlermeldung fuer etwas, das sich in zwei Sekunden erledigt.
                sql.EnableRetryOnFailure(maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
                sql.CommandTimeout(15);
            })
            .Options;
    }

    public string ConnectionString { get; }

    public CockpitDbContext CreateContext() => new(_options);

    /// <summary>
    /// Erreichbarkeit und Schema-Stand pruefen. Wirft nicht — der Aufrufer soll
    /// entscheiden, ob er in den Cache-Betrieb faellt.
    /// </summary>
    public async Task<DatabaseHealth> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = CreateContext();
            var row = await db.SchemaVersion
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == 1, ct)
                .ConfigureAwait(false);

            if (row is null)
            {
                return new DatabaseHealth(true, null,
                    "Tabelle SchemaVersion ist leer — Skripte aus db/ wurden nie ausgefuehrt.");
            }

            if (row.Version != CockpitDbContext.ExpectedSchemaVersion)
            {
                return new DatabaseHealth(true, row.Version,
                    $"Schema-Version {row.Version}, erwartet {CockpitDbContext.ExpectedSchemaVersion}. "
                    + "Skripte aus db/ nachziehen.");
            }

            return new DatabaseHealth(true, row.Version, null);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Zentrale Datenbank nicht erreichbar.");
            return new DatabaseHealth(false, null, ex.Message);
        }
    }
}
