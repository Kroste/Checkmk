/*
    003-area-points.sql — Bereiche dürfen ein Punkt sein, nicht nur eine Fläche.

    Hintergrund: Die meisten Standorte („Außenstelle X", „Stadthaus") sind auf
    einer Stadtkarte sinnvoll ein Marker. Nur wo es auf den Umriss ankommt —
    Campus mit mehreren Serverräumen — lohnt das Zeichnen einer Fläche.
    Beides ist erlaubt: Punkt als Normalfall, Polygon als Ergänzung. Ein
    Bereich mit beidem wird als Fläche gezeichnet.

    Zusätzlich Herkunftsfelder, damit sich Standorte aus dem Kartenserver der
    Landeshauptstadt (FeatureServer Verwaltung_LH_Potsdam) importieren und
    später erneut abgleichen lassen, ohne Dubletten zu erzeugen.

    Mit dem SA-Konto ausführen, nach 002-map-teams.sql. Idempotent.
*/

SET NOCOUNT ON;
GO

IF COL_LENGTH('dbo.Area', 'Lat') IS NULL
    ALTER TABLE dbo.Area ADD Lat float NULL;
GO
IF COL_LENGTH('dbo.Area', 'Lon') IS NULL
    ALTER TABLE dbo.Area ADD Lon float NULL;
GO

/* Anschrift zur Anzeige — der Import bringt sie mit, und ohne sie ist ein
   Marker auf der Karte schwer einer Außenstelle zuzuordnen. */
IF COL_LENGTH('dbo.Area', 'Address') IS NULL
    ALTER TABLE dbo.Area ADD [Address] nvarchar(300) NULL;
GO

/* Herkunft: woher der Bereich stammt und wie er dort heisst.
   ExternalSource z. B. 'LHP-Verwaltungsstandorte', ExternalId die GLOBALID. */
IF COL_LENGTH('dbo.Area', 'ExternalSource') IS NULL
    ALTER TABLE dbo.Area ADD ExternalSource nvarchar(64) NULL;
GO
IF COL_LENGTH('dbo.Area', 'ExternalId') IS NULL
    ALTER TABLE dbo.Area ADD ExternalId nvarchar(128) NULL;
GO

/* Gefilterter eindeutiger Index: Ein importierter Standort darf genau einmal
   existieren, von Hand angelegte Bereiche (beide Felder NULL) bleiben davon
   unberuehrt. Ohne den Filter waere schon der zweite handangelegte Bereich
   eine Schluesselverletzung. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Area_External' AND object_id = OBJECT_ID('dbo.Area'))
    CREATE UNIQUE INDEX UX_Area_External ON dbo.Area (ExternalSource, ExternalId)
        WHERE ExternalSource IS NOT NULL AND ExternalId IS NOT NULL;
GO

/* Version erst stempeln, wenn wirklich alles steht — siehe 001/002. */
DECLARE @missing nvarchar(1000) = N'';

IF COL_LENGTH('dbo.Area', 'Lat')            IS NULL SET @missing += N'Area.Lat, ';
IF COL_LENGTH('dbo.Area', 'Lon')            IS NULL SET @missing += N'Area.Lon, ';
IF COL_LENGTH('dbo.Area', 'Address')        IS NULL SET @missing += N'Area.Address, ';
IF COL_LENGTH('dbo.Area', 'ExternalSource') IS NULL SET @missing += N'Area.ExternalSource, ';
IF COL_LENGTH('dbo.Area', 'ExternalId')     IS NULL SET @missing += N'Area.ExternalId, ';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Area_External' AND object_id = OBJECT_ID('dbo.Area'))
    SET @missing += N'Index UX_Area_External, ';

IF LEN(@missing) > 0
BEGIN
    DECLARE @msg nvarchar(1200) =
        N'003-area-points.sql UNVOLLSTAENDIG. Es fehlen: '
        + LEFT(@missing, LEN(@missing) - 1)
        + N'. SchemaVersion wurde nicht hochgesetzt.';
    RAISERROR(@msg, 16, 1);
END
ELSE
BEGIN
    UPDATE dbo.SchemaVersion
       SET Version = 3, AppliedAtUtc = SYSUTCDATETIME(), AppliedBy = SUSER_SNAME()
     WHERE Id = 1 AND Version < 3;

    PRINT '003-area-points.sql angewendet (SchemaVersion = 3).';
END
GO
