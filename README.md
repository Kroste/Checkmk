# Checkmk Cockpit — Benutzerhandbuch

Ein Windows-Desktop-Tool für den täglichen Umgang mit **Checkmk 2.5** (REST-API v1),
gebaut für den Fachbereich **5424 IT-Basis-Dienste**. Es holt die häufigen
Admin-Handgriffe, die das Checkmk-Webinterface tief in Menüs vergräbt, an die
Zeile, an der man das Problem sieht.

Wenn du das Tool nur benutzen willst, ist diese Datei alles, was du brauchst.
Technische Details (Architektur, API-Fallen, Roadmap) stehen in
[`CLAUDE.md`](CLAUDE.md).

---

## Inhalt

- [Installation](#installation)
- [Erststart und Verbindung](#erststart-und-verbindung)
- [Statusansicht — was läuft gerade](#statusansicht--was-läuft-gerade)
- [Ack und Downtime](#ack-und-downtime)
- [Bulk-Aktionen (mehrere Services gleichzeitig)](#bulk-aktionen-mehrere-services-gleichzeitig)
- [Host-Details](#host-details)
- [Host-Filter und Favoriten](#host-filter-und-favoriten)
- [Hosts-Tab — Host-Liste, Service Discovery, Änderungen aktivieren](#hosts-tab--host-liste-service-discovery-änderungen-aktivieren)
- [Updates](#updates)
- [Wo liegen meine Daten](#wo-liegen-meine-daten)
- [Wenn etwas nicht funktioniert](#wenn-etwas-nicht-funktioniert)
- [Hilfe und Kontakt](#hilfe-und-kontakt)

---

## Installation

**Windows (empfohlen):**

1. Neuestes ZIP von den [GitHub Releases](https://github.com/Kroste/Checkmk/releases)
   herunterladen (`Checkmk-x.y.z-win-x64.zip`).
2. In einen beliebigen Ordner entpacken — z. B. `C:\Tools\Checkmk\`.
3. `Checkmk.App.exe` starten.

Das ZIP ist **self-contained** — es ist kein .NET-Runtime auf dem Rechner nötig,
alles Nötige ist im Bundle. Etwa 90 MB.

**Zugriffsvoraussetzung Windows-Verbindungsdatei:** Das Tool liest die
Checkmk-Verbindungsdaten aus `\\Samba01\542$\Checkmk\settings.json`. Der Rechner
muss diesen Pfad lesen können (die üblichen Fileshare-Rechte des Fachbereichs).

**Linux:** Als AppImage oder tar.gz aus dem gleichen Release-Ordner. Linux ist
Zweitplattform; die Verbindung liegt dort lokal unter
`~/.config/Kroste/Checkmk/settings.json` (nicht auf dem Fileshare).

---

## Erststart und Verbindung

### Wenn die zentrale Verbindungsdatei schon vorhanden ist

Nichts weiter zu tun. Das Tool findet `\\Samba01\542$\Checkmk\settings.json`
automatisch, entschlüsselt es und verbindet sich. Du landest direkt im
Status-Tab.

### Wenn du die Verbindung neu einrichten musst (Admin-Fall)

1. Menüpunkt **„Einstellungen"** oben rechts.
2. Felder ausfüllen:

   | Feld | Was da rein muss |
   |---|---|
   | **Host** | Der DNS-Name deines Checkmk-Servers, z. B. `monitoring.lhp.intern`. |
   | **Site** | Der Site-Name — das URL-Segment hinter dem Host, meist `prod` oder `main`. |
   | **Automation-User** | Ein **Automation-User**, kein GUI-Benutzer. Muss in Checkmk unter *Setup → Users* mit passenden Rechten angelegt sein. Default: `automation`. |
   | **Automation-Secret** | Das Automation-Secret dieses Users (das lange Zufalls-String, nicht das GUI-Passwort). |
   | **HTTPS** | Fast immer ja. Nur ausschalten, wenn dein Server nur HTTP kann (Lab). |
   | **Zertifikatsfehler ignorieren (Lab)** | Nur setzen, wenn dein Server ein Selbstsigniertes hat. In Produktion: aus lassen. |

3. **„Testen"** klicken — das Tool ruft `/version` auf und meldet Edition und
   Version. Wenn das grün ist, klappt die Verbindung.
4. **„Speichern"** — Das Secret wird verschlüsselt in
   `\\Samba01\542$\Checkmk\settings.json` abgelegt. Alle anderen Nutzer im
   Fachbereich sehen die Verbindung ab dem Moment.

**Wichtig:** Seit Checkmk 2.4/2.5 wird kein `automation`-User mehr
automatisch angelegt. Du musst ihn im Checkmk-Webinterface einmal einrichten
und ihm mindestens die Rechte für die genutzten Endpunkte geben.

Unter dem Formular steht immer der aktuelle Speicherort — daran erkennst du,
ob du gerade die zentrale Datei bearbeitest („Zentrale Datei: \\Samba01\...")
oder eine lokale (Linux/erster Start ohne Fileshare-Zugriff).

---

## Statusansicht — was läuft gerade

Der **Status-Tab** ist die Startseite: alle überwachten Services und ihr
aktueller Zustand.

- **Ampelpunkte** links: 🟢 OK, 🟡 WARN, 🔴 CRIT, ⚫ UNKNOWN.
- **Freitext-Filter** oben — tippt sich live durch Hostnamen und Service-Namen
  (Contains, case-insensitive).
- **„Nur Probleme"** blendet alle OK-Zeilen aus. Standard: an.
- **„Auto"** + Sekundenrad rechts: das Tool aktualisiert sich alle N Sekunden
  selbstständig. Default 30 s, minimal 5 s.
- **„Aktualisieren"** holt einmal manuell nach.

Ganz unten in der Statusleiste stehen die Aggregat-Zähler (Hosts UP/DOWN,
Services OK/WARN/CRIT) und der Zeitpunkt der letzten Aktualisierung.

---

## Ack und Downtime

Zwei Szenarien, die im Checkmk-Webinterface immer viele Klicks brauchen.

### Acknowledge (Problem quittieren)

Man kennt das Problem, kümmert sich (oder es ist bekannt harmlos), und will die
Warnung stumm schalten:

1. Zeile im Status-Tab wählen (oder Rechtsklick).
2. Toolbar-Button **„Acknowledge…"** oder Menüpunkt.
3. Kommentar eingeben — der ist **Pflicht** (Checkmk-Vorgabe). Sinnvoll: Grund,
   Ticket-Nummer, Deadline.
4. **OK** — die Warnung ist quittiert, das Ampelsymbol bleibt aber (Farbe
   heißt „State", Ack ist ein Flag). In der „Ack"-Spalte steht dann ein Haken.

### Downtime (geplante Wartung)

Man weiß, dass ein Service demnächst rot wird (Reboot, Deploy, geplante
Netzwerkumbau), und will keine Alarme:

1. Zeile wählen, **„Downtime…"** klicken.
2. Kommentar eingeben (Pflicht).
3. **Dauer-Preset** wählen: 1 Stunde, 2 Stunden, 4 Stunden oder „bis morgen
   06:00" (praktisch für Overnight-Wartung).
4. **OK** — Downtime läuft ab **jetzt** bis zum berechneten Ende.

---

## Bulk-Aktionen (mehrere Services gleichzeitig)

Wenn zehn Services gleichzeitig warnen (Cluster-Failover, Reboot, Netzwerk-Blip),
ist es lästig, jeden einzeln zu acknowledgen. Deshalb:

1. **Ctrl-Klick** oder **Shift-Klick** in der Service-Tabelle markiert mehrere
   Zeilen.
2. **„Acknowledge…"** oder **„Downtime…"** öffnen den bekannten Dialog. Statt
   „host / service" steht im Ziel jetzt z. B. **„7 Services auf 3 Hosts"**.
3. Ein Kommentar gilt für alle. Bei Downtime gilt das gewählte Dauer-Preset für
   alle.
4. **OK** — das Tool arbeitet die Auswahl iterativ ab und zeigt den Fortschritt
   in der Statusleiste: **„Ack 3/12: DBSQL01 / CPU load"**.

Wenn einzelne Aktionen fehlschlagen, bricht der Bulk **nicht ab** — die Fehler
werden gesammelt und am Ende gemeldet: **„Acknowledged: 10/12 — 2 Fehler
(siehe Log)."** Details im NLog-Logfile.

---

## Host-Details

Wenn du einen Host komplett anschauen willst (alle seine Services + Konfig),
öffnet sich ein eigenes Fenster:

- **Doppelklick** auf eine Zeile (im Status-Tab oder im Hosts-Tab), oder
- **Rechtsklick → „Host-Details…"**.

Das Fenster zeigt oben:
- **Host-State-Ampel** (UP/DOWN/UNREACH)
- **Ordner-Pfad, IP-Adresse, Alias** aus der Config
- **Plugin-Ausgabe** des Host-Checks (was der Host selbst meldet)
- **Ack-Flag** des Hosts

Rechts daneben zwei Buttons:
- **„Host-Ack…"** — quittiert das **Host**-Problem (nur wenn Host DOWN/UNREACH ist)
- **„Host-Downtime…"** — setzt den **ganzen Host** in Wartung. Alle Services
  darunter sind dann auch stumm.

Darunter kommt die Service-Tabelle des Hosts mit Aggregat-Zählern
(OK/WARN/CRIT/UNK). **Bulk-Ack und Bulk-Downtime funktionieren hier
genauso** — Ctrl-Klick, „Ack…" oder „Downtime…".

Mehrere Detail-Fenster können parallel offen sein, z. B. für zwei DB-Server
gleichzeitig.

---

## Host-Filter und Favoriten

Wenn ihr über tausend Hosts habt, will keiner alle sehen. Deshalb gibt es
speicherbare Filter — nennt sich hier **„Favoriten"**.

### Filter auswählen

In der Toolbar (Status-Tab **und** Hosts-Tab) ist eine Combobox
**„Host-Filter:"**. Wählst du dort einen Favoriten, sind sofort in beiden Tabs
nur noch die passenden Hosts sichtbar. Zurück auf alle: Auswahl leeren
(„(Alle Hosts)").

### Favoriten aus einer Auswahl speichern

Im **Hosts-Tab** die passenden Hosts per Ctrl-/Shift-Klick markieren, dann
**„Auswahl als Favorit…"**. Namen eingeben („DB-Server", „Meine kritischen",
…) und speichern. Der Favorit steht dann in der Combobox beider Tabs.

### Favoriten verwalten

**„Filter verwalten…"** öffnet ein eigenes Fenster mit einer Liste aller
Favoriten. Rechts der Editor mit drei Feldern:

- **Name** — was in der Combobox erscheint.
- **Hostname-Regex** — .NET-Regex, case-insensitive. Beispiele:
  - `^db-` — alle Hosts, deren Name mit `db-` beginnt.
  - `sql|ora` — alle Hosts mit `sql` **oder** `ora` im Namen.
  - `.*` — alle.
- **Explizite Hostnamen** — eine feste Liste, ein Hostname pro Zeile. Wenn
  hier etwas steht, wird das **Regex ignoriert** — es zählen exakt diese
  Hostnamen.

Buttons:
- **„Übernehmen"** — Änderungen speichern (auf demselben ausgewählten Filter).
- **„Aktivieren"** — den gewählten Filter sofort aktiv setzen (analog zur
  Combobox in der Hauptleiste).
- **„Filter deaktivieren"** — kein Filter mehr aktiv, alle Hosts sichtbar.

Favoriten liegen **user-lokal** — jeder Kollege hat seine eigenen. Die Datei
ist `%APPDATA%\Kroste\Checkmk\filter.json`.

---

## Hosts-Tab — Host-Liste, Service Discovery, Änderungen aktivieren

Der **Hosts-Tab** ist für Änderungen an der Checkmk-Setup-Seite (also nicht
Live-Status, sondern was überhaupt überwacht wird).

### Host anlegen (standardmäßig ausgeblendet)

Das Anlege-Formular ist per Default **nicht sichtbar** — der Handgriff läuft
im Fachbereich zentral, Fehlbedienung produziert Config-Änderungen. Zum
Einblenden: `%APPDATA%\Kroste\Checkmk\bootstrap.json` öffnen und
`"showHostCreation": true` ergänzen. Kein UI-Schalter, absichtlich.

Wenn das Formular sichtbar ist:

- **Hostname** *(Pflicht)* — der Name, unter dem der Host in Checkmk laufen
  soll.
- **Ordner** — **muss ein ID-Pfad sein**, nicht der Titel aus dem Webinterface.
  Der Root-Ordner ist `/`. Ein DB-Ordner könnte `/datenbanken/db-mssql` heißen.
  Wenn du unsicher bist, schau im Checkmk-Webinterface in die URL — hinter
  `folder=` steht der ID-Pfad.
- **IP-Adresse** — optional, aber ohne läuft der Ping-Check nicht.
- **Alias** — optional, freier Anzeigename.

**„Anlegen"** legt den Host im Setup an. **Das reicht aber noch nicht, damit
er überwacht wird** — es fehlt noch die Service-Discovery (siehe unten).

### Änderungen aktivieren

Nach jeder Änderung im Setup (Host anlegen, Service Discovery, …) müssen die
Änderungen aktiviert werden — genau wie im Webinterface. Der Button
**„Änderungen aktivieren"** macht das mit einem Klick.

### Service Discovery — bestehende Hosts ins Monitoring bringen

Wenn ein Host in der Config existiert (z. B. weil er über eine
Bulk-CSV-Anlage kam), aber keine überwachten Services hat, brauchst du eine
Service-Discovery.

1. Zeile in der Host-Liste anklicken.
2. Toolbar-Button **„Services entdecken"** oder Rechtsklick →
   **„Services entdecken (fix_all + aktivieren)"**.
3. Das Tool startet einen Hintergrund-Task auf dem Server (`fix_all`),
   pollt bis fertig, aktiviert die Änderungen automatisch, und lädt die Liste
   neu. In der Statusleiste steht der Fortschritt: „Service-Discovery läuft
   für DBSQL01…" → „Discovery beendet…" → „Fertig — DBSQL01 ist im
   Monitoring."

Bei Hosts mit vielen Services kann das ein paar Sekunden dauern. Standard-Timeout
ist 2 Minuten.

---

## Updates

Das Tool prüft **beim Start** einmal, ob es eine neuere Version auf GitHub gibt.
Der Check läuft im Hintergrund und blockiert die App nicht — wenn kein Update
verfügbar oder GitHub nicht erreichbar ist, merkst du gar nichts.

Wenn eine neuere Version vorliegt, erscheint unten rechts in der Statusleiste
ein gelbes Feld **„Update auf 1.2.3 verfügbar"**. Klick öffnet einen Dialog:

- **Release-Seite öffnen** — führt zum GitHub-Release, dort ist das ZIP.
  Aktuell **musst du das ZIP von Hand herunterladen, entpacken und die alte
  Version ersetzen** — das Tool ersetzt sich noch nicht selbst
  (Selbst-Update kommt in einer späteren Version).
- **Später** — Dialog wird geschlossen, der Badge bleibt. Beim nächsten
  App-Start wird wieder geprüft.
- **Diese Version überspringen** — der Badge verschwindet und kommt erst
  wieder, wenn eine **noch neuere** Version rauskommt. Nützlich, wenn du ein
  Release absichtlich ignorieren willst.

---

## Wo liegen meine Daten

Alle Pfade auf einen Blick, damit du beim Support-Fall weißt, wo du hinschauen
musst.

### Windows

| Was | Wo | Zentral oder lokal |
|---|---|---|
| Verbindung (Host/Site/User/Secret) | `\\Samba01\542$\Checkmk\settings.json` | zentral, verschlüsselt |
| Update-Kanal-URL (überschreibbar) | `%APPDATA%\Kroste\Checkmk\bootstrap.json` | lokal |
| Übersprungene Update-Version | `%APPDATA%\Kroste\Checkmk\updates.json` | lokal |
| Filter/Favoriten | `%APPDATA%\Kroste\Checkmk\filter.json` | lokal |
| Logs | `logs\` neben `Checkmk.App.exe` | lokal |

### Linux

| Was | Wo |
|---|---|
| Verbindung | `~/.config/Kroste/Checkmk/settings.json` (verschlüsselt, user-lokal) |
| Filter/Favoriten | `~/.config/Kroste/Checkmk/filter.json` |
| Bootstrap/Updates | analog `~/.config/Kroste/Checkmk/` |

**Wenn du die Verbindungsdatei auf einen anderen Pfad legen willst** (z. B.
temporär auf ein anderes Share), editier `bootstrap.json`:

```json
{
  "sharedSettingsPath": "\\\\andersServer\\share\\Checkmk\\settings.json",
  "updateChannelUrl": "https://api.github.com/repos/Kroste/Checkmk/releases/latest"
}
```

Beim nächsten Start greift das. **Bewusst gibt es kein UI dafür** — es ist ein
Deployment-Notfallgriff, kein Alltagsschalter.

---

## Wenn etwas nicht funktioniert

### „Nicht konfiguriert — bitte Verbindung in den Einstellungen setzen"

Die zentrale `settings.json` existiert nicht oder ist leer. Prüfen:
- Kannst du `\\Samba01\542$\Checkmk\` im Explorer öffnen? Wenn nein →
  Fileshare-Zugriff mit dem Fachbereich klären.
- Existiert die Datei dort? Wenn nein → Admin muss die Verbindung einmal
  einrichten (siehe [Erststart](#erststart-und-verbindung)).

### „Fehler: Wrong credentials" (HTTP 401) beim Testen

- **Automation-Secret**, nicht das GUI-Passwort des Users verwendet? Das GUI-
  Passwort funktioniert **nicht** für die REST-API.
- **Automation-User** existiert überhaupt in Checkmk? Seit 2.4/2.5 muss er
  manuell angelegt werden.
- User hat mindestens die Rolle für die genutzten Endpunkte?

### „These fields have problems: attributes" beim Host-Anlegen

Sollte nicht mehr auftreten — das Tool sendet keine `null`-Werte im
`attributes`-Block. Wenn's doch passiert: Log anschauen und Ticket öffnen.

### „Ordner nicht gefunden" beim Host-Anlegen

Du hast wahrscheinlich den Titel aus der Breadcrumb genommen statt des
ID-Pfads. Prüfe im Checkmk-Webinterface die URL — hinter `folder=` steht der
ID-Pfad, den du hier eintragen musst.

### Zertifikatsfehler beim Verbinden

Dein Checkmk-Server nutzt ein selbst-signiertes Zertifikat oder das Zertifikat
ist nicht im Windows-Zertifikatspeicher. Für **Lab-Umgebungen**: In den
Einstellungen den Haken **„Zertifikatsfehler ignorieren (Lab)"** setzen. Für
**Produktion**: ein korrektes Zertifikat installieren und den Haken *nicht*
setzen.

### Der Update-Badge kommt nie, obwohl es eine neue Version gibt

- Rechner hat keinen Internetzugang → GitHub API nicht erreichbar. Update-Check
  wird still übersprungen.
- Du hast die Version explizit übersprungen — `%APPDATA%\Kroste\Checkmk\
  updates.json` löschen, dann erscheint der Badge wieder.

### Die App fühlt sich falsch an — was tun

1. Ins Logfile schauen (`logs\` neben der Exe). Dort steht meistens, was
   schiefgelaufen ist.
2. Wenn's ein reproduzierbarer Bug ist: Issue auf GitHub aufmachen (siehe
   unten), mit Logauszug (Passwörter und Secrets sind maskiert, kannst du
   sorglos anhängen).

---

## Hilfe und Kontakt

- **Fachbereich:** 5424 IT-Basis-Dienste
- **GitHub-Repo:** <https://github.com/Kroste/Checkmk>
- **Bugs, Feature-Wünsche:** dort als Issue oder direkt an Lars.

---

## Was ist bewusst *nicht* enthalten

Damit du nicht danach suchst:

- **Kein Checkmk-Setup** — das Tool spricht mit einem vorhandenen Checkmk 2.5,
  installiert oder konfiguriert aber nichts auf dem Server.
- **Kein Ersatz für das Webinterface** — es deckt die häufigen
  Alltagshandgriffe ab (Status, Ack, Downtime, Host-Details, Service
  Discovery), nicht die selteneren Sachen (Rollen, Regeln, Notifications,
  Reports). Für die bleibst du im Webinterface.
- **Kein automatisches Selbst-Update** — nur der Hinweis auf neue Versionen.
  Der Download-und-Ersetzen-Schritt ist manuell.
