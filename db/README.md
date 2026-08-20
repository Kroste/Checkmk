# Datenbank `CheckMK_Copilot` (FOC-SQL01)

Zentrale Ablage für alles, was **allen** Cockpit-Nutzern gemeinsam gehört:
globale Vorgaben, Host-Metadaten, Bereiche der Karte, Teams und geteilte Filter.

Was hier bewusst **nicht** hinein gehört: das Verbindungs-Secret und die
SSH-Passwörter. Die bleiben user-lokal unter `%APPDATA%\Kroste\Checkmk\` und mit
DPAPI an den Windows-User gebunden. Ein Geheimnis in einer Tabelle, die 48 Leute
lesen dürfen, ist keins mehr — unabhängig davon, ob die Datenbank verschlüsselt ist.

## Zwei Konten, mit Absicht

| Konto | Rechte | Wer benutzt es |
|---|---|---|
| `CheckMK_Copilot_SA` | `db_owner` | Administrator, nur zum Ausführen der Skripte in diesem Ordner |
| `CheckMK_Copilot_Worker` | `db_datareader` + `db_datawriter` | die ausgelieferte Anwendung |

Die Anwendung braucht zur Laufzeit **kein** `db_owner`. Sie liest und schreibt
Zeilen, mehr nicht. Da der Verbindungsstring mit der EXE auf ~50 Arbeitsplätzen
liegt und dort bestenfalls verschleiert ist, entscheidet allein dieses Recht,
was jemand anrichten kann, der ihn ausliest: Zeilen ändern ja, Tabellen löschen
nein. Das Trennen der Konten ist deshalb keine Förmlichkeit — es ist der einzige
wirksame Schutz an dieser Stelle.

## Migrationen laufen nicht vom Client

Kein `Database.Migrate()` beim Start. Sonst rennen 50 Clients gleichzeitig in ein
DDL-Update, für das die meisten gar keine Rechte haben — und der Erste, der
gewinnt, entscheidet über den Rest.

Stattdessen: Der Administrator führt die Skripte in Reihenfolge mit dem
SA-Konto aus, die Anwendung prüft beim Start nur `dbo.SchemaVersion` und sagt
klar Bescheid, wenn Anwendung und Schema nicht zusammenpassen.

Die Skripte sind **idempotent** (`IF NOT EXISTS`), ein zweiter Lauf schadet also
nicht. Reihenfolge:

```
001-initial.sql      Schema-Version, globale Einstellungen, Host-Domains
002-map-teams.sql    Bereiche, Host-Zuordnung, Teams, geteilte Filter, Sichten
```

## Wenn die Datenbank nicht erreichbar ist

Die Verfügbarkeit des Fileshares war der Grund, hier überhaupt hinzuziehen — also
darf die Datenbank nicht der nächste Engpass werden. Die Anwendung legt nach
jedem erfolgreichen Lesen eine Kopie der globalen Einstellungen unter
`%APPDATA%\Kroste\Checkmk\globals-cache.json` ab und startet damit weiter, wenn
FOC-SQL01 nicht antwortet. Sichtbar wird das in der Statusleiste, nicht nur im Log.

## Verbindungsstring

Zum Entwickeln liegt er user-lokal in `%APPDATA%\Kroste\Checkmk\db-dev.json` —
außerhalb des Repos, damit er nicht versehentlich mitcommittet wird.

```json
{
  "ConnectionString": "Server=FOC-SQL01;Database=CheckMK_Copilot;User Id=CheckMK_Copilot_Worker;Password=…;Encrypt=True;TrustServerCertificate=True"
}
```

`TrustServerCertificate=True` steht dort, weil `Microsoft.Data.SqlClient` seit
Version 4 standardmäßig verschlüsselt und ein selbstsigniertes Serverzertifikat
sonst den ersten Verbindungsversuch mit einer Meldung abbricht, die nach einem
Passwortproblem aussieht. Hat FOC-SQL01 ein reguläres Zertifikat, kann die
Option weg.
