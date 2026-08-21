using Microsoft.EntityFrameworkCore;

namespace Checkmk.Data;

/// <summary>
/// Zugriff auf die zentrale Cockpit-Datenbank (<c>CheckMK_Copilot</c> auf
/// FOC-SQL01).
///
/// Das Schema entsteht <b>nicht</b> hier, sondern aus den Skripten in
/// <c>db/</c>, die ein Administrator mit dem SA-Konto faehrt. Deshalb gibt es
/// keine Migrationen im Projekt und kein <c>Database.Migrate()</c> beim Start:
/// das Laufzeitkonto hat nur Lese-/Schreibrechte auf Zeilen, und 50 Clients,
/// die beim Start gleichzeitig DDL versuchen, waeren in keiner Lesart eine gute
/// Idee. Passt die Version nicht, sagt die Anwendung das — sie repariert nicht.
/// </summary>
public sealed class CockpitDbContext(DbContextOptions<CockpitDbContext> options)
    : DbContext(options)
{
    /// <summary>Schema-Stand, den dieser Programmstand erwartet. Muss zu den
    /// Skripten in <c>db/</c> passen (aktuell 003-area-points.sql).</summary>
    public const int ExpectedSchemaVersion = 3;

    public DbSet<SchemaVersionRow> SchemaVersion => Set<SchemaVersionRow>();
    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();
    public DbSet<HostDomain> HostDomains => Set<HostDomain>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<HostArea> HostAreas => Set<HostArea>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SchemaVersionRow>(e =>
        {
            e.ToTable("SchemaVersion");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.AppliedBy).HasMaxLength(128);
        });

        b.Entity<GlobalSetting>(e =>
        {
            e.ToTable("GlobalSetting");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(128);
            e.Property(x => x.ChangedBy).HasMaxLength(128);
            // ChangedAtUtc/ChangedBy haben in der Tabelle DEFAULTs. Wir setzen
            // sie trotzdem selbst: SUSER_SNAME() liefert das *Dienstkonto* der
            // Anwendung, nicht den Menschen davor — und wissen wollen wir den.
        });

        b.Entity<HostDomain>(e =>
        {
            e.ToTable("HostDomain");
            e.HasKey(x => x.HostName);
            e.Property(x => x.HostName).HasMaxLength(255);
            e.Property(x => x.Domain).HasMaxLength(255);
            e.Property(x => x.ChangedBy).HasMaxLength(128);
        });

        b.Entity<Area>(e =>
        {
            e.ToTable("Area");
            e.HasKey(x => x.AreaId);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.MapLayerKey).HasMaxLength(128);
            e.Property(x => x.ChangedBy).HasMaxLength(128);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.ExternalSource).HasMaxLength(64);
            e.Property(x => x.ExternalId).HasMaxLength(128);
            // Bewusst keine Navigations-Property auf den Elternteil: der Baum
            // wird komplett geladen und im Speicher gebaut (ein paar Dutzend
            // Zeilen), Lazy Loading auf einer Selbstreferenz waere ein
            // N+1-Generator ohne Gegenwert.
        });

        b.Entity<HostArea>(e =>
        {
            e.ToTable("HostArea");
            e.HasKey(x => x.HostName);
            e.Property(x => x.HostName).HasMaxLength(255);
            e.Property(x => x.AssignedBy).HasMaxLength(128);
        });
    }
}
