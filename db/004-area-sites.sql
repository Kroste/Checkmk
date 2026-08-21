/*
    004-area-sites.sql — Bereiche je Checkmk-Site sichtbar machen.

    Hintergrund: LHP und Schul_IT sind heute getrennte Sites. Ohne Zuordnung
    stünden nach dem Schul-Import 82 graue Schul-Marker in der LHP-Sicht, weil
    dort keiner ihrer Hosts existiert.

    Bewusst KEINE Spalte `Site` auf dbo.Area:

    Ein Standort ist ein Ort, kein Site-Eigentum. Im Stadthaus kann Technik aus
    beiden Sites stehen, und wenn die Sites irgendwann zusammengeführt werden,
    soll die Bereichsstruktur unverändert weitergelten. Deshalb eine n:m-Zuordnung
    als reiner **Sichtbarkeitsfilter** — und die Regel:

        KEINE Zeile für einen Bereich  =  in ALLEN Sites sichtbar.

    Damit bleiben alle bestehenden Bereiche unverändert sichtbar, und die
    Zusammenführung in zehn Jahren ist ein DELETE auf diese Tabelle.

    Mit dem SA-Konto ausführen, nach 003-area-points.sql. Idempotent.
*/

SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.AreaSite', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AreaSite
    (
        AreaId    int           NOT NULL,
        Site      nvarchar(128) NOT NULL,
        AddedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_AreaSite_AddedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AreaSite PRIMARY KEY (AreaId, Site),
        -- CASCADE ist hier richtig und anderswo nicht: Die Zeile ist reine
        -- Sichtbarkeit ohne Eigenwert. Ein geloeschter Bereich soll seine
        -- Sichtbarkeitsangaben mitnehmen, nicht als Waisen zurueckbleiben.
        CONSTRAINT FK_AreaSite_Area FOREIGN KEY (AreaId)
            REFERENCES dbo.Area (AreaId) ON DELETE CASCADE
    );
    CREATE INDEX IX_AreaSite_Site ON dbo.AreaSite (Site);
END
GO

DECLARE @missing nvarchar(1000) = N'';

IF OBJECT_ID(N'dbo.AreaSite', N'U') IS NULL SET @missing += N'AreaSite, ';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AreaSite_Site' AND object_id = OBJECT_ID('dbo.AreaSite'))
    SET @missing += N'Index IX_AreaSite_Site, ';

IF LEN(@missing) > 0
BEGIN
    DECLARE @msg nvarchar(1200) =
        N'004-area-sites.sql UNVOLLSTAENDIG. Es fehlen: '
        + LEFT(@missing, LEN(@missing) - 1)
        + N'. SchemaVersion wurde nicht hochgesetzt.';
    RAISERROR(@msg, 16, 1);
END
ELSE
BEGIN
    UPDATE dbo.SchemaVersion
       SET Version = 4, AppliedAtUtc = SYSUTCDATETIME(), AppliedBy = SUSER_SNAME()
     WHERE Id = 1 AND Version < 4;

    PRINT '004-area-sites.sql angewendet (SchemaVersion = 4).';
END
GO
