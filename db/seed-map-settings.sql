/*
    seed-map-settings.sql — Kartenquelle als Zeilen in dbo.GlobalSetting.

    OPTIONAL. Die Anwendung kennt dieselben Werte als eingebaute Vorgabe und
    laeuft auch ohne diese Zeilen. Sinn des Skripts: die Einstellung sichtbar
    und aenderbar machen — wer die Kartenquelle wechseln will, aendert dann
    eine Zeile statt eine Anwendung.

    Kein Schema-Eingriff, deshalb auch KEIN Hochsetzen von SchemaVersion:
    Schluessel/Wert ist genau dafuer gemacht (siehe db/README.md).

    Mit dem SA-Konto ausfuehren. Idempotent.
*/

SET NOCOUNT ON;
GO

/* Digitale Orthophotos 20 cm, LGB Brandenburg — Open Data (dl-de/by-2.0).

   Bewusst der WMS- und nicht der WMTS-Endpunkt: Das Matrix-Set grid_3857 der
   LGB hat einen auf Brandenburg beschraenkten Ursprung und weist globale
   Slippy-Map-Kachelindizes mit TileOutOfRange ab. Ueber GetMap gibt der Client
   die Bounding-Box selbst vor; MapProxy liefert trotzdem aus seinem Cache.

   Verifiziert am 2026-08-21 aus dem Netz des Fachbereichs: Kachel
   z13/4393/2691 (Potsdam Innenstadt) lieferte 156 KB Bilddaten. */
MERGE dbo.GlobalSetting AS target
USING (VALUES
    (N'MapWmsUrl',      N'https://isk.geobasis-bb.de/mapproxy/dop20c/service/wms'),
    (N'MapWmsLayer',    N'bebb_dop20c'),
    -- Namensnennung ist Lizenzpflicht, nicht Zierde. Steht fest im Kartenbild.
    (N'MapAttribution', N'© GeoBasis-DE/LGB, dl-de/by-2-0'),
    /* Auswaehlbare Hintergruende fuer den Umschalter im Bereiche-Tab.
       Alle vier am 2026-08-21 gegen den Dienst geprueft (echte Kacheln fuer
       Potsdam). Auf dem Luftbild sind eingefaerbte Flaechen schwer zu lesen,
       weil der Untergrund selbst bunt ist — deshalb gehoert mindestens eine
       Karte ohne Foto dazu. Reihenfolge = Reihenfolge im Auswahlfeld,
       der erste Eintrag ist die Vorgabe. */
    /* Gemeinsamer Kachelspeicher. LEER = nur lokaler Cache je Arbeitsplatz.

       Eintragen lohnt sich: Eine kalte Kachel kostet gut eine Sekunde, aus dem
       Cache acht Millisekunden. Ohne gemeinsamen Ordner laedt jeder der 48
       Nutzer dieselben ~200 MB einzeln beim Landesdienst. Mit ihm zahlt der
       Erste die Wartezeit, alle anderen lesen. Leserecht genuegt; wer schreiben
       darf, fuellt ihn im Vorbeigehen mit.
       Beispiel: \\Samba01\542$\5424_IT-Basis-Dienste\_Oste\CheckMK\tiles */
    (N'MapTileSharePath', N''),

    /* Ab welchem Alter eine Kachel im Hintergrund erneuert wird. Angezeigt
       wird immer sofort der vorhandene Stand - niemand wartet auf eine
       Auffrischung. 0 = nie. Orthophotos werden jaehrlich beflogen, oefter
       nachzuladen kostet Bandbreite ohne Gegenwert. */
    (N'MapTileMaxAgeDays', N'180'),

    (N'MapLayers', N'[
      {"Name":"Luftbild",           "Url":"https://isk.geobasis-bb.de/mapproxy/dop20c/service/wms",         "Layer":"bebb_dop20c"},
      {"Name":"Stadtplan",          "Url":"https://isk.geobasis-bb.de/mapproxy/basemapde-bebb/service/wms", "Layer":"basemapde_farbe"},
      {"Name":"Topographisch grau", "Url":"https://isk.geobasis-bb.de/mapproxy/dtk10grau/service/wms",      "Layer":"bb_dtk10_grau"},
      {"Name":"Luftbild grau",      "Url":"https://isk.geobasis-bb.de/mapproxy/dop20g/service/wms",         "Layer":"bebb_dop20g"},
      {"Name":"Liegenschaftskarte", "Url":"https://isk.geobasis-bb.de/ows/alkis_wms",
       "Layer":"adv_alkis_tatsaechliche_nutzung,adv_alkis_flurstuecke,adv_alkis_gebaeude"},
      {"Name":"Stadtkarte Potsdam", "Url":"https://geoportal.potsdam.de/server/services/Stadtkarte/MapServer/WMSServer",
       "Layer":"0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29",
       "Crs":"EPSG:4326"}
    ]')
) AS source ([Key], [Value])
    ON target.[Key] = source.[Key]
WHEN NOT MATCHED THEN
    INSERT ([Key], [Value]) VALUES (source.[Key], source.[Value]);
GO

SELECT [Key], [Value], ChangedBy, ChangedAtUtc
  FROM dbo.GlobalSetting
 WHERE [Key] LIKE 'Map%'
 ORDER BY [Key];
GO
