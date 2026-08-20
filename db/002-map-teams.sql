/*
    002-map-teams.sql — Bereiche (Karte), Teams und geteilte Filter.

    Leitgedanke: Der Ort ist geteilt, das Interesse ist es nicht.
    Ein Serverraum wird EINMAL gezeichnet und ein Gerät EINMAL zugeordnet;
    was ein Team davon sieht, entscheidet allein sein Filter. Sonst zeichnen
    acht Teams denselben Raum acht Mal, und wer einen Switch umträgt, müsste
    es acht Teams sagen.

    Mit dem SA-Konto ausführen, nach 001-initial.sql. Idempotent.
*/

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------------------
   Teams

   Reine Organisation, kein Zugriffsschutz: Alle 48 Personen dürfen alle Hosts
   sehen, und das Laufzeitkonto der Anwendung kann diese Tabellen ohnehin
   schreiben. Teams bündeln Filter, Sichten und die Zuständigkeit für Bereiche.
--------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.Team', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Team
    (
        TeamId       int           NOT NULL IDENTITY(1,1) CONSTRAINT PK_Team PRIMARY KEY,
        Name         nvarchar(128) NOT NULL CONSTRAINT UQ_Team_Name UNIQUE,
        Description  nvarchar(400) NULL,
        CreatedAtUtc datetime2(0)  NOT NULL CONSTRAINT DF_Team_CreatedAtUtc DEFAULT SYSUTCDATETIME()
    );
END
GO

/* Mitgliedschaft ist n:m — wer AD und Exchange macht, steht in beiden Teams.
   UserName ist der blanke Windows-Anmeldename ohne Domänenpräfix, dieselbe
   Schreibweise, die das Cockpit heute schon als Checkmk-Benutzer verwendet. */
IF OBJECT_ID(N'dbo.TeamMember', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TeamMember
    (
        TeamId   int           NOT NULL,
        UserName nvarchar(128) NOT NULL,
        CONSTRAINT PK_TeamMember PRIMARY KEY (TeamId, UserName),
        CONSTRAINT FK_TeamMember_Team FOREIGN KEY (TeamId)
            REFERENCES dbo.Team (TeamId) ON DELETE CASCADE
    );
    CREATE INDEX IX_TeamMember_UserName ON dbo.TeamMember (UserName);
END
GO

/* Wer Teams anlegen und Anmeldungen zuordnen darf. Wer in keinem Team ist,
   sieht alles — das ist der Normalfall, nicht die Ausnahme. */
IF OBJECT_ID(N'dbo.AppAdmin', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppAdmin
    (
        UserName  nvarchar(128) NOT NULL CONSTRAINT PK_AppAdmin PRIMARY KEY,
        AddedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_AppAdmin_AddedAtUtc DEFAULT SYSUTCDATETIME(),
        AddedBy    nvarchar(128) NOT NULL CONSTRAINT DF_AppAdmin_AddedBy   DEFAULT SUSER_SNAME()
    );
END
GO

/* ---------------------------------------------------------------------------
   Bereiche — geteilt und hierarchisch.

   Ein Baum, weil Stadtsicht und Campus-Sicht dasselbe auf zwei Zoomstufen sind:
       ZR2 -> Serverraum 3, Serverraum 4
       Campus -> Bereich A .. D
   Der Status rollt von unten nach oben durch, schlechtester gewinnt.

   GeometryJson hält ein GeoJSON-Polygon in WGS84. Bewusst kein geography-Typ:
   dafür bräuchte EF NetTopologySuite (ein weiteres Paket, das hier von Hand
   beschafft werden müsste), und wir rechnen ohnehin nichts räumlich — wir
   zeichnen nur und prüfen Klicks im Client.

   MapLayerKey benennt die Rasterquelle, über der das Polygon liegt (z. B. ein
   WMTS-Layer der LGB oder ein hinterlegter Gebäudeplan). NULL = vom Elternteil erben.
--------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.Area', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Area
    (
        AreaId       int           NOT NULL IDENTITY(1,1) CONSTRAINT PK_Area PRIMARY KEY,
        ParentAreaId int           NULL,
        Name         nvarchar(200) NOT NULL,
        GeometryJson nvarchar(max) NULL,
        MapLayerKey  nvarchar(128) NULL,
        SortOrder    int           NOT NULL CONSTRAINT DF_Area_SortOrder DEFAULT 0,
        OwningTeamId int           NULL,
        ChangedAtUtc datetime2(0)  NOT NULL CONSTRAINT DF_Area_ChangedAtUtc DEFAULT SYSUTCDATETIME(),
        ChangedBy    nvarchar(128) NOT NULL CONSTRAINT DF_Area_ChangedBy    DEFAULT SUSER_SNAME(),
        -- Kein ON DELETE CASCADE auf der Selbstreferenz: SQL Server lässt das
        -- nicht zu, und stilles Wegräumen ganzer Teilbäume will hier auch niemand.
        CONSTRAINT FK_Area_Parent FOREIGN KEY (ParentAreaId)
            REFERENCES dbo.Area (AreaId),
        CONSTRAINT FK_Area_OwningTeam FOREIGN KEY (OwningTeamId)
            REFERENCES dbo.Team (TeamId)
    );

    /* Namen eindeutig je Ebene. Zwei gefilterte Indizes statt eines UNIQUE:
       SQL Server erlaubt in einem UNIQUE-Constraint nur EINEN NULL-Wert —
       mit ParentAreaId IS NULL gäbe es sonst genau einen Wurzelbereich. */
    CREATE UNIQUE INDEX UX_Area_Root_Name
        ON dbo.Area (Name) WHERE ParentAreaId IS NULL;
    CREATE UNIQUE INDEX UX_Area_Child_Name
        ON dbo.Area (ParentAreaId, Name) WHERE ParentAreaId IS NOT NULL;
END
GO

/* Wo ein Gerät steht, ist eine physische Tatsache und keine Meinung — deshalb
   HostName als Primärschlüssel: genau ein Bereich pro Host. Damit ist auch die
   Frage „wer hat sw042 verschoben" eine Abfrage und keine Diskussion. */
IF OBJECT_ID(N'dbo.HostArea', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HostArea
    (
        HostName      nvarchar(255) NOT NULL CONSTRAINT PK_HostArea PRIMARY KEY,
        AreaId        int           NOT NULL,
        AssignedAtUtc datetime2(0)  NOT NULL CONSTRAINT DF_HostArea_AssignedAtUtc DEFAULT SYSUTCDATETIME(),
        AssignedBy    nvarchar(128) NOT NULL CONSTRAINT DF_HostArea_AssignedBy    DEFAULT SUSER_SNAME(),
        CONSTRAINT FK_HostArea_Area FOREIGN KEY (AreaId) REFERENCES dbo.Area (AreaId)
    );
    CREATE INDEX IX_HostArea_AreaId ON dbo.HostArea (AreaId);
END
GO

/* ---------------------------------------------------------------------------
   Host-Filter — bisher user-lokal in filter.json, künftig teilbar.

   Das ist der Teil, der im Alltag am schnellsten auffällt: Heute baut sich
   jeder der 48 seinen eigenen Filter, und wenn der Netzwerkkollege im Urlaub
   ist, fängt die Vertretung bei null an.

   TeamId gesetzt = Team-Filter, OwnerUserName gesetzt = persönlich. Genau eins
   von beidem, dafür der CHECK.
--------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.HostFilter', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HostFilter
    (
        HostFilterId  int           NOT NULL IDENTITY(1,1) CONSTRAINT PK_HostFilter PRIMARY KEY,
        TeamId        int           NULL,
        OwnerUserName nvarchar(128) NULL,
        Site          nvarchar(128) NOT NULL,   -- Filter sind pro Checkmk-Site organisiert
        Name          nvarchar(128) NOT NULL,
        HostNameRegex nvarchar(400) NULL,
        ChangedAtUtc  datetime2(0)  NOT NULL CONSTRAINT DF_HostFilter_ChangedAtUtc DEFAULT SYSUTCDATETIME(),
        ChangedBy     nvarchar(128) NOT NULL CONSTRAINT DF_HostFilter_ChangedBy    DEFAULT SUSER_SNAME(),
        CONSTRAINT CK_HostFilter_Owner CHECK
            ((TeamId IS NULL) <> (OwnerUserName IS NULL)),
        -- Kein CASCADE: sonst entstünden über TeamView zwei Löschpfade zu Team,
        -- und die lehnt SQL Server ab. Ein Team räumt die Anwendung in einer
        -- Transaktion ab.
        CONSTRAINT FK_HostFilter_Team FOREIGN KEY (TeamId) REFERENCES dbo.Team (TeamId)
    );
    CREATE INDEX IX_HostFilter_Team  ON dbo.HostFilter (TeamId, Site);
    CREATE INDEX IX_HostFilter_Owner ON dbo.HostFilter (OwnerUserName, Site);
END
GO

/* Include-Liste des Filters (leer = es gilt HostNameRegex). */
IF OBJECT_ID(N'dbo.HostFilterHost', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HostFilterHost
    (
        HostFilterId int           NOT NULL,
        HostName     nvarchar(255) NOT NULL,
        CONSTRAINT PK_HostFilterHost PRIMARY KEY (HostFilterId, HostName),
        CONSTRAINT FK_HostFilterHost_Filter FOREIGN KEY (HostFilterId)
            REFERENCES dbo.HostFilter (HostFilterId) ON DELETE CASCADE
    );
END
GO

/* ---------------------------------------------------------------------------
   Team-Sicht — die Linse auf die geteilte Karte.

   Die Farbe eines Bereichs entsteht erst hier: schlechtester Status der Hosts,
   die (a) in dem Bereich stehen und (b) auf den Filter dieser Sicht passen.
   Derselbe Serverraum ist für das DB-Team grün, für den Wachschutz rot, wenn
   die USV Netzausfall meldet.
--------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.TeamView', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TeamView
    (
        TeamViewId   int           NOT NULL IDENTITY(1,1) CONSTRAINT PK_TeamView PRIMARY KEY,
        TeamId       int           NOT NULL,
        Name         nvarchar(128) NOT NULL,
        RootAreaId   int           NULL,   -- NULL = ganze Stadt
        HostFilterId int           NULL,   -- NULL = alle Hosts
        IsDefault    bit           NOT NULL CONSTRAINT DF_TeamView_IsDefault DEFAULT 0,
        ChangedAtUtc datetime2(0)  NOT NULL CONSTRAINT DF_TeamView_ChangedAtUtc DEFAULT SYSUTCDATETIME(),
        ChangedBy    nvarchar(128) NOT NULL CONSTRAINT DF_TeamView_ChangedBy    DEFAULT SUSER_SNAME(),
        CONSTRAINT UQ_TeamView_Name UNIQUE (TeamId, Name),
        CONSTRAINT FK_TeamView_Team   FOREIGN KEY (TeamId)       REFERENCES dbo.Team (TeamId),
        CONSTRAINT FK_TeamView_Area   FOREIGN KEY (RootAreaId)   REFERENCES dbo.Area (AreaId),
        CONSTRAINT FK_TeamView_Filter FOREIGN KEY (HostFilterId) REFERENCES dbo.HostFilter (HostFilterId)
    );
END
GO

/* ---------------------------------------------------------------------------
   Erster Administrator. Ohne einen Eintrag hier kann niemand Teams anlegen —
   das ist der einzige Handgriff, der von Hand passieren muss.
   Anzupassen, falls der Anmeldename anders lautet.
--------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.AppAdmin)
BEGIN
    INSERT dbo.AppAdmin (UserName) VALUES (N'OsteL');
END
GO

MERGE dbo.SchemaVersion AS target
USING (SELECT 1 AS Id, 2 AS Version) AS source
    ON target.Id = source.Id
WHEN MATCHED AND target.Version < source.Version THEN
    UPDATE SET Version = source.Version, AppliedAtUtc = SYSUTCDATETIME(), AppliedBy = SUSER_SNAME()
WHEN NOT MATCHED THEN
    INSERT (Id, Version) VALUES (source.Id, source.Version);
GO

PRINT '002-map-teams.sql angewendet.';
GO
