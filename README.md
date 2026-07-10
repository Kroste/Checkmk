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
8. [Tray und Notifications](#8-tray-und-notifications)
9. [Hosts-Tab: Service Discovery und Änderungen aktivieren](#9-hosts-tab-service-discovery-und-änderungen-aktivieren)
10. [Client-Aktualisierung (Agent-Update)](#10-client-aktualisierung-agent-update)
11. [CSV-Export](#11-csv-export)
12. [Updates](#12-updates)
13. [Wo liegen meine Daten](#13-wo-liegen-meine-daten)
14. [Wenn etwas nicht funktioniert](#14-wenn-etwas-nicht-funktioniert)
15. [Hilfe und Kontakt](#15-hilfe-und-kontakt)

---

## 1. Installation

**Windows (empfohlen):**

1. Neuestes ZIP von den [GitHub Releases](https://github.com/Kroste/Checkmk/releases)
   herunterladen (`Checkmk-x.y.z-win-x64.zip`).
2. In einen beliebigen Ordner entpacken — z. B. `C:\Tools\Checkmk\`.
3. `Checkmk.App.exe` starten.

Das ZIP ist **self-contained** — es ist kein .NET-Runtime auf dem Rechner nötig,
alles Nötige ist im Bundle. Rechnen etwa 130 MB.

**Zugriffsvoraussetzung Windows-Verbindungsdatei:** Das Tool liest die
Checkmk-Verbindungsdaten aus `\\Samba01\542$\Checkmk\settings.json`. Der Rechner
muss diesen Pfad lesen können (die üblichen Fileshare-Rechte des Fachbereichs).

**Linux (tar.gz oder AppImage):** Aus dem gleichen Release-Ordner. Linux ist
Zweitplattform; die Verbindung liegt dort lokal unter
`~/.config/Kroste/Checkmk/settings.json` (nicht auf dem Fileshare).

**Hinter einem Proxy?** Der Update-Check nutzt automatisch die
Windows-Standard-Anmeldedaten für den Proxy (Negotiate/NTLM). Am FortiProxy des
Fachbereichs funktioniert das ohne Zusatzkonfiguration.

---

## 2. Ersteinrichtung

### Wenn die zentrale Verbindungsdatei schon vorhanden ist

Nichts weiter zu tun. Das Tool findet `\\Samba01\542$\Checkmk\settings.json`
automatisch, entschlüsselt es und verbindet sich. Du landest direkt im
Status-Tab.

### Wenn du die Verbindung neu einrichten musst (Admin-Fall)

1. Menüpunkt **„Einstellungen"** oben rechts.
2. Felder ausfüllen:

   | Feld | Was da rein muss |
   |---|---|
   | **Host** | Der DNS-Name des Checkmk-Servers, z. B. `monitoring.lhp.intern`. |
   | **Site** | Der Site-Name — das URL-Segment hinter dem Host, meist `prod` oder `main`. |
   | **Automation-User** | Ein **Automation-User**, kein GUI-Benutzer. Muss in Checkmk unter *Setup → Users* mit passenden Rechten angelegt sein. Default: `automation`. |
   | **Automation-Secret** | Das Automation-Secret dieses Users (der lange Zufalls-String, **nicht** das GUI-Passwort). |
   | **HTTPS** | Fast immer ja. Nur ausschalten, wenn dein Server nur HTTP kann (Lab). |
   | **Zertifikatsfehler ignorieren (Lab)** | Nur setzen, wenn dein Server ein Selbstsigniertes hat. In Produktion: aus lassen. |

3. **„Testen"** klicken — das Tool ruft `/version` auf und meldet Edition und
   Version. Wenn das grün ist, klappt die Verbindung.
4. **„Speichern"** — Das Secret wird verschlüsselt in
   `\\Samba01\542$\Checkmk\settings.json` abgelegt. Alle anderen Nutzer im
   Fachbereich sehen die Verbindung ab dem Moment.

**Wichtig:** Seit Checkmk 2.4/2.5 wird kein `automation`-User mehr automatisch
angelegt. Du musst ihn im Checkmk-Webinterface einmal einrichten und ihm
mindestens die Rechte für die genutzten Endpunkte geben.

Unter dem Formular steht immer der aktuelle Speicherort — daran erkennst du, ob
du gerade die zentrale Datei bearbeitest („Zentrale Datei: \\Samba01\\…") oder
eine lokale (Linux/erster Start ohne Fileshare-Zugriff).

---

## 3. Die Oberfläche

Ganz oben die eigene Titelleiste mit **„Einstellungen"** und **„Über"**, dann
drei Reiter:

- **Status** — Live-Status aller überwachten Services (Startseite).
- **Hosts** — Host-Liste im Setup, Service Discovery, Änderungen aktivieren.
- Ein **Einstellungen-Dialog** öffnet die Verbindungsdaten.

Am unteren Rand die blaue **Statusleiste** mit:

- links die aktuelle Rückmeldung („Aktualisiert 14:32:07 — 87 Services, 14 Hosts")
- Mitte, wenn zutreffend, ein gelber **Update-Badge** („Update auf 1.2.3
  verfügbar") — Klick öffnet den Release-Notes-Dialog.
- rechts die Verbindungsangabe (`https://monitoring.lhp.intern/prod (automation)`).

Sobald du das Fenster minimierst, verschwindet die App **ins System-Tray** (nicht
in die Taskleiste). Siehe [Tray und Notifications](#8-tray-und-notifications).

---

## 4. Die drei Alltags-Handgriffe

Ack, Downtime und Kommentar — drei Aktionen, die im Webinterface je 4–6 Klicks
kosten. Hier eine Zeile wählen und ein Menü öffnen.

### Acknowledge (Problem quittieren)

Du kennst das Problem, kümmerst dich (oder es ist bekannt harmlos), und willst
die Warnung stumm schalten:

1. Zeile im Status-Tab wählen (oder Rechtsklick).
2. Toolbar-Button **„Acknowledge…"** oder Menüpunkt.
3. Kommentar eingeben — **Pflicht** (Checkmk-Vorgabe). Sinnvoll: Grund,
   Ticket-Nummer, Deadline.
4. **OK** — die Warnung ist quittiert. In der „Ack"-Spalte steht ein Haken; die
   Ampelfarbe bleibt (State ≠ Ack).

### Downtime (geplante Wartung)

Du weißt, dass ein Service demnächst rot wird (Reboot, Deploy, geplante
Netzwerkumbau), und willst keine Alarme:

1. Zeile wählen, **„Downtime…"** klicken.
2. Kommentar eingeben (Pflicht).
3. **Dauer-Preset** wählen: 1 Stunde, 2 Stunden, 4 Stunden oder „bis morgen
   06:00" (praktisch für Overnight-Wartung).
4. **OK** — Downtime läuft ab **jetzt** bis zum berechneten Ende.

### Kommentar

Kontext an Host oder Service hinterlassen — „bin dran, gehört Team X, siehe
INC-4711":

- **Status-Tab:** Zeile wählen → **Rechtsklick → „Kommentar…"**.
- **Host-Detail-Fenster:** entweder **„Host-Kommentar…"** (in der Kopfleiste,
  legt einen Kommentar am Host an) oder **„Kommentar…"** in der Aktions-Toolbar
  (legt einen Kommentar am markierten Service an).

Kommentar-Text eingeben (Pflicht) und wählen, ob der Kommentar **persistent**
sein soll (überlebt einen Neustart des Monitorings). Bestehende Kommentare
werden im Host-Detail-Fenster unten als Liste angezeigt (neueste oben, mit
Autor + Zeitstempel).

**Hinweis:** Kommentare löschen ist noch nicht implementiert — die Checkmk-2.4/
2.5-REST-API hat konkurrierende Endpunkt-Varianten, das kommt nach einer
Live-Server-Verifikation nach.

### Bulk-Aktionen — mehrere Services gleichzeitig

Zehn Services gleichzeitig warnen (Cluster-Failover, Reboot, Netzwerk-Blip)? Du
willst nicht jeden einzeln bearbeiten:

1. **Ctrl-Klick** oder **Shift-Klick** in der Service-Tabelle markiert mehrere
   Zeilen.
2. **„Acknowledge…"** oder **„Downtime…"** öffnen den bekannten Dialog. Statt
   „host / service" steht im Ziel z. B. **„7 Services auf 3 Hosts"**.
3. Ein Kommentar gilt für alle. Bei Downtime gilt das gewählte Preset für alle.
4. **OK** — das Tool arbeitet die Auswahl iterativ ab, Fortschritt in der
   Statusleiste: **„Ack 3/12: DBSQL01 / CPU load"**.

Wenn einzelne Aktionen fehlschlagen, bricht der Bulk **nicht ab** — die Fehler
werden gesammelt und am Ende gemeldet: **„Acknowledged: 10/12 — 2 Fehler
(siehe Log)."** Details im NLog-Logfile neben der Exe.

---

## 5. Tabellen- und Baumansicht

Der Status-Tab kann die Services entweder als flache Tabelle oder als **Baum
(Hosts → Services)** zeigen. Umschalter oben in der Toolbar.

**Baum:**

- Jeder Host ist ein oberster Knoten mit **OS-Pictogramm** (Fenster für Windows,
  Tux für Linux, „?" bei unbekanntem OS), Ampel und **Problem-Zähler**.
- Aufgeklappt: die Services des Hosts mit Ausgabe.
- **Rechtsklick** funktioniert kontextabhängig — auf einem Host-Knoten stehen
  andere Aktionen zur Verfügung als auf einem Service-Knoten (u. a.
  Host-Details, Ack, Downtime, Kommentar, Client aktualisieren).

Der Baum ist besonders nützlich, wenn dich interessiert, wie sich Probleme über
Hosts verteilen — und für die schnelle OS-Erkennung.

---

## 6. Host-Details

Wenn du einen Host komplett anschauen willst (alle seine Services + Konfig),
öffnet sich ein eigenes Fenster:

- **Doppelklick** auf eine Zeile (im Status-Tab, im Hosts-Tab oder im Baum),
  oder
- **Rechtsklick → „Host-Details…"**.

Das Fenster zeigt oben:

- **Host-State-Ampel** (UP/DOWN/UNREACH)
- **In-Wartung- und Acknowledged-Badge** direkt neben der Ampel (falls
  zutreffend)
- **Ordner-Pfad, IP-Adresse, Alias** aus der Config. Fehlt in Checkmk eine IP,
  ermittelt das Tool sie per **Ping/DNS** und markiert die Herkunft.
- **Plugin-Ausgabe** des Host-Checks

Rechts daneben Buttons:

- **„Host-Ack…"** — quittiert das **Host**-Problem (nur wenn Host DOWN/UNREACH ist)
- **„Host-Downtime…"** — setzt den **ganzen Host** in Wartung. Alle Services
  darunter sind dann auch stumm.
- **„Host-Kommentar…"** — Kommentar am Host anlegen.

Darunter die Service-Tabelle des Hosts mit Aggregat-Zählern (OK/WARN/CRIT/UNK)
und den bekannten Ack/Downtime/Kommentar-Aktionen. **Bulk-Ack und
Bulk-Downtime funktionieren hier genauso** — Ctrl-Klick, „Ack…", „Downtime…".

Ganz unten die Liste **bestehender Kommentare** (Zeitstempel absteigend, Autor +
Ziel).

Mehrere Detail-Fenster können parallel offen sein, z. B. für zwei DB-Server
gleichzeitig.

---

## 7. Filter und Favoriten

Wenn ihr über tausend Hosts habt, will keiner alle sehen. Speicherbare Filter —
hier **„Favoriten"** — beschränken die Sicht auf das, was für dich relevant ist.

### Freitext-Filter (immer sichtbar)

Oben im Status-Tab: einfaches Suchfeld. Sucht case-insensitive über **Host,
Service, Ausgabe und Alias**. Ideal um schnell auf „CPU load" oder eine
Ticket-Nr. in der Plugin-Ausgabe zu filtern.

### Persistente Favoriten (Combobox)

In der Toolbar (Status-Tab **und** Hosts-Tab) gibt es die Combobox
**„Host-Filter:"**. Wählst du dort einen Favoriten, sind sofort in beiden Tabs
nur noch die passenden Hosts sichtbar. Zurück auf alle: Auswahl leeren
(„(Alle Hosts)").

### Favoriten aus einer Auswahl speichern (Hosts-Tab)

Im **Hosts-Tab** die passenden Hosts per Ctrl-/Shift-Klick markieren, dann
**„Auswahl als Favorit…"**. Namen eingeben („DB-Server", „Meine kritischen",
…) und speichern. Der Favorit steht dann in beiden Tabs zur Auswahl.

### Favoriten verwalten

**„Filter verwalten…"** öffnet ein eigenes Fenster mit einer Liste aller
Favoriten. Rechts der Editor mit drei Feldern:

- **Name** — was in der Combobox erscheint.
- **Hostname-Regex** — .NET-Regex, case-insensitive. Beispiele:
  - `^db-` — alle Hosts, deren Name mit `db-` beginnt.
  - `sql|ora` — alle Hosts mit `sql` **oder** `ora` im Namen.
  - `.*sql.*|.*ora.*` — die klassische „alle DB-Server"-Regel.
- **Explizite Hostnamen** — eine feste Liste, ein Hostname pro Zeile. Wenn hier
  etwas steht, wird das **Regex ignoriert** — es zählen exakt diese Hostnamen.

Buttons:

- **„Übernehmen"** — Änderungen speichern (auf dem ausgewählten Filter).
- **„Aktivieren"** — den gewählten Filter sofort aktiv setzen (analog zur
  Combobox in der Hauptleiste).
- **„Filter deaktivieren"** — kein Filter mehr aktiv, alle Hosts sichtbar.

Favoriten liegen **user-lokal** — jeder Kollege hat seine eigenen. Die Datei
ist `%APPDATA%\Kroste\Checkmk\filter.json`.

---

## 8. Tray und Notifications

Minimieren legt die App **ins System-Tray** (nicht in die Taskleiste). Das
Tray-Icon zeigt per Ampelfarbe den **schlechtesten Status im aktiven Filter**
(also z. B. nur die DB-Server, wenn du diesen Favoriten aktiv hast). Tooltip mit
Kurzfassung.

Im Tray läuft der **Auto-Refresh** weiter (automatisch aktiviert beim
Minimieren), und bei Statusänderungen bekommst du eine **Toast-Notification**:

- **Windows:** moderne Toast-Benachrichtigung, landet im **Action Center** und
  bleibt dort abrufbar. Beim ersten Mal legt das Tool automatisch einen
  Startmenu-Eintrag „Checkmk Cockpit" an — das ist ein Windows-Requirement für
  Toast-Notifications und passiert einmalig, ohne Nachfrage.
- **Linux:** über `notify-send` (KDE, GNOME).

Die Notifications sind **gebündelt** — wenn zehn Services gleichzeitig flippen,
kriegst du eine Sammelmeldung („3 neue Probleme, 2 Recoveries") statt zehn
einzelner Toasts. Und sie greifen **nur im aktiven Filter** — dein DB-Favorit
alarmiert dich nicht bei Web-Server-Ausfällen.

Zurück aus dem Tray: Klick auf das Tray-Icon oder Rechtsklick → **„Anzeigen"**.
Beenden über Rechtsklick → **„Beenden"**.

---

## 9. Hosts-Tab: Service Discovery und Änderungen aktivieren

Der **Hosts-Tab** ist die Sicht auf die Checkmk-Setup-Seite (nicht Live-Status,
sondern was überhaupt überwacht wird).

### Hosts-Liste

Zeigt Hostname, Ordner, IP und Alias jedes konfigurierten Hosts. Die aktuelle
Filter-Auswahl (siehe [Filter](#7-filter-und-favoriten)) greift auch hier.
Doppelklick öffnet das **Host-Detail-Fenster**.

### Änderungen aktivieren

Nach jeder Änderung im Setup (z. B. Service Discovery) müssen die Änderungen
aktiviert werden — genau wie im Webinterface. Der Button **„Änderungen
aktivieren"** macht das mit einem Klick.

### Service Discovery — bestehende Hosts ins Monitoring bringen

Wenn ein Host in der Config existiert (z. B. weil er über eine Bulk-CSV-Anlage
kam), aber keine überwachten Services hat, brauchst du eine Service-Discovery.

1. Zeile in der Host-Liste anklicken.
2. Toolbar-Button **„Services entdecken"** oder Rechtsklick →
   **„Services entdecken (fix_all + aktivieren)"**.
3. Das Tool startet einen Hintergrund-Task auf dem Server (`fix_all`), pollt bis
   fertig, aktiviert die Änderungen automatisch, und lädt die Liste neu.
   Fortschritt in der Statusleiste: „Service-Discovery läuft für DBSQL01…" →
   „Discovery beendet…" → „Fertig — DBSQL01 ist im Monitoring."

Bei Hosts mit vielen Services kann das ein paar Sekunden dauern. Standard-Timeout
ist 2 Minuten.

### Host anlegen (standardmäßig ausgeblendet)

Das Anlege-Formular ist per Default **nicht sichtbar** — der Handgriff läuft im
Fachbereich zentral, Fehlbedienung produziert Config-Änderungen. Zum Einblenden:
`%APPDATA%\Kroste\Checkmk\bootstrap.json` öffnen und `"showHostCreation": true`
ergänzen. Kein UI-Schalter, absichtlich.

Wenn das Formular sichtbar ist:

- **Hostname** *(Pflicht)* — der Name, unter dem der Host in Checkmk laufen soll.
- **Ordner** — **muss ein ID-Pfad sein**, nicht der Titel aus dem Webinterface.
  Der Root-Ordner ist `/`. Ein DB-Ordner könnte `/datenbanken/db-mssql` heißen.
  Unsicher? Im Checkmk-Webinterface in die URL schauen — hinter `folder=` steht
  der ID-Pfad.
- **IP-Adresse** — optional, aber ohne läuft der Ping-Check nicht.
- **Alias** — optional, freier Anzeigename.

**„Anlegen"** legt den Host im Setup an. **Das reicht aber noch nicht, damit er
überwacht wird** — es fehlt noch die Service-Discovery.

---

## 10. Client-Aktualisierung (Agent-Update)

Aus dem Kontextmenü einer Zeile (oder eines Baum-Knotens): **„Client
aktualisieren…"** startet die Aktualisierung des Checkmk-Agents auf dem
Zielhost. **Windows-only, WinRM am Zielhost muss aktiv sein** (in der DMZ
typischerweise geblockt).

Ablauf:

1. **Credentials-Dialog** — Admin-Credentials für die Remote-Session (dein
   Domänen-Admin oder ein passender Service-Account).
2. Der Installer wird per **`Copy-Item -ToSession`** auf den Host kopiert — das
   umgeht das Double-Hop-Problem.
3. Eine editierbare **Skript-Vorlage** läuft: Deinstall der alten Version →
   Install → `cmk-agent-ctl register`.
4. Fortschritt und Ausgabe im Fenster; Erfolg wird am Exit-Code festgemacht,
   nicht an stderr (native Tools wie `msiexec` oder `cmk-agent-ctl` schreiben
   Infos manchmal nach stderr).

### Agent-Share und Skript-Vorlage anpassen

In den Einstellungen findest du zwei Felder für dieses Feature:

- **Agent-Share** — Pfad zum Installer-Paket auf dem Fileshare.
- **Update-Skript-Vorlage** — der PowerShell-Code, der auf dem Zielhost läuft.
  Kannst du beliebig anpassen (z. B. andere Register-Argumente, Retention-Logik).

**Wichtige Details** (aus schmerzhaften Live-Tests gelernt):

- Der Register-Befehl **braucht `--trust-cert`**, sonst hängt sich
  `cmk-agent-ctl register` in einer interaktiven Zertifikats-Abfrage auf — die
  in der Remote-Session nicht beantwortbar ist. `--trust-cert` steht in der
  Default-Vorlage. Wer eine ältere Vorlage gespeichert hat, muss das **manuell
  ergänzen**.
- `Start-Process msiexec -Wait` meldet Nicht-Null-Exits **nicht automatisch**.
  Wer harte Fehlererkennung braucht: `-PassThru` + Exit-Code-Prüfung in die
  Vorlage einbauen.

### Sicherheit

Skript, Passwörter und temporäre Dateien werden **nicht** ins Logfile
geschrieben. Wenn du beim Debuggen tiefer schauen musst, findest du die Skript-
Ausgabe im Fenster selbst.

---

## 11. CSV-Export

Rechtsklick oder Toolbar → **„Als CSV exportieren…"**. Exportiert die **aktuell
gefilterte Ansicht** — also mit allen Filter-Einstellungen (Favorit, Freitext,
„Nur Probleme").

Format:

- **Semikolon-getrennt** (Excel öffnet das direkt korrekt in Deutschland)
- **UTF-8-BOM** (Umlaute stimmen)
- **RFC-4180-konformes Quoting** (Semikolons, Anführungszeichen und
  Zeilenumbrüche in Plugin-Outputs bleiben unversehrt)

Nützlich für Reporting, Übergaben oder Auswertungen in Excel.

---

## 12. Updates

Das Tool prüft **beim Start** einmal, ob es eine neuere Version auf GitHub gibt.
Der Check läuft im Hintergrund und blockiert die App nicht — wenn kein Update
verfügbar oder GitHub nicht erreichbar ist, merkst du gar nichts.

Am Firmen-Proxy (Fortinet): der Update-Check nutzt automatisch die
Windows-Anmeldedaten für die Proxy-Auth — funktioniert ohne Zusatzkonfiguration.

Bei neuerer Version erscheint in der Statusleiste ein gelbes Feld **„Update auf
1.2.3 verfügbar"**. Klick öffnet einen Dialog:

- **Release-Seite öffnen** — führt zum GitHub-Release, dort ist das ZIP.
  Aktuell **musst du das ZIP von Hand herunterladen, entpacken und die alte
  Version ersetzen** — das Tool ersetzt sich noch nicht selbst.
- **Später** — Dialog geschlossen, Badge bleibt. Beim nächsten App-Start wird
  wieder geprüft.
- **Diese Version überspringen** — der Badge verschwindet und kommt erst
  wieder, wenn eine **noch neuere** Version rauskommt.

---

## 13. Wo liegen meine Daten

Alle Pfade auf einen Blick, damit du beim Support-Fall weißt, wo du hinschauen
musst.

### Windows

| Was | Wo | Zentral oder lokal |
|---|---|---|
| Verbindung (Host/Site/User/Secret) | `\\Samba01\542$\Checkmk\settings.json` | zentral, verschlüsselt |
| Update-Kanal-URL, Fileshare-Pfad, `showHostCreation` | `%APPDATA%\Kroste\Checkmk\bootstrap.json` | lokal |
| Übersprungene Update-Version | `%APPDATA%\Kroste\Checkmk\updates.json` | lokal |
| Filter/Favoriten | `%APPDATA%\Kroste\Checkmk\filter.json` | lokal |
| Logs | `logs\` neben `Checkmk.App.exe` | lokal |

### Linux

| Was | Wo |
|---|---|
| Verbindung | `~/.config/Kroste/Checkmk/settings.json` (verschlüsselt, user-lokal) |
| Filter/Favoriten | `~/.config/Kroste/Checkmk/filter.json` |
| Bootstrap/Updates | analog `~/.config/Kroste/Checkmk/` |

### Bootstrap-Datei — Overrides für Sonderfälle

`%APPDATA%\Kroste\Checkmk\bootstrap.json` (Windows) enthält Optionen, für die es
bewusst **kein UI** gibt:

```json
{
  "sharedSettingsPath": "\\\\Samba01\\542$\\Checkmk\\settings.json",
  "updateChannelUrl": "https://api.github.com/repos/Kroste/Checkmk/releases/latest",
  "showHostCreation": false
}
```

- **`sharedSettingsPath`** — anderer Fileshare-Pfad für die Verbindungsdatei.
- **`updateChannelUrl`** — anderer Update-Kanal (z. B. später ein interner
  Server statt GitHub).
- **`showHostCreation`** — auf `true` setzen, wenn das „Host anlegen"-Formular
  im Hosts-Tab sichtbar sein soll.

Beim ersten Start wird die Datei mit Default-Werten angelegt und ist dann per
Editor anpassbar.

---

## 14. Wenn etwas nicht funktioniert

### „Nicht konfiguriert — bitte Verbindung in den Einstellungen setzen"

Die zentrale `settings.json` existiert nicht oder ist leer:

- Kannst du `\\Samba01\542$\Checkmk\` im Explorer öffnen? Wenn nein →
  Fileshare-Zugriff mit dem Fachbereich klären.
- Existiert die Datei dort? Wenn nein → Admin muss die Verbindung einmal
  einrichten (siehe [Ersteinrichtung](#2-ersteinrichtung)).

### „Fehler: Wrong credentials" (HTTP 401) beim Testen

- **Automation-Secret**, nicht das GUI-Passwort des Users verwendet? Das
  GUI-Passwort funktioniert **nicht** für die REST-API.
- **Automation-User** existiert überhaupt in Checkmk? Seit 2.4/2.5 muss er
  manuell angelegt werden.
- User hat mindestens die Rolle für die genutzten Endpunkte?

### „Ordner nicht gefunden" beim Host-Anlegen

Wahrscheinlich der Titel aus der Breadcrumb genommen statt des ID-Pfads. Prüfe
im Checkmk-Webinterface die URL — hinter `folder=` steht der ID-Pfad, den du
hier eintragen musst.

### Zertifikatsfehler beim Verbinden

Dein Checkmk-Server nutzt ein selbst-signiertes Zertifikat oder es ist nicht im
Windows-Zertifikatspeicher. Für **Lab-Umgebungen**: In den Einstellungen den
Haken **„Zertifikatsfehler ignorieren (Lab)"** setzen. Für **Produktion**: ein
korrektes Zertifikat installieren.

### Der Update-Badge kommt nie, obwohl es eine neue Version gibt

- Rechner hat keinen Internetzugang → GitHub API nicht erreichbar (im Log als
  Debug-Nachricht).
- Proxy-Auth klappt nicht → das Tool nutzt die Windows-Anmeldedaten; bei
  Kerberos-Problemen einmal Ab-/Anmelden.
- Du hast die Version explizit übersprungen → `%APPDATA%\Kroste\Checkmk\
  updates.json` löschen, dann erscheint der Badge wieder.

### Notifications erscheinen nicht (Windows)

- Fokusassistent (Ruhezeiten) im Windows aktiv? Dann werden Toasts unterdrückt.
- Das Cockpit sollte einen Eintrag „Checkmk Cockpit" im Startmenü haben — der
  wird beim ersten Toast automatisch angelegt und ist ein Windows-Requirement.
  Fehlt er, ist beim ersten Toast-Trigger etwas schiefgelaufen (Log prüfen).
- Toast im Action Center suchen — dort landen sie, auch wenn das Popup zu
  schnell verschwunden ist.

### Client-Aktualisierung meldet „NativeCommandError" bei Register

`cmk-agent-ctl register` will interaktiv das Zertifikat bestätigen. In der
Remote-Session ist das nicht beantwortbar. **`--trust-cert` fehlt in deiner
gespeicherten Skript-Vorlage** — direkt hinter `register` ergänzen.

### Client-Aktualisierung meldet „Abgeschlossen" bei leerer Ausgabe

Alte Version des Cockpits — der Fix ist ab v1.2.1 drin (Skript läuft jetzt
über eine temporäre `.ps1` mit `-File`, nicht mehr über `-Command -` via STDIN).
Update ziehen.

### Die App fühlt sich falsch an — was tun

1. Ins Logfile schauen (`logs\` neben der Exe). Dort steht meistens, was
   schiefgelaufen ist. Passwörter/Secrets sind maskiert — kannst du bedenkenlos
   anhängen.
2. Wenn's reproduzierbar ist: Issue auf GitHub (siehe unten) mit Logauszug und
   ein paar Sätzen zum Kontext.

---

## 15. Hilfe und Kontakt

- **Fachbereich:** 5424 IT-Basis-Dienste
- **GitHub-Repo:** <https://github.com/Kroste/Checkmk>
- **Bugs, Feature-Wünsche:** dort als Issue oder direkt an Lars.

---

## Was ist bewusst *nicht* enthalten

Damit du nicht danach suchst:

- **Kein Checkmk-Setup** — das Tool spricht mit einem vorhandenen Checkmk 2.5,
  installiert oder konfiguriert aber nichts auf dem Server.
- **Kein Ersatz für das Webinterface** — es deckt die häufigen Alltagshandgriffe
  ab (Status, Ack, Downtime, Kommentare, Host-Details, Service Discovery,
  Client-Aktualisierung), nicht die selteneren Sachen (Rollen, Regeln,
  Notifications, Reports, Ereignisverwaltung).
- **Kein automatisches Selbst-Update** — nur der Hinweis auf neue Versionen.
  Download-und-Ersetzen ist noch manuell.
- **Kein Kommentare-Löschen** — kommt, sobald am Live-Server verifiziert ist,
  welche der 2.4/2.5-API-Varianten passt.
- **Kein DB-Health-Board als eigener Tab** — der Filter mit Include-Liste bzw.
  Regex deckt das ab: Favorit „DB-Server" anlegen (`.*sql.*|.*ora.*` oder eine
  Include-Liste deiner Instanzen), fertig.
