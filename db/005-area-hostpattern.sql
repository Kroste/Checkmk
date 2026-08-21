/*
    005-area-hostpattern.sql — Namensmuster je Bereich für Zuordnungsvorschläge.

    Hintergrund: 93 Bereiche stehen, aber nur eine Handvoll Hosts ist zugeordnet.
    Über 1000 Geräte von Hand zu verteilen ist keine Option — die Namen tragen
    die Information aber schon:

        Schule 46  ->  46-SW04, 46-USV, NAS46-01, PA46-01, ESX46-02, iRMC-46

    Für Schulen lässt sich das Muster aus SCHULNUM des städtischen
    Kartenservers ableiten (45 von 82 tragen eine reine Nummer, dazu drei
    zusammengelegte wie 25/26). Für die Verwaltungsstandorte stehen die Kürzel
    nicht in den offenen Daten — die trägt man einmal je Bereich ein, das sind
    35 Eingaben statt tausend Zuordnungen.

    Das Muster ist ein regulärer Ausdruck. Der Import erzeugt ihn mit
    Ziffern-Grenze, damit Schule 4 nicht die Hosts der Schulen 40–49 einsammelt.

    Mit dem SA-Konto ausführen, nach 004-area-sites.sql. Idempotent.
*/

SET NOCOUNT ON;
GO

IF COL_LENGTH('dbo.Area', 'HostPattern') IS NULL
    ALTER TABLE dbo.Area ADD HostPattern nvarchar(400) NULL;
GO

/* Herkunfts-Code (z. B. SCHULNUM). Getrennt vom Muster, damit ein erneuter
   Import erkennt, ob sich der Code geaendert hat — ohne ein von Hand
   angepasstes Muster zu ueberschreiben. */
IF COL_LENGTH('dbo.Area', 'ExternalCode') IS NULL
    ALTER TABLE dbo.Area ADD ExternalCode nvarchar(64) NULL;
GO

DECLARE @missing nvarchar(1000) = N'';

IF COL_LENGTH('dbo.Area', 'HostPattern')  IS NULL SET @missing += N'Area.HostPattern, ';
IF COL_LENGTH('dbo.Area', 'ExternalCode') IS NULL SET @missing += N'Area.ExternalCode, ';

IF LEN(@missing) > 0
BEGIN
    DECLARE @msg nvarchar(1200) =
        N'005-area-hostpattern.sql UNVOLLSTAENDIG. Es fehlen: '
        + LEFT(@missing, LEN(@missing) - 1)
        + N'. SchemaVersion wurde nicht hochgesetzt.';
    RAISERROR(@msg, 16, 1);
END
ELSE
BEGIN
    UPDATE dbo.SchemaVersion
       SET Version = 5, AppliedAtUtc = SYSUTCDATETIME(), AppliedBy = SUSER_SNAME()
     WHERE Id = 1 AND Version < 5;

    PRINT '005-area-hostpattern.sql angewendet (SchemaVersion = 5).';
END
GO
