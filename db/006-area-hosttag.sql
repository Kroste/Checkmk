/*
    006-area-hosttag.sql — Checkmk-Host-Tag je Bereich.

    Gemessen am 2026-08-21 auf Site schul_it: 553 von 654 Hosts tragen das
    Attribut `tag_location_school` mit Werten wie `schule_46`. Das ist eine
    deutlich bessere Zuordnungsquelle als der Regex auf den Hostnamen aus
    Skript 005:

      * Es ist im Checkmk-Setup gepflegt, nicht aus dem Namen erschlossen.
      * Es trifft auch Hosts, die sich nicht an die Namenskonvention halten
        (`WLC-01SL-01` gehoert zu schule_01, `QCENTER02` zu keiner).
      * Es ist eindeutig — 49 der 51 Tag-Werte lassen sich genau einer Schule
        zuordnen, keiner ist mehrdeutig.

    Der Regex bleibt trotzdem: Auf Site LHP gibt es praktisch keine Ortstags
    (`tag_location` steht auf 9 von 1438 Hosts), dort traegt der Hostname die
    Information. Beide Wege stehen deshalb nebeneinander, der Tag gewinnt.

    Gespeichert wird der Tag-WERT, nicht der Schluessel — also `schule_46`.
    Der Schluessel (`tag_location_school`) ist eine Eigenschaft der Umgebung
    und steht in GlobalSetting.HostLocationTagKeys, nicht 93-mal in dieser
    Tabelle.

    Warum der Wert und nicht die Nummer: Die Uebersetzung Schulnummer -> Tag
    ist unregelmaessig. Zusammengelegte Schulen stehen mal als `schule_2526`
    (25/26) und mal als `schule_10` (fuer 10/30) in Checkmk. Dieser Abgleich
    laeuft einmal ueber „Tags zuordnen" unter Sichtkontrolle; danach ist die
    Zuordnung ein exakter Stringvergleich und von Hand korrigierbar.

    Mit dem SA-Konto ausfuehren, nach 005-area-hostpattern.sql. Idempotent.
*/

SET NOCOUNT ON;
GO

IF COL_LENGTH('dbo.Area', 'HostTag') IS NULL
    ALTER TABLE dbo.Area ADD HostTag nvarchar(128) NULL;
GO

/* Zwei Bereiche mit demselben Tag wuerden jeden Host doppelt beanspruchen —
   der Vorschlag kaeme als „mehrdeutig" durch und niemand koennte ihn
   aufloesen. Gefiltert, weil NULL der Normalfall ist. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Area_HostTag'
                                           AND object_id = OBJECT_ID('dbo.Area'))
    CREATE UNIQUE INDEX UX_Area_HostTag ON dbo.Area(HostTag) WHERE HostTag IS NOT NULL;
GO

/* Kandidatenliste der Tag-Schluessel, in Reihenfolge. Erster Schluessel, den
   ein Host traegt, gewinnt — dasselbe Muster wie bei HostOsAttributeKeys.
   Aenderbar per UPDATE, ohne neuen Client und ohne DDL. */
IF NOT EXISTS (SELECT 1 FROM dbo.GlobalSetting WHERE [Key] = N'HostLocationTagKeys')
    INSERT INTO dbo.GlobalSetting ([Key], [Value], ChangedAtUtc, ChangedBy)
    VALUES (N'HostLocationTagKeys',
            N'tag_location_school,tag_location_filiale,tag_location',
            SYSUTCDATETIME(), SUSER_SNAME());
GO

DECLARE @missing nvarchar(1000) = N'';

IF COL_LENGTH('dbo.Area', 'HostTag') IS NULL SET @missing += N'Area.HostTag, ';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Area_HostTag'
                                           AND object_id = OBJECT_ID('dbo.Area'))
    SET @missing += N'UX_Area_HostTag, ';
IF NOT EXISTS (SELECT 1 FROM dbo.GlobalSetting WHERE [Key] = N'HostLocationTagKeys')
    SET @missing += N'GlobalSetting.HostLocationTagKeys, ';

IF LEN(@missing) > 0
BEGIN
    DECLARE @msg nvarchar(1200) =
        N'006-area-hosttag.sql UNVOLLSTAENDIG. Es fehlen: '
        + LEFT(@missing, LEN(@missing) - 1)
        + N'. SchemaVersion wurde nicht hochgesetzt.';
    RAISERROR(@msg, 16, 1);
END
ELSE
BEGIN
    UPDATE dbo.SchemaVersion
       SET Version = 6, AppliedAtUtc = SYSUTCDATETIME(), AppliedBy = SUSER_SNAME()
     WHERE Id = 1 AND Version < 6;

    PRINT '006-area-hosttag.sql angewendet (SchemaVersion = 6).';
END
GO
