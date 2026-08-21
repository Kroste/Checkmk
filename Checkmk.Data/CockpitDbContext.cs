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
    /// Skripten in <c>db/</c> passen (aktuell 006-area-hosttag.sql).</summary>
    public const int ExpectedSchemaVersion = 6;

    public DbSet<SchemaVersionRow> SchemaVersion => Set<SchemaVersionRow>();
    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();
    public DbSet<HostDomain> HostDomains => Set<HostDomain>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<HostArea> HostAreas => Set<HostArea>();
    public DbSet<AreaSite> AreaSites => Set<AreaSite>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<AppAdmin> AppAdmins => Set<AppAdmin>();
    public DbSet<HostFilterRow> HostFilters => Set<HostFilterRow>();
    public DbSet<HostFilterHostRow> HostFilterHosts => Set<HostFilterHostRow>();

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
            e.Property(x => x.HostPattern).HasMaxLength(400);
            e.Property(x => x.ExternalCode).HasMaxLength(64);
            e.Property(x => x.HostTag).HasMaxLength(128);
            // Bewusst keine Navigations-Property auf den Elternteil: der Baum
            // wird komplett geladen und im Speicher gebaut (ein paar Dutzend
            // Zeilen), Lazy Loading auf einer Selbstreferenz waere ein
            // N+1-Generator ohne Gegenwert.
        });

        b.Entity<AreaSite>(e =>
        {
            e.ToTable("AreaSite");
            e.HasKey(x => new { x.AreaId, x.Site });
            e.Property(x => x.Site).HasMaxLength(128);
        });

        b.Entity<HostArea>(e =>
        {
            e.ToTable("HostArea");
            e.HasKey(x => x.HostName);
            e.Property(x => x.HostName).HasMaxLength(255);
            e.Property(x => x.AssignedBy).HasMaxLength(128);
        });

        b.Entity<Team>(e =>
        {
            e.ToTable("Team");
            e.HasKey(x => x.TeamId);
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Description).HasMaxLength(400);
        });

        b.Entity<TeamMember>(e =>
        {
            e.ToTable("TeamMember");
            e.HasKey(x => new { x.TeamId, x.UserName });
            e.Property(x => x.UserName).HasMaxLength(128);
            // Beziehung deklarieren, obwohl es keine Navigations-Property gibt:
            // Ohne sie kennt EF die Abhaengigkeit nicht und darf den Elternsatz
            // zuerst loeschen. Die Datenbank raeumt dann per ON DELETE CASCADE
            // die Kinder weg, und EFs eigenes DELETE trifft nichts mehr —
            // Ergebnis ist eine DbUpdateConcurrencyException („expected 1 row,
            // affected 0"), die wie ein Nebenlaeufigkeitsproblem aussieht und
            // keines ist.
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AppAdmin>(e =>
        {
            e.ToTable("AppAdmin");
            e.HasKey(x => x.UserName);
            e.Property(x => x.UserName).HasMaxLength(128);
            e.Property(x => x.AddedBy).HasMaxLength(128);
        });

        b.Entity<HostFilterRow>(e =>
        {
            e.ToTable("HostFilter");
            e.HasKey(x => x.HostFilterId);
            e.Property(x => x.OwnerUserName).HasMaxLength(128);
            e.Property(x => x.Site).HasMaxLength(128);
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.HostNameRegex).HasMaxLength(400);
            e.Property(x => x.ChangedBy).HasMaxLength(128);
            // Bewusst KEIN Cascade — so steht es auch in der Datenbank. Ein Team
            // zu loeschen soll nicht unbemerkt die geteilten Filter mitnehmen;
            // TeamStore raeumt sie ausdruecklich und nennt vorher die Zahl.
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<HostFilterHostRow>(e =>
        {
            e.ToTable("HostFilterHost");
            e.HasKey(x => new { x.HostFilterId, x.HostName });
            e.Property(x => x.HostName).HasMaxLength(255);
            e.HasOne<HostFilterRow>().WithMany().HasForeignKey(x => x.HostFilterId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
