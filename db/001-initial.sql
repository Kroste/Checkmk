/*
    001-initial.sql — Grundgerüst der zentralen Cockpit-Datenbank.

    Mit dem SA-Konto (db_owner) ausführen. Idempotent: ein zweiter Lauf
    ändert nichts. Siehe db/README.md.
*/

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------------------
   Schema-Version

   Einzeiler-Tabelle (CHECK Id = 1). Die Anwendung liest sie beim Start und
   verweigert den Dienst, wenn sie eine andere Version erwartet — besser ein
   klarer Hinweis als ein Fehler beim ersten Zugriff auf eine Spalte, die es
   noch nicht gibt.
--------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.SchemaVersion', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SchemaVersion
    (
        Id           int           NOT NULL CONSTRAINT PK_SchemaVersion PRIMARY KEY
                                            CONSTRAINT CK_SchemaVersion_Single CHECK (Id = 1),
        Version      int           NOT NULL,
        AppliedAtUtc datetime2(0)  NOT NULL CONSTRAINT DF_SchemaVersion_AppliedAtUtc DEFAULT SYSUTCDATETIME(),
        AppliedBy    nvarchar(128) NOT NULL CONSTRAINT DF_SchemaVersion_AppliedBy    DEFAULT SUSER_SNAME()
    );
END
GO

/* ---------------------------------------------------------------------------
   Globale Einstellungen — bewusst Schlüssel/Wert statt typisierter Spalten.

   Grund: Migrationen darf nur der Administrator mit dem SA-Konto fahren. Wäre
   jede neue Einstellung eine neue Spalte, bräuchte jede Kleinigkeit einen
   DDL-Termin. So kostet eine zusätzliche Einstellung eine INSERT-Zeile und
   einen typisierten Zugriff im Code.

   Löst die geteilten Felder aus bootstrap.json ab (SharedHostsPath,
   HostDefaultDomain, UpdateChannelUrl, HostOsAttributeKeys, ShowHostCreation).
   In bootstrap.json bleibt nur noch, wo die Datenbank steht.
--------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.GlobalSetting', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.GlobalSetting
    (
        [Key]        nvarchar(128) NOT NULL CONSTRAINT PK_GlobalSetting PRIMARY KEY,
        [Value]      nvarchar(max) NULL,
        ChangedAtUtc datetime2(0)  NOT NULL CONSTRAINT DF_GlobalSetting_ChangedAtUtc DEFAULT SYSUTCDATETIME(),
        ChangedBy    nvarchar(128) NOT NULL CONSTRAINT DF_GlobalSetting_ChangedBy    DEFAULT SUSER_SNAME()
    );
END
GO

/* ---------------------------------------------------------------------------
   Host -> Domain. Löst hosts.json auf dem Fileshare ab.

   Der alte Weg schrieb die komplette Datei zurück, ohne Sperre und ohne vorher
   neu zu laden: zwei gleichzeitige Bearbeiter, und der Eintrag des Ersten war
   lautlos weg. Eine Zeile pro Host macht daraus ein UPDATE, das niemandem
   etwas wegnimmt.

   ChangedBy/ChangedAtUtc sind nicht Zierde: Wer ein Gerät umträgt, verantwortet
   die Zuordnung — dann muss auch nachvollziehbar sein, wer sie zuletzt angefasst hat.
--------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.HostDomain', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HostDomain
    (
        HostName     nvarchar(255) NOT NULL CONSTRAINT PK_HostDomain PRIMARY KEY,
        Domain       nvarchar(255) NOT NULL,
        ChangedAtUtc datetime2(0)  NOT NULL CONSTRAINT DF_HostDomain_ChangedAtUtc DEFAULT SYSUTCDATETIME(),
        ChangedBy    nvarchar(128) NOT NULL CONSTRAINT DF_HostDomain_ChangedBy    DEFAULT SUSER_SNAME()
    );
END
GO

/* Version eintragen bzw. hochsetzen. */
MERGE dbo.SchemaVersion AS target
USING (SELECT 1 AS Id, 1 AS Version) AS source
    ON target.Id = source.Id
WHEN MATCHED AND target.Version < source.Version THEN
    UPDATE SET Version = source.Version, AppliedAtUtc = SYSUTCDATETIME(), AppliedBy = SUSER_SNAME()
WHEN NOT MATCHED THEN
    INSERT (Id, Version) VALUES (source.Id, source.Version);
GO

PRINT '001-initial.sql angewendet.';
GO
