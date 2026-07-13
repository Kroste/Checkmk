# Checkmk Cockpit — Benutzerhandbuch

Ein Windows-Desktop-Tool für den **täglichen Umgang mit Checkmk 2.5** (REST-API v1),
gebaut für den Fachbereich **5424 IT-Basis-Dienste**. Es holt die häufigen
Admin-Handgriffe, die das Checkmk-Webinterface tief in Menüs vergräbt, an die
Zeile, an der du das Problem siehst.

Dieses Handbuch beschreibt alle Funktionen des Cockpits aus Anwendersicht. Wer
sich für Architektur und Interna interessiert: [`CLAUDE.md`](CLAUDE.md).

---

## Inhalt

1. [Installation](#1-installation)
2. [Ersteinrichtung](#2-ersteinrichtung)
3. [Die Oberfläche](#3-die-oberfläche)
4. [Die drei Alltags-Handgriffe](#4-die-drei-alltags-handgriffe)
5. [Tabellen- und Baumansicht](#5-tabellen--und-baumansicht)
6. [Host-Details](#6-host-details)
7. [Filter und Favoriten](#7-filter-und-favoriten)
8. [Regex-Beispiele für Filter](#8-regex-beispiele-für-filter)
9. [Tray und Notifications](#9-tray-und-notifications)
10. [Hosts-Tab: Service Discovery und Änderungen aktivieren](#10-hosts-tab-service-discovery-und-änderungen-aktivieren)
11. [Client-Aktualisierung (Agent-Update)](#11-client-aktualisierung-agent-update)
12. [CSV-Export](#12-csv-export)
13. [Mehrere Sites am selben Server](#13-mehrere-sites-am-selben-server)
14. [Updates](#14-updates)
15. [Wo liegen meine Daten](#15-wo-liegen-meine-daten)
16. [Wenn etwas nicht funktioniert](#16-wenn-etwas-nicht-funktioniert)
17. [Hilfe und Kontakt](#17-hilfe-und-kontakt)

---

## 1. Installation

**Windows (empfohlen):**

1. Neuestes ZIP von den [GitHub Releases](https://github.com/Kroste/Checkmk/releases)
   herunterladen (`Checkmk-x.y.z-win-x64.zip`).
2. In einen beliebigen Ordner entpacken — z. B. `C:\Tools\Checkmk\`.
3. `Checkmk.App.exe` starten.

Das ZIP ist **self-contained** — es ist kein .NET-Runtime auf dem Rechner nötig,
alles Nötige ist im Bundle. Rechnen etwa 130 MB.

**Fileshare-Zugriff** ist für zwei Dinge nötig:

- `\\Samba01\542$\5424_IT-Basis-Dienste\_Oste\CheckMK\bootstrap.json` — App-Konfiguration
  (Update-Kanal, OS-Attribut-Keys, Default-Domain, …), die alle Cockpit-Nutzer teilen.
- `\\Samba01\542$\5424_IT-Basis-Dienste\_Oste\CheckMK\hosts.json` — Domain-Zuordnung
  pro Host (für FQDNs bei Ping/RDP/SSH).

**Deine persönliche Anmeldung** liegt **lokal** unter `%APPDATA%\Kroste\Checkmk\settings.json`
— DPAPI-verschlüsselt (nur du kannst sie entschlüsseln). Ist der Fileshare mal
nicht erreichbar (VPN weg), fällt das Cockpit auf einen lokalen Bootstrap-Cache
zurück und läuft weiter.

**Hinter einem Proxy?** Der Update-Check nutzt automatisch die
Windows-Standard-Anmeldedaten für den Proxy (Negotiate/NTLM). Am FortiProxy des
Fachbereichs funktioniert das ohne Zusatzkonfiguration.

---

## 2. Ersteinrichtung

Beim ersten Start ist noch keine Verbindung eingerichtet — die Statusleiste zeigt
„Nicht konfiguriert". Menüpunkt **„Einstellungen"** oben rechts.

### Anmeldemethode wählen

Ganz oben im Dialog: **„Anmeldemethode"** mit zwei Optionen.

**Windows/LDAP (empfohlen)** — jeder Nutzer meldet sich mit seinem echten
AD-Account an. Damit steht im Checkmk-Audit-Log bei Ack/Downtime dein Name,
nicht „automation-User". Der Anmeldename ist mit deinem Windows-User
vorbelegt; du tippst nur einmal dein AD-Passwort ein. Weil Passwörter im
Fachbereich maximal einmal jährlich rotieren, ist das kein Alltagsaufwand.

**Automation-User (Legacy)** — der klassische Weg mit einem dedizierten
Automation-Account und dessen Secret. Für Skript-artige Nutzung oder für Server,
die noch kein LDAP-Passwort für den User haben.

### Felder ausfüllen

| Feld | Was da rein muss |
|---|---|
| **Host** | Der DNS-Name des Checkmk-Servers, z. B. `monitoring.lhp.intern`. |
| **Site** | Der Site-Name — das URL-Segment hinter dem Host, meist `LHP-Prod` oder `main`. |
| **Bekannte Sites am selben Server** | *Optional.* Kommasepariert, z. B. `LHP-Prod, Schul_IT`. Sobald mehr als eine Site drin steht, erscheint oben rechts ein **Site-Umschalter** (siehe [Abschnitt 13](#13-mehrere-sites-am-selben-server)). |
| **Anmeldename** | Bei Windows/LDAP: dein Windows-User. Bei Automation: der Automation-User-Name (meist `automation`). |
| **Passwort/Secret** | Bei Windows/LDAP: dein AD-Passwort. Bei Automation: das lange Automation-Secret (der Zufalls-String aus der User-Verwaltung, **nicht** das GUI-Passwort). |
| **HTTPS** | Fast immer ja. Nur ausschalten, wenn dein Server nur HTTP kann (Lab). |
| **Zertifikatsfehler ignorieren (Lab)** | Nur setzen bei selbst-signierten Zertifikaten. In Produktion: aus lassen. |

Klick **„Testen"** — das Tool ruft `/version` auf und meldet Edition und
Version. Grün = klappt. Klick **„Speichern"** — Passwort/Secret wird
DPAPI-verschlüsselt in `%APPDATA%\Kroste\Checkmk\settings.json` abgelegt.

### Voraussetzung serverseitig (Windows/LDAP-Modus)

Der Nutzer muss in Checkmk unter *Setup → Users* **„REST API access"** angehakt
haben. LDAP-User bekommen das typischerweise beim Sync gesetzt. Wenn das fehlt,
antwortet Checkmk mit `401 Wrong credentials` — nicht wegen falschem Passwort,
sondern weil dieser User die REST-API gar nicht darf.

---

## 3. Die Oberfläche

Ganz oben die eigene Titelleiste mit:

- links **„Checkmk Cockpit"** + Versions-Badge
- rechts (wenn mehrere Sites konfiguriert sind) ein **Site-Umschalter**
- **„Einstellungen"** und **„Über"**
- Fensterkontroll-Buttons

Darunter drei Reiter:

- **Status** — Live-Status aller überwachten Services (Startseite).
- **Hosts** — Host-Liste im Setup, Service Discovery, Änderungen aktivieren.
- **Dashboard** — Kacheln je Favorit mit Hosts-Zahl und Service-Aggregat.

Am unteren Rand die blaue **Statusleiste** mit:

- links ein **Health-Punkt** (grün = letzter Refresh OK, rot = Fehler) plus die
  aktuelle Rückmeldung („Aktualisiert 14:32:07 — 87 Services, 14 Hosts").
- mittig, wenn zutreffend, ein gelber **Update-Badge**.
- rechts die Verbindungsangabe (`https://monitoring.lhp.intern/LHP-Prod (lars.kruegel)`).

Sobald du das Fenster minimierst, verschwindet die App **ins System-Tray**
(nicht in die Taskleiste). Siehe [Tray und Notifications](#9-tray-und-notifications).

---

## 4. Die drei Alltags-Handgriffe

Ack, Downtime und Kommentar — drei Aktionen, die im Webinterface je 4–6 Klicks
kosten. Hier eine Zeile wählen und ein Menü öffnen.

### Acknowledge (Problem quittieren)

1. Zeile im Status-Tab wählen (oder Rechtsklick).
2. Toolbar-Button **„Acknowledge…"** oder Menüpunkt.
3. Kommentar eingeben — **Pflicht** (Checkmk-Vorgabe).
4. **OK** — die Warnung ist quittiert. In der „Ack"-Spalte steht ein Haken.

### Downtime (geplante Wartung)

1. Zeile wählen, **„Downtime…"** klicken.
2. Kommentar eingeben (Pflicht).
3. **Dauer-Preset** wählen: 1 Stunde, 2 Stunden, 4 Stunden oder „bis morgen
   06:00" (praktisch für Overnight-Wartung).
4. **OK** — Downtime läuft ab **jetzt** bis zum berechneten Ende.

### Kommentar

Kontext an Host oder Service hinterlassen:

- **Status-Tab:** Zeile wählen → **Rechtsklick → „Kommentar…"**.
- **Host-Detail-Fenster:** entweder **„Host-Kommentar…"** oder **„Kommentar…"**
  in der Aktions-Toolbar (letzterer legt am markierten Service an).

Kommentar-Text eingeben (Pflicht) und wählen, ob der Kommentar **persistent**
sein soll (überlebt einen Neustart des Monitorings). Bestehende Kommentare
werden im Host-Detail-Fenster unten als Liste angezeigt (neueste oben, mit
Autor + Zeitstempel).

**Löschen:** in der Kommentarliste hat jeder Eintrag rechts einen roten
✕-Button — Klick löscht den Kommentar sofort (Host- und Service-Kommentare).
Kein Bestätigungs-Dialog; wenn du daneben klickst, kannst du den Kommentar
sofort neu anlegen.

### Bulk-Aktionen — mehrere Services gleichzeitig

1. **Ctrl-Klick** oder **Shift-Klick** in der Service-Tabelle markiert mehrere
   Zeilen.
2. **„Acknowledge…"** oder **„Downtime…"** öffnen den Dialog. Im Ziel steht
   z. B. **„7 Services auf 3 Hosts"**.
3. Ein Kommentar gilt für alle.
4. **OK** — das Tool arbeitet die Auswahl iterativ ab; Fortschritt in der
   Statusleiste: **„Ack 3/12: DBSQL01 / CPU load"**.

Wenn einzelne Aktionen fehlschlagen, bricht der Bulk **nicht ab** — Fehler
werden gesammelt und am Ende gemeldet.

---

## 5. Tabellen- und Baumansicht

Der Status-Tab kann die Services entweder als flache Tabelle oder als **Baum
(Hosts → Services)** zeigen. Umschalter oben in der Toolbar.

**Baum:**

- Jeder Host ist ein oberster Knoten mit **OS-Pictogramm** (Fenster für Windows,
  Tux für Linux, „?" bei unbekanntem OS), Ampel und **Problem-Zähler**.
- Aufgeklappt: die Services des Hosts mit Ausgabe.
- **Rechtsklick** funktioniert kontextabhängig — auf einem Host-Knoten stehen
  andere Aktionen zur Verfügung als auf einem Service-Knoten.

Die **OS-Erkennung** liest primär das Custom Host Attribute (z. B.
„Operation System"), das ihr auf Folder-Ebene setzt und das auf die Hosts
vererbt wird. Fallback ist der Parse aus dem Check_MK-Agent-Service. Der
Attribut-Key ist konfigurierbar in `bootstrap.json` unter
`HostOsAttributeKeys` — Default probiert `tag_operation_system`,
`operation_system`, `operating_system` und `os_family` durch.

---

## 6. Host-Details

**Doppelklick** auf eine Zeile (Status-Tab, Hosts-Tab oder Baum) oder
**Rechtsklick → „Host-Details…"** öffnet ein eigenes Fenster mit:

- **Host-State-Ampel** (UP/DOWN/UNREACH)
- **In-Wartung- und Acknowledged-Badge** neben der Ampel (falls zutreffend)
- **Ordner-Pfad, IP-Adresse, Alias** aus der Config. Fehlt in Checkmk eine IP,
  ermittelt das Tool sie per **Ping/DNS** und markiert die Herkunft.
- **Plugin-Ausgabe** des Host-Checks

Rechts daneben Buttons:

- **„Host-Ack…"** — quittiert das Host-Problem
- **„Host-Downtime…"** — setzt den ganzen Host in Wartung
- **„Host-Kommentar…"** — Kommentar am Host anlegen

Darunter die Service-Tabelle mit Aggregat-Zählern (OK/WARN/CRIT/UNK) und den
bekannten Ack/Downtime/Kommentar-Aktionen. Bulk-Ack und Bulk-Downtime funktionieren
hier genauso.

Ganz unten die Liste **bestehender Kommentare** mit dem ✕-Button zum Löschen.

Mehrere Detail-Fenster können parallel offen sein.

---

## 7. Filter und Favoriten

Wenn ihr über tausend Hosts habt, will keiner alle sehen. Speicherbare Filter —
hier **„Favoriten"** — beschränken die Sicht auf das, was für dich relevant ist.

### Freitext-Filter (immer sichtbar)

Oben im Status-Tab: einfaches Suchfeld. Sucht case-insensitive über **Host,
Service, Ausgabe und Alias**. Ideal um schnell auf „CPU load" oder eine
Ticket-Nummer in der Plugin-Ausgabe zu filtern. **Ctrl+F** fokussiert das Feld,
**Esc** leert es.

### Persistente Favoriten (Combobox)

In der Toolbar (Status-Tab **und** Hosts-Tab) gibt es die Combobox
**„Host-Filter:"**. Wählst du dort einen Favoriten, sind sofort in beiden Tabs
nur noch die passenden Hosts sichtbar. Zurück auf alle: Auswahl leeren
(„(Alle Hosts)").

### Favoriten pro Site

Favoriten sind **pro Site** organisiert — in der Site `LHP-Prod` andere als in
`Schul_IT`. Beim Site-Wechsel lädt das Cockpit automatisch die Favoriten der
neuen Site nach. Neu angelegte Favoriten landen unter der aktuell aktiven Site.

### Favoriten aus einer Auswahl speichern

- **Im Hosts-Tab**: Ctrl-/Shift-Klick markiert mehrere Hosts, dann
  **„Auswahl als Favorit…"** in der Toolbar oder im Kontextmenü.
- **Im Status-Tab**: Rechtsklick auf einen Service (oder mehrere markierte) →
  **„Zu Favorit hinzufügen…"** oder **„Als neuen Favorit speichern…"**. Der
  Hostname wird aus dem Service ermittelt und dedupliziert.

Wenn du zu einem Favoriten hinzufügen willst und noch keiner existiert, legt
das Tool automatisch einen neuen an — der Klick landet nie ins Leere.

### Favoriten verwalten

**„Filter verwalten…"** öffnet ein eigenes Fenster mit einer Liste aller
Favoriten der aktuellen Site. Rechts der Editor mit drei Feldern:

- **Name** — was in der Combobox erscheint.
- **Hostname-Regex** — .NET-Regex, case-insensitive. Siehe
  [Abschnitt 8](#8-regex-beispiele-für-filter) für ausführliche Beispiele.
- **Explizite Hostnamen** — eine feste Liste, ein Hostname pro Zeile. Wenn hier
  etwas steht, wird das **Regex ignoriert** — es zählen exakt diese Hostnamen.

Buttons:

- **„Übernehmen"** — Änderungen speichern. Ein kaputter Regex wird
  **vorher** validiert; die Fehlermeldung erscheint direkt unter dem Regex-Feld
  und der Filter wird nicht gespeichert, bis du ihn korrigierst.
- **„Aktivieren"** — den gewählten Filter sofort aktiv setzen.
- **„Filter deaktivieren"** — kein Filter aktiv, alle Hosts sichtbar.

Favoriten liegen **user-lokal** unter `%APPDATA%\Kroste\Checkmk\filter.json`.
Struktur: pro Site ein eigenes Set. Jeder Kollege hat seine eigenen.

---

## 8. Regex-Beispiele für Filter

Das Regex-Feld nimmt einen **.NET-Regex, case-insensitive**, gematcht wird
**IsMatch** (nicht Full-Match) — die Regel greift also, sobald der Ausdruck
**irgendwo** im Hostnamen passt. `sql` matcht `dbsql01` genauso wie
`sql-cluster-b`.

### Einfache Enthält-Suchen

| Regex | Trifft auf |
|---|---|
| `sql` | `DBSQL01`, `sql-cluster-b`, `mssql-prod`, `dbsql-hotstandby` |
| `sql\|ora` | alle DB-Server (MSSQL **oder** Oracle) |
| `web\|iis\|nginx` | alle Web-Server-Kandidaten |
| `dc0\|dc1` | Domain-Controller `dc0*` und `dc1*` |

### Anker: Anfang und Ende

Der Caret `^` bindet an den **Anfang** des Hostnamens, das Dollar `$` an das
**Ende**.

| Regex | Trifft auf | Trifft *nicht* auf |
|---|---|---|
| `^db-` | `db-prod01`, `db-schulen` | `xdb-prod`, `webdb-01` |
| `-prod$` | `dc0-prod`, `mssql-prod` | `dc0-prod-hot`, `prod-dc0` |
| `^srv-.*-prod$` | `srv-mssql-prod`, `srv-web-prod` | `srv-web-test`, `mssql-prod` |

### Alternativen mit Gruppen

`(a|b|c)` ist eine Gruppe mit Alternativen. Das drumherum funktioniert wie
in normalen Regexen.

| Regex | Trifft auf | Bedeutung |
|---|---|---|
| `^(dc0\|dc1)-` | `dc0-prod`, `dc1-schulen` | DC0- oder DC1-Präfix |
| `-(prod\|test\|dev)$` | `srv-web-prod`, `srv-app-test` | endet auf einer der Umgebungen |
| `^(db\|mssql\|ora).*prod` | `db-cluster-prod`, `mssql-schul-prod` | DB-Präfix + irgendwo „prod" |

### Zeichenklassen

Eckige Klammern definieren eine Zeichenmenge — genau **eines** dieser Zeichen
trifft.

| Regex | Trifft auf |
|---|---|
| `dbsql0[1-9]` | `dbsql01` bis `dbsql09` |
| `srv-[abcd]` | `srv-a`, `srv-b`, `srv-c`, `srv-d` |
| `[a-z]{3}-[0-9]{2}$` | dreistellige Bezeichnung + Bindestrich + zwei Ziffern am Ende: `abc-01`, `dev-42` |

### Zahlen und Bereiche

| Regex | Trifft auf |
|---|---|
| `\d` | irgendeine Ziffer |
| `\d{2,}` | zwei oder mehr aufeinanderfolgende Ziffern |
| `sql\d{2}$` | `sql01`…`sql99` am Ende |
| `(0[1-9]\|1[0-2])$` | endet auf einer Zahl von 01 bis 12 (z. B. für Monatscodes im Hostnamen) |

### Ausschlüsse mit Negation

Regex kann „passt **nicht** auf X" nur über **Lookarounds** — geht in .NET, ist
aber selten die einfachste Lösung.

| Regex | Trifft auf | Bedeutung |
|---|---|---|
| `^(?!.*test).*sql` | `dbsql01` | Enthält „sql", aber **nicht** „test" |
| `^srv-(?!prod)` | `srv-dev-01`, `srv-test-a` | Fängt mit `srv-` an, aber nicht mit `srv-prod` |

Für Ausschlüsse ist eine **Include-Liste** oft einfacher: einfach die Hostnamen
zeilenweise ins Feld „Explizite Hostnamen" schreiben.

### Praxis-Rezepte

**Alle Datenbank-Server (MSSQL, Oracle, PostgreSQL):**
```
sql|ora|pgsql|postgres
```

**Nur Produktions-Web-Server, unabhängig vom Präfix:**
```
(web|iis|nginx|apache).*prod
```

**Alle Terminalserver an drei Standorten:**
```
^ts-(lhp|schul|kita)-\d+
```

**„Kritische Server" mit ein paar Namenskonventionen:**
```
^(dc\d|mssql-cluster|exchange-)|core-network
```

**Alle Hosts eines Kunden anhand Ordner-Namens im Präfix:**
```
^kunde42-
```

### Häufige Fehler

- **`^` mit `IsMatch` vergessen**: `db-` matcht auch `webdb-01`. Wenn du wirklich
  „beginnt mit" willst, brauchst du `^db-`.
- **Wildcard falsch geschrieben**: der Regex-Wildcard ist `.*` (Punkt-Stern),
  nicht `*` alleine.
- **Sonderzeichen nicht escaped**: Klammern, Punkt, Pipe usw. haben in Regex
  eine Sonderbedeutung. Wenn du sie **wörtlich** meinst, `\.` schreiben. Beispiel:
  `srv\.lhp\.intern` — sucht nach dem literalen String „srv.lhp.intern".
- **Case-Sensitivity überdenken**: Das Cockpit setzt automatisch
  `IgnoreCase` — `db-prod` matcht auch `DB-PROD`. Keine Sorge um Groß-/Kleinschreibung.

### Wenn der Regex nicht funktioniert wie erwartet

Zwei einfache Tests:

- **Ins Freitext-Feld tippen**: der macht auch case-insensitive Contains-Match
  auf den Hostnamen. Wenn dein Wunsch-Ergebnis dort schon nicht sichtbar ist,
  ist das kein Regex-Problem sondern eine Frage der Namen.
- **Explizite Liste vorziehen**: bei kleinen, festen Server-Gruppen (< 20 Hosts)
  ist die zeilenweise Liste unschlagbar — kein Regex-Debugging.

Wer .NET-Regex im Browser testen will:
[regex101.com](https://regex101.com) → Flavor auf „.NET" umschalten,
„Case Insensitive" anhaken.

---

## 9. Tray und Notifications

Minimieren legt die App **ins System-Tray**. Das Tray-Icon zeigt per Ampelfarbe
den **schlechtesten Status im aktiven Filter**. Im Tray läuft der Auto-Refresh
weiter, und bei Statusänderungen bekommst du eine **Toast-Notification** —
Action-Center-kompatibel.

Beim ersten Toast legt das Tool automatisch einen Startmenü-Eintrag
„Checkmk Cockpit" an — Windows-Requirement für Toast-Notifications von
unpackaged Apps.

**Wichtig:** Prüfe unter `Win+I` → System → Benachrichtigungen, dass
**„Benachrichtigungen von anderen Apps und Absendern erlauben"** angeschaltet
ist. Ist diese Sammel-Option aus, kommen keine Toasts durch.

Notifications sind **gebündelt** (eine Sammelmeldung statt zehn einzelner Toasts)
und **filter-scoped** (dein DB-Favorit alarmiert nicht bei Web-Server-Ausfällen).

Zurück aus dem Tray: Klick auf das Tray-Icon oder Rechtsklick → **„Anzeigen"**.
Beenden über Rechtsklick → **„Beenden"**.

---

## 10. Hosts-Tab: Service Discovery und Änderungen aktivieren

### Hosts-Liste

Zeigt Hostname, Ordner, IP und Alias jedes konfigurierten Hosts. Die aktuelle
Filter-Auswahl greift auch hier. Doppelklick öffnet das **Host-Detail-Fenster**.

### Änderungen aktivieren

Nach jeder Änderung im Setup: **„Änderungen aktivieren"**.

### Service Discovery — bestehende Hosts ins Monitoring bringen

1. Zeile in der Host-Liste anklicken.
2. Toolbar-Button **„Services entdecken"** oder Rechtsklick →
   **„Services entdecken (fix_all + aktivieren)"**.
3. Das Tool startet einen Hintergrund-Task auf dem Server (`fix_all`), pollt bis
   fertig, aktiviert die Änderungen automatisch, lädt die Liste neu.

### Host anlegen (standardmäßig ausgeblendet)

Das Formular ist per Default **nicht sichtbar** — Setup läuft zentral. Zum
Einblenden: in der zentralen `bootstrap.json` (auf Samba01) `"showHostCreation": true`
setzen.

Bei sichtbarem Formular:

- **Hostname** *(Pflicht)*.
- **Ordner** — **ID-Pfad**, nicht Titel. Root ist `/`, ein DB-Ordner z. B.
  `/datenbanken/db-mssql`.
- **IP-Adresse** — optional.
- **Alias** — optional.

**„Anlegen"** legt den Host im Setup an. Danach fehlt noch die
Service-Discovery, damit er überwacht wird.

---

## 11. Client-Aktualisierung (Agent-Update)

Aus dem Kontextmenü einer Zeile: **„Client aktualisieren…"** startet die
Aktualisierung des Checkmk-Agents auf dem Zielhost. **Windows-only, WinRM am
Zielhost muss aktiv sein**.

Ablauf:

1. **Credentials-Dialog** — Admin-Credentials für die Remote-Session.
2. Installer wird per `Copy-Item -ToSession` auf den Host kopiert.
3. Editierbare **Skript-Vorlage** läuft: Deinstall → Install → Register.
4. Fortschritt und Ausgabe im Fenster; Erfolg am Exit-Code (nicht an stderr).

### Agent-Share und Skript-Vorlage

In den Einstellungen:

- **Agent-Share** — Pfad zum Installer-Paket.
- **Update-Skript-Vorlage** — PowerShell-Code für den Zielhost, editierbar.

**Wichtige Details:**

- Der Register-Befehl braucht `--trust-cert`, sonst hängt sich
  `cmk-agent-ctl register` in einer interaktiven Zertifikatsabfrage auf.
- `msiexec`-Aufrufe im Skript nutzen jetzt `-PassThru` + Exit-Code-Check — die
  Vorlage wirft, wenn Install oder Deinstall mit Non-Zero-ExitCode endet
  (bisher wurden solche Fehler stumm geschluckt).

Wer eine ältere Vorlage gespeichert hat und die neuen Defaults will: in den
Einstellungen den kompletten Skript-Text löschen, Speichern → beim nächsten
Öffnen wird die neue Default-Vorlage geladen.

### Sicherheit

Skript, Passwörter und temporäre Dateien werden **nicht** ins Logfile
geschrieben.

---

## 12. CSV-Export

Toolbar → **„CSV-Export…"**. Exportiert die **aktuell gefilterte Ansicht** —
mit allen Filter-Einstellungen (Favorit, Freitext, „Nur Probleme").

Format:

- Semikolon-getrennt (Excel öffnet das direkt korrekt)
- UTF-8-BOM (Umlaute stimmen)
- RFC-4180-konformes Quoting

---

## 13. Mehrere Sites am selben Server

Wenn ihr am selben Checkmk-Server mehrere Sites betreibt (z. B. `LHP-Prod` für
den Regelbetrieb und `Schul_IT` für die Schulen), muss man nicht ständig die
Verbindung neu einrichten.

**Einrichtung**: in den Einstellungen das Feld **„Bekannte Sites am selben Server"**
mit den Site-Namen kommasepariert füllen, z. B. `LHP-Prod, Schul_IT`. Host,
Anmeldung und Secret bleiben identisch.

**Nutzung**: sobald mehr als eine Site drin steht, erscheint oben rechts in der
Titelleiste ein **Site-Dropdown**. Ein Klick wechselt die aktive Site — das
Cockpit lädt die Livestatus-Daten der neuen Site und wechselt gleichzeitig die
Favoriten auf das Set der neuen Site.

Die zuletzt aktive Site wird gespeichert; beim nächsten App-Start landest du
wieder dort.

---

## 14. Updates

Das Tool prüft **beim Start** einmal, ob es eine neuere Version auf GitHub gibt.
Der Check läuft im Hintergrund und blockiert die App nicht.

Am Firmen-Proxy (Fortinet): der Update-Check nutzt automatisch die
Windows-Anmeldedaten für die Proxy-Auth.

Bei neuerer Version erscheint in der Statusleiste ein gelbes Feld **„Update auf
1.5.0 verfügbar"**. Klick öffnet einen Dialog:

- **Release-Seite öffnen** — führt zum GitHub-Release, dort ist das ZIP.
  Aktuell **musst du das ZIP von Hand herunterladen, entpacken und die alte
  Version ersetzen**.
- **Später** — Badge bleibt, beim nächsten Start wird wieder geprüft.
- **Diese Version überspringen** — der Badge kommt erst wieder, wenn eine
  **noch neuere** Version rauskommt.

---

## 15. Wo liegen meine Daten

| Was | Wo | Wer teilt sich das |
|---|---|---|
| App-Konfiguration (Update-Kanal, OS-Attribut-Keys, Default-Domain, …) | `\\Samba01\542$\5424_IT-Basis-Dienste\_Oste\CheckMK\bootstrap.json` | zentral, alle Nutzer |
| Domain-Zuordnung je Host | `\\Samba01\...\hosts.json` | zentral, alle Nutzer |
| Verbindung (Host/Site/User/Secret) | `%APPDATA%\Kroste\Checkmk\settings.json` | lokal, DPAPI-verschlüsselt |
| SSH-Logins (User+Passwort je Host) | `%APPDATA%\Kroste\Checkmk\ssh-creds.json` | lokal, DPAPI-verschlüsselt |
| Filter/Favoriten (pro Site) | `%APPDATA%\Kroste\Checkmk\filter.json` | lokal |
| Übersprungene Update-Version | `%APPDATA%\Kroste\Checkmk\updates.json` | lokal |
| UI-Zustand (Auto-Refresh, Baum/Tabelle, letzter Filter) | `%APPDATA%\Kroste\Checkmk\statusview.json` | lokal |
| Logs | `logs\` neben `Checkmk.App.exe` | lokal |

**Grundregel**: alles was mehreren Nutzern nutzt und **keine Secrets** enthält,
liegt zentral. User-spezifische Anmeldedaten, persönliche Favoriten und
UI-Präferenzen liegen lokal.

### Bootstrap-Datei — Overrides für Sonderfälle

Die zentrale `bootstrap.json` enthält Optionen, für die es bewusst kein UI gibt:

```json
{
  "SharedSettingsPath": "%APPDATA%\\Kroste\\Checkmk\\settings.json",
  "SharedHostsPath":    "\\\\Samba01\\...\\hosts.json",
  "HostDefaultDomain":  "lhp.intern",
  "HostOsAttributeKeys": [
    "tag_operation_system",
    "operation_system",
    "operating_system",
    "os_family"
  ],
  "UpdateChannelUrl":   "https://api.github.com/repos/Kroste/Checkmk/releases/latest",
  "ShowHostCreation":   false
}
```

- **`SharedSettingsPath`** — Pfad zur user-lokalen Verbindungsdatei.
- **`SharedHostsPath`** — Pfad zur zentralen Domain-Zuordnung.
- **`HostDefaultDomain`** — Fallback-Domain, wenn ein Host keinen expliziten
  Eintrag in `hosts.json` hat.
- **`HostOsAttributeKeys`** — Custom-Host-Attribute-Keys, unter denen das
  Cockpit die OS-Familie sucht. Erster Treffer gewinnt. Wenn euer Attribut
  anders heißt: hier ergänzen. Debug-Log zeigt die tatsächlich gesehenen Keys
  beim ersten Refresh.
- **`UpdateChannelUrl`** — anderer Update-Kanal (z. B. später ein interner
  Server statt GitHub).
- **`ShowHostCreation`** — auf `true` setzen, wenn das „Host anlegen"-Formular
  im Hosts-Tab sichtbar sein soll.

Beim ersten Start wird die zentrale Datei mit Default-Werten angelegt (Fallback
auf lokal, wenn Samba01 nicht schreibbar).

---

## 16. Wenn etwas nicht funktioniert

### „Nicht konfiguriert — bitte Verbindung in den Einstellungen setzen"

Deine lokale `settings.json` existiert nicht oder ist unvollständig. Einfach
Einstellungen öffnen, Verbindung eingeben, speichern.

### „Wrong credentials" (HTTP 401) beim Testen

Zwei häufige Ursachen:

- **Bei Windows/LDAP-Modus**: dein AD-Passwort ist abgelaufen (jährliche
  Rotation) — einfach neu tippen und speichern. Oder der User hat in Checkmk
  **REST API access** nicht angehakt.
- **Bei Automation-Modus**: du hast das GUI-Passwort statt des Automation-Secrets
  eingetragen. Das Automation-Secret ist ein langer Zufalls-String aus der
  User-Verwaltung.

### Regex-Filter: „Regex ungültig" beim Übernehmen

Der Filter-Manager validiert deinen Regex, bevor er ihn speichert. Bei Fehler
erscheint eine rote Meldung direkt unter dem Regex-Feld:

- **Klammern nicht geschlossen**: fehlende `)`, `]` oder `}`.
- **Ungültiger Escape**: `\p` oder `\q` sind keine gültigen Zeichenklassen.
- **Endlose Alternation**: `|` am Anfang oder Ende oder doppelt.

Siehe [Abschnitt 8](#8-regex-beispiele-für-filter) für Beispiele.

### Der Filter zeigt keine Hosts, obwohl welche passen sollten

- **Ist die richtige Site aktiv?** Filter sind pro Site — auf `Schul_IT` siehst
  du keine LHP-Filter.
- **Ist im Regex ein `^` vergessen?** `^db-` bindet an den Anfang; ohne `^`
  matcht `db-` auch mitten im Namen.
- **Sind Sonderzeichen escaped?** `srv.lhp.intern` matcht mehr als du denkst,
  weil `.` in Regex „irgendein Zeichen" heißt. Für den literalen Punkt: `srv\.lhp\.intern`.

### OS-Erkennung stimmt nicht (Windows als Linux erkannt oder umgekehrt)

Das Cockpit liest das OS aus dem Custom Host Attribute (Default: Suche in
`tag_operation_system` etc.). Prüfen:

1. Ist auf Folder-Ebene ein Custom Attribute gesetzt und wird es vererbt?
2. Wie heißt der interne Key? Im **Log** (Debug-Level) listet das Cockpit
   beim ersten Refresh alle gesehenen Attribute-Keys — dort taucht der echte
   Key auf.
3. Nicht der erwartete Key dabei? In `bootstrap.json` unter
   `HostOsAttributeKeys` den echten Key ergänzen.

Fallback ist der Parse der Check_MK-Agent-Ausgabe („OS: windows/linux") — der
greift nur, wenn kein Custom Attribute gesetzt ist.

### Site-Umschalter zeigt nur eine Site

Prüfe unter Einstellungen das Feld **„Bekannte Sites am selben Server"** —
Kommasepariert, mindestens zwei Sites. Nach dem Speichern zeigt der Dropdown
alle Einträge.

### „Ordner nicht gefunden" beim Host-Anlegen

Wahrscheinlich der Titel aus der Breadcrumb genommen statt des ID-Pfads. Im
Checkmk-Webinterface in die URL schauen — hinter `folder=` steht der ID-Pfad.

### Zertifikatsfehler beim Verbinden

Selbst-signiertes Zertifikat oder nicht im Windows-Zertifikatspeicher. **Lab**:
Haken „Zertifikatsfehler ignorieren (Lab)" setzen. **Produktion**: ein
korrektes Zertifikat installieren.

### Der Update-Badge kommt nie, obwohl es eine neue Version gibt

- Kein Internetzugang → GitHub API nicht erreichbar (im Log als Debug-Meldung).
- Proxy-Auth klappt nicht → einmal ab-/anmelden.
- Version explizit übersprungen → `%APPDATA%\Kroste\Checkmk\updates.json`
  löschen, dann erscheint der Badge wieder.

### Notifications erscheinen nicht (Windows)

- Fokusassistent (Ruhezeiten) aktiv? Dann werden Toasts unterdrückt.
- Startmenü-Eintrag „Checkmk Cockpit" fehlt (Windows-Requirement) — beim
  ersten Toast-Trigger ist etwas schiefgelaufen (Log prüfen).
- Toast im Action Center suchen — dort landen sie auch nach dem Popup.
- Sammel-Option unter `Win+I` → System → Benachrichtigungen prüfen.

### Client-Aktualisierung meldet „NativeCommandError" bei Register

`cmk-agent-ctl register` will interaktiv das Zertifikat bestätigen. **`--trust-cert`
fehlt in deiner gespeicherten Skript-Vorlage** — direkt hinter `register`
ergänzen.

### Die App fühlt sich falsch an — was tun

1. Ins Logfile schauen (`logs\` neben der Exe). Passwörter/Secrets sind
   maskiert — kannst du bedenkenlos anhängen.
2. Reproduzierbar? Issue auf GitHub mit Logauszug und Kontext.

---

## 17. Hilfe und Kontakt

- **Fachbereich:** 5424 IT-Basis-Dienste
- **GitHub-Repo:** <https://github.com/Kroste/Checkmk>
- **Bugs, Feature-Wünsche:** dort als Issue oder direkt an Lars.

---

## Was ist bewusst *nicht* enthalten

- **Kein Checkmk-Setup** — das Tool spricht mit einem vorhandenen Checkmk 2.5.
- **Kein Ersatz für das Webinterface** — deckt Alltagshandgriffe ab, nicht die
  selteneren Sachen (Rollen, Regeln, Notifications, Reports, Event-Console).
- **Kein automatisches Selbst-Update** — nur der Hinweis auf neue Versionen.
- **Kein SSO** — der Checkmk-Server hat keine Kerberos/SPNEGO-Konfig, deshalb
  meldest du dich einmal mit deinem AD-Passwort an (jährliche Rotation).
- **Kein DB-Health-Board als eigener Tab** — der Filter mit Regex oder
  Include-Liste deckt das ab (Favorit „DB-Server" anlegen).
