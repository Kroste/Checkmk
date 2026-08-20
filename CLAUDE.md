# Checkmk Cockpit

Avalonia-12-Desktop-Tool, das die **täglichen Checkmk-Admin-Handgriffe entwirrt** — die
Aktionen, die das Webinterface tief in Menüs vergräbt, liegen hier flach an der Zeile, wo
man das Problem sieht. Ziel-Backend: **Checkmk 2.5.x Pro** über die **REST-API v1**.

**Bewusst Windows-only** (dokumentierte Ausnahme zur Cross-Platform-Regel des
kroste-avalonia-Skills): App-Target `net10.0-windows`, `WinExe`, nur `win-x64`,
**kein Linux-Build/AppImage**. Grund sind tragende, Windows-gebundene Features —
DPAPI-Secret-Storage, WinRM/PowerShell-basierte Client-Aktualisierung und der
Tray-Balloon per `Shell_NotifyIcon`-P/Invoke. Diese Entscheidung ist final und
soll nicht „nach Cross-Platform repariert" werden.

> Diese Datei wird von Copilot/Claude in VS Code als always-on-Kontext gelesen. Regeln sind
> bewusst kurz, begründet und mit Beispielen — nicht wiederholen, was Linter/`.editorconfig`
> ohnehin erzwingen.

---

## 1 · Build, Test, Run (immer zuerst)

```bash
dotnet build Checkmk.slnx -c Release          # muss 0 Warnings / 0 Errors sein
dotnet test  Checkmk.slnx                      # xunit.v3 + FluentAssertions v7
# Self-contained Single-File (bevorzugte Distribution, kein System-.NET nötig):
dotnet publish Checkmk.App/Checkmk.App.csproj -c Release -r win-x64 \
  --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

`TreatWarningsAsErrors=true` ist gesetzt — **jede** Warnung bricht den Build. Vor jedem Commit
muss `dotnet build -c Release` sauber durchlaufen.

## 2 · Entwicklung in VS Code

- **Extensions:** C# Dev Kit (`ms-dotnettools.csdevkit`) + `ms-dotnettools.csharp`. Avalonia:
  „Avalonia for VS Code" für XAML-Preview/IntelliSense.
- **Debuggen:** F5 nutzt `.vscode/launch.json` (Config „Checkmk.App (Debug)"), `preLaunchTask`
  ist `build`.
- **Tasks** (`.vscode/tasks.json`): `build`, `test`, `publish-win-x64`, `clean-hard`
  (löscht rekursiv alle `bin/`/`obj/`).
- **Bazzite:** `dotnet`/`code` laufen in der Distrobox `dotnet10` (Fedora, RPM-installiert,
  via `distrobox-export --app code`), `$HOME` ist zwischen Host und Container geteilt.

## 3 · Architektur

| Projekt | Zweck |
|---|---|
| `Checkmk.Core` | REST-API-Client (`CheckmkClient`), Modelle, Optionen. **UI-unabhängig**, keine Avalonia-Abhängigkeit. |
| `Checkmk.Data` | EF Core 10 auf die zentrale MSSQL-Datenbank `CheckMK_Copilot` (FOC-SQL01): globale Vorgaben, Host-Metadaten, Bereiche/Teams. UI-unabhängig; EF gehört **nicht** in `Checkmk.Core`, der bleibt reiner REST-Client. |
| `Checkmk.App` | Avalonia-UI: Tabs, Dialoge, DI-Bootstrap. |
| `Checkmk.Core.Tests` | xunit.v3 + FluentAssertions **v7** (v8 = kommerzielle Xceed-Lizenz, siehe §6). |

**Muster:** MVVM mit CommunityToolkit.Mvvm (Source Generators, `[ObservableProperty]`,
`[RelayCommand]`); manuelles DI via `ServiceCollection` in `Program.cs`; NLog (Secrets maskiert).
`CheckmkClient` ist bewusst frei von UI/DI, damit er wiederverwendbar bleibt.

**Laufzeit-Client:** Verbindung ist zur Laufzeit änderbar → `ICheckmkClientProvider` baut den
`CheckmkClient` aus den aktuellen Settings neu (statt statischem `IOptions`). Nach dem Speichern
der Settings `Configure(...)` aufrufen, nicht die App neu starten.

**Fenster:** alle Fenster erben von `Controls/ChromeWindow` (randlos,
`WindowDecorations.BorderOnly` + `ExtendClientAreaToDecorationsHint=true` +
`ExtendClientAreaTitleBarHeightHint=-1` + `CanResize=true` — alle vier Zeilen
Pflicht, sonst schluckt die OS-Caption-Zone Klicks/Drag). Die Titelleiste ist
das UserControl `Controls/TitleBar` — ein Fenster packt schlicht
`<controls:TitleBar Title="..." />` an den oberen Rand, keine inline
`Border`+`PointerPressed`-Konstruktion mehr. Die TitleBar setzt intern die
Avalonia-12-`chrome:WindowDecorationProperties.ElementRole`-Rollen
(`TitleBar` = nativer Drag/Doppelklick via HTCAPTION, `User` an Fensterbuttons
und Extras — Klicks laufen als HTCLIENT direkt zu den Controls). Für Extras in
der Titelleiste (z. B. Site-Umschalter im MainWindow) gibt es die
`TitleBar.Extras`-Property (ContentProperty), Kinder darin erben automatisch
die `User`-Rolle. **Zusaetzlich Pflicht:** der managed Drag-Fallback in
`TitleBar.OnBarPointerPressed` filtert per Visual-Tree-Walk
(`LandedOnInteractiveChild`) alle Klicks aus, die aus einem interaktiven Kind
gebubbelt kommen. Ohne diesen Guard startet `BeginMoveDrag` bei jedem Klick auf
die Site-ComboBox einen Fenster-Drag, der Pointer geht ans OS und das Dropdown
oeffnet nie (nur der ToolTip erscheint). Buttons sind davon nicht betroffen, weil
sie den Press selbst als handled markieren — die ComboBox tut das nicht. Der Guard
existierte schon einmal in `ChromeWindow` (be95724) und ging beim TitleBar-Refactor
(23160d8) verloren; **nicht wieder als „durch ElementRole ueberfluessig" entfernen**,
solange der Fallback-Handler dranhaengt. Palette/Buttons: `Kroste*Brush` + `Button.chrome` in
`App.axaml`. **App-Icon:** `Assets/app.ico` (`<ApplicationIcon>`, EXE) +
`Assets/app.png` (`ChromeWindow.Icon`, Fenster/Taskleiste; die TitleBar zeigt
es zusätzlich klein oben links). Dialoge mit Laufzeitdaten (z. B.
`ServiceActionDialog`) werden direkt instanziiert, nicht über DI. Referenz
für das gesamte Muster: kroste-avalonia-Skill (Klemmbrett-Scaffold).
**Version:** Anzeige immer über `AppVersion.Display` (MinVer-`InformationalVersion`, ohne
`+`-Suffix) — `Assembly.GetName().Version` liefert bei MinVer nur `Major.0.0.0`.

## 4 · Aktueller Funktionsstand

- **Status-Tab:** Host-/Service-Livestatus (Polling, Auto-Refresh), Ampel-Punkte,
  Freitext-Filter (Host/Service/**Ausgabe**/**Alias**), „Nur Probleme". **Ack + Downtime
  direkt aus der Liste** (Toolbar-Button + Rechtsklick): Zeile wählen → Dialog mit
  Pflicht-Kommentar; Downtime mit Dauer-Presets. **Bulk-Ack/Downtime**: Ctrl/Shift-Klick
  markiert mehrere Services; ein Kommentar für alle, iterative Ausführung mit Fortschritt
  „Ack 3/12: host/service" in der Statusleiste. Einzelfehler brechen den Bulk nicht ab,
  werden geloggt und am Ende summiert. Spalte **Age** (Zeit seit letzter Statusänderung)
  statt „Letzter Check". **CSV-Export** der gefilterten Ansicht via `CsvExporter`
  (Semikolon, UTF-8-BOM, RFC-4180-Quoting).
- **Refresh läuft im Hintergrund** (seit v1.8.0). Bei ungefiltertem Blick auf ~32.000
  Checks stand die App vorher mehrere Sekunden. Drei Ursachen, alle beseitigt — und
  alle drei sind leicht wieder einzubauen:
  1. **`CheckmkClient.GetAsync` streamt.** Vorher `ReadAsStringAsync` + synchrones
     `JsonSerializer.Deserialize` — der Parse lief nach dem `await` wieder auf dem
     UI-Thread. Jetzt `HttpCompletionOption.ResponseHeadersRead` +
     `DeserializeAsync` + durchgängiges `ConfigureAwait(false)`. Nicht auf den
     String-Weg zurückbauen: der puffert zweistellige Megabytes und blockiert.
     Preis: bei kaputter Antwort gibt es keinen vollen Body mehr für die
     Fehlermeldung — `CountingStream` schneidet dafür die ersten 2 KB mit
     (reicht, um Proxy-HTML statt JSON zu erkennen).
  2. **`BulkObservableCollection.ReplaceAll` statt `Clear()` + N × `Add()`.**
     32.000 Zeilen einzeln einzufügen sind 32.000 `CollectionChanged`-Zustellungen
     ans DataGrid; ein Reset kostet dank Zeilen-Virtualisierung fast nichts. Der
     Reset räumt allerdings die Grid-Selektion ab — `ApplyVisible` zieht sie über
     `ServiceKey` (Host + Description) nach, sonst verliert ein Auto-Refresh alle
     30 s die markierte Zeile.
  3. **Der Baum wird nur gebaut, wenn er sichtbar ist** (`BuildTreeIfVisible`,
     `_treeStale`). In der Tabellenansicht war das ein ViewModel je Host für nichts.
  Fortschritt: `CountingStream` meldet gedrosselt (alle 256 KB) Bytes, `RefreshSegment`
  bildet sie auf ein Segment des Balkens ab (Hosts 0–10 %, Services 10–80 %,
  Auswerten/Anzeigen bis 100 %). **Checkmk schickt die großen Livestatus-Antworten
  chunked, also ohne `Content-Length`** — Nenner ist deshalb die Antwortgröße des
  letzten Laufs aus `statusview.json` (`LastHostBytes`/`LastServiceBytes`); ohne
  Schätzer läuft der Balken indeterminate. Restzeit wird linear hochgerechnet.
  Ein neuer Refresh **bricht den laufenden ab** (`_refreshCts`), der Timer-Tick
  dagegen **verwirft sich selbst**, solange `IsBusy` — sonst käme bei 32.000 Checks
  und kurzem Intervall nie einer durch. `_refreshRun` verwirft verspätete
  Fortschrittsmeldungen abgebrochener Läufe, sonst verstellen sie den Balken des
  Nachfolgers.
- **Spaltenkonfiguration (Status-Tab):** Der Spaltensatz der Service-Tabelle steht
  **nicht mehr im XAML**, sondern entsteht immer über `StatusColumnFactory` — einmal
  aus `columns.json` (Normalmodus, `StatusGridColumns.Merge/Apply/Capture`) und einmal
  aus `viewer.json` (Viewer-Modus, dort gesperrt). Zwei Quellen für denselben
  Spaltensatz wären zwangsläufig irgendwann uneinig; nicht wieder ins XAML zurückbauen.
  Bedienung: Rechtsklick auf die Kopfzeile → Checkbox-Liste, Drag am Kopf sortiert um.
  Drei Fallen, die schon zugeschnappt sind:
  1. **Breiten aus `Column.Width`, nicht `ActualWidth`.** Spalten, die rechts aus dem
     sichtbaren Bereich ragen, sind nicht gemessen und liefern Unsinn (20 px für eine
     110-px-Spalte) — gespeichert schrumpft die Tabelle bei jedem Start weiter.
     Stern-Breiten (Ausgabe-Spalte) werden als `null` gesichert, sonst frieren sie fest.
  2. **`ContextRequested` per Visual-Tree-Walk trennen** (`IsInsideColumnHeader`):
     Kopfzeile und Zellen liefern dasselbe Event am selben DataGrid, sonst bekommt man
     auf dem Header das Zeilen-Menü.
  3. **Neue Katalog-Spalten kommen ausgeblendet dazu** (`Merge`) — ein Update darf
     niemandem die gewohnte Ansicht umbauen. `DefaultLayout` ist exakt der alte
     XAML-Satz.
- **Baumansicht** (Umschalter Tabelle ⇄ Baum, im Status-Tab): Hosts als oberste Knoten mit
  **OS-Pictogramm** (`Assets/os/windows.png` bzw. Tux-Vektor, „?" bei unbekanntem OS),
  Ampelpunkt, Problem-Zähler; aufgeklappt die Services mit Ausgabe. OS-Familie wird aus
  der Check_MK-Agent-Ausgabe geparst (`OsDetection`) — kein Zusatzdienst nötig. Nur die
  **Familie** (Windows/Linux), die exakte Version bräuchte die HW/SW-Inventur
  (`os_version`). Kontextmenü im Baum ist knotenabhängig (Host vs. Service): Host-Details,
  Ack, Downtime, Kommentar, Client aktualisieren.
- **Tray & Notifications:** Minimieren legt die App ins **System-Tray** (nicht Taskleiste)
  und schaltet Auto-Refresh ein (`TrayController`). Tray-Icon zeigt per Ampelfarbe den
  schlechtesten Status im aktiven Filter, Tooltip mit Kurzfassung. `StatusChangeMonitor`
  vergleicht Snapshots, `IToastNotifier` meldet Änderungen und Recovery **gebündelt** —
  nur im aktiven Filter, keine Alarm-Sturm-Kaskade.
  WinRT-Toast über `Microsoft.Toolkit.Uwp.Notifications` (`ToastContentBuilder.Show`) —
  Action-Center-kompatibel. `ToastNotificationManagerCompat` registriert AumID +
  Startmenu-Shortcut + COM-Server; ein leerer `OnActivated`-Handler im
  `WindowsToastNotifier`-Ctor erzwingt die Registrierung sofort, statt sie lazy
  beim ersten `Show()`-Call laufen zu lassen. Nach jedem `Show()` wird
  `Notifier.Setting` geloggt — Windows sagt uns direkt, ob es blockt
  (Focus Assist, DisabledForApplication, GroupPolicy).
- **Hosts-Tab** (früher „Konfiguration"): Host-Liste mit Ordner/IP/Alias, „Änderungen aktivieren",
  **Service Discovery** (Toolbar-Button + Rechtsklick auf einer Zeile): startet
  `fix_all` als Hintergrund-Task auf dem Server, pollt bis `active=false`, aktiviert
  danach die Änderungen — bringt vorhandene Hosts wie `DBSQL01` ins Monitoring.
  Das „Host anlegen"-Formular ist per Default **ausgeblendet** (Setup-Handgriffe
  laufen zentral, Fehlbedienung produziert Config-Änderungen); wieder einblenden
  über `%APPDATA%\Kroste\Checkmk\bootstrap.json` mit `"showHostCreation": true`.
- **Host-Details** (`HostDetailWindow`): Doppelklick oder Rechtsklick auf eine Zeile
  öffnet ein eigenes Fenster mit Host-State (Ampel + **In-Wartung-** und
  **Acknowledged-Badge**), Config-Attributen (Ordner/IP/Alias), Plugin-Output,
  Service-Aggregat (OK/WARN/CRIT/UNK) und der Service-Tabelle. Ack + Downtime direkt
  auf einzelnen Services **und** auf dem kompletten Host („ganzer Host in Wartung" ist
  damit erledigt). Mehrere Detail-Fenster können parallel offen sein. **IP-Fallback**:
  wenn Checkmk keine IP liefert, ermittelt `IpResolver` sie via Ping/DNS und markiert
  die Herkunft im UI.
- **Kommentare**: bestehende Kommentare (Host + Service) werden im Host-Detail-Fenster
  unten aufgelistet (Zeitstempel absteigend). Neue Kommentare per „Host-Kommentar…" bzw.
  „Kommentar…" auf dem markierten Service; Status-Tab hat Rechtsklick → „Kommentar…".
  Persistent-Flag im Dialog wählbar. Delete-Endpoint noch nicht implementiert (2.4/2.5-API
  hat konkurrierende Varianten — nachziehen sobald an Live-Server verifiziert).
- **Client-Aktualisierung** ist seit v1.7.0 **ausgelagert** ins Plugin
  [`Checkmk-Plugin-AgentUpdater`](https://github.com/Kroste/Checkmk-Plugin-AgentUpdater).
  Wer die Funktion braucht, legt die Plugin-DLL in den `plugins/`-Ordner neben
  `Checkmk.App.exe`. Grund für das Auslagern: die Aktion braucht Admin-Credentials
  und ist nicht für jeden Cockpit-Nutzer gedacht. Das Plugin exportiert einen
  `IAgentUpdater`-Service (aus `Checkmk.PluginContracts.Services`), den andere
  Plugins konsumieren können (Plan: vSphere-Baseimage-Plugin für Batch-Updates).
- **Externe Plugin-Repos als Submodules**: unter `external-plugins/` liegen die
  Plugin-Repos als Git-Submodules. Nach `git submodule update --init --recursive`
  greift das `build/external-plugins.targets`-Target beim Cockpit-Debug-Build:
  jedes Plugin wird mitgebaut und die `CheckmkPlugin.*.dll` ins
  `Checkmk.App/bin/Debug/…/plugins/` kopiert — F5-Start hat die Plugins direkt
  drin. **CI/Release checken die Submodules bewusst NICHT aus** (`actions/checkout`
  ohne `submodules: true`), damit End-User-ZIPs plugin-frei bleiben — Plugins
  müssen aktiv installiert werden.
- **Autoupdater (Phase 1):** Beim Start fragt `GitHubReleasesUpdateChecker` den
  `Bootstrap.UpdateChannelUrl` ab (Default `api.github.com/repos/Kroste/Checkmk/releases/
  latest`), vergleicht mit `Assembly.Version` und meldet bei neuerer Version einen
  gelben Badge in der Statusleiste. Klick öffnet den `UpdateDialog` (Release-Notes +
  „Release-Seite öffnen"/„Später"/„Diese Version überspringen"). Skip-Version liegt in
  `%APPDATA%\Kroste\Checkmk\updates.json`.
  Kein Selbst-Ersetzen des Binary — Roadmap-Phase 2.
  **Manuell (About-Box):** Button „Nach Updates suchen" ruft `CheckManuallyAsync`
  auf — ignoriert bewusst die übersprungene Version und gibt klares Feedback
  (aktuell / verfügbar → `UpdateDialog` / fehlgeschlagen). Gemeinsame Kernlogik mit
  dem Startup-Check über das private `EvaluateAsync(honorSkip)`.
  **Proxy-Fix (v1.2.1):** `HttpClient` nutzt `DefaultProxyCredentials`
  (Negotiate/NTLM über den angemeldeten Windows-User) — sonst 407 am FortiProxy.
- **Host-Filter (beide Tabs):** Persistente Favoriten wählbar über eine ComboBox in der Tool-
  bar. Ein Favorit ist entweder ein **Hostname-Regex** (case-insensitive) oder eine explizite
  **Include-Liste** von Hostnamen. Aus dem Hosts-Tab lassen sich per Ctrl+Klick mehrere Hosts
  markieren und mit „Auswahl als Favorit…" als benannte Liste speichern. Verwaltung
  (Anlegen/Bearbeiten/Löschen/Aktivieren) im `FilterManagerWindow`. Ablage user-lokal und
  unverschlüsselt unter `%APPDATA%\Kroste\Checkmk\filter.json`.
  Anwendung ist rein clientside (bei ≤ ein paar tausend Hosts problemlos);
  Livestatus-Query-serverside kann später kommen, wenn nötig.
- **Viewer-Modus** (`viewer.json` **neben der Exe**, `ViewerProfile.LoadOrNull`):
  zweite Betriebsart für Leute, die nur gucken sollen. Liegt die Datei da, kommt die
  Verbindung aus ihr (`ViewerConnectionSettingsStore` statt `ConnectionSettingsStore`),
  der Spaltensatz der Service-Tabelle wird aus `columns` gebaut (`StatusColumnFactory`,
  Schlüssel = Checkmk-Sichtnamen wie `svc_state_age`) und `view` liefert Start-Filter.
  Lockdown: nur Status-Tab (Hosts/Dashboard werden in `MainWindow.axaml.cs` **entfernt**,
  nicht `IsVisible`-versteckt — sonst bleiben sie per Ctrl+Tab erreichbar), kein
  Einstellungen-Button, kein Ack/Downtime/Kommentar/Remote-Tool, keine Hotkeys,
  **keine Plugins** (`PluginLoader` wird übersprungen — sonst hebelt ein Plugin-Tab
  den Lockdown aus). Fehlt die Datei, ändert sich nichts; beide Modi laufen aus
  demselben Binary. Drei Punkte, die nicht „aufgeräumt" werden dürfen:
  1. **`secretBase64` ist Maskierung, keine Verschlüsselung** — nie als „Secret ist
     geschützt" dokumentieren. DPAPI ist user-gebunden, die Datei wird verteilt; AES mit
     Key im Binary wäre der SharedAes-Trick aus §8.20, den wir verworfen haben. Die echte
     Grenze ist die **Checkmk-Lese-Rolle** des Users im Profil. Die UI-Sperren sind
     Bedienkomfort, kein Zugriffsschutz — deshalb sitzen zusätzlich `CanWrite`-Guards in
     den ViewModels, nicht nur `IsVisible` im XAML. Base64 wird **strikt** als UTF-8
     dekodiert (`throwOnInvalidBytes`): der häufigste Bedienfehler ist Klartext im
     Base64-Feld, und wenn der zufällig gültiges Base64 ist, gäbe es sonst nur ein
     nichtssagendes `401 Wrong credentials` vom Server.
  2. **Kaputtes JSON schaltet den Viewer-Modus NICHT ab** — `LoadFrom` gibt dann ein
     Profil mit `LoadError` zurück. Ein Tippfehler darf keinem Nur-Gucker die volle
     Oberfläche freischalten.
  Zusätzlich im Viewer-Modus: **`popUpOnProblem`** (Default true) holt bei einer
  Verschlechterung das Fenster maximiert nach vorn (`TrayController.PopUpForProblem`)
  und markiert die betroffene Zeile über `StatusViewModel.RequestSpotlight`. Nur bei
  `ChangeSummary.HasWorsened` — reine Recoveries dürfen nichts aufreißen — und nie bei
  aktivem Snooze. Der `Topmost`-Toggle in `PopUpForProblem` ist nötig, weil `Activate()`
  allein unter Windows den Vordergrund nicht erzwingt; den Tastaturfokus vergibt Windows
  trotzdem nach eigenen Regeln, sichtbar-und-oben ist garantiert, fokussiert nicht.
  3. **Der Filterzustand kommt ausschließlich aus dem Profil.** `HostFilterCollection`
     lädt im Viewer-Modus die persönliche `filter.json` gar nicht erst und persistiert
     nie; `StatusViewModel` ruft `ApplyPreset(v.ToHostFilter())` **bedingungslos** —
     auch bei leerem `hostRegex` (= alle Hosts). Ohne beides gewann die `filter.json`
     des Rechners, auf dem das Profil gebaut wurde: deren `ActiveFilterName` überstimmte
     die Vorgabe und die fremden Favoriten standen im Dropdown. Nicht auf „nur wenn ein
     Host-Bezug da ist" zurückbauen. `view`-Werte im Übrigen sind Startwerte und gehen
     nicht nach `statusview.json` (`PersistState` ist No-Op).
- **Settings:** Verbindung (Host/Site/User/Secret/HTTPS/Cert), Secret verschlüsselt
  via `WindowsDpapiProtector` (DPAPI-CurrentUser). Ablage user-lokal unter
  `%APPDATA%\Kroste\Checkmk\settings.json`. Zusätzlich `KnownSites: [...]` als
  Grundlage für den Site-Umschalter in der Titelleiste (z. B. `LHP-Prod` ⇄
  `Schul_IT` am selben Server — Host/User/Secret bleiben). Der Pfad ist per
  `bootstrap.json` (`SharedSettingsPath`) überschreibbar; alter Samba-Default aus
  v1.0-v1.4 wird beim nächsten Start automatisch auf den lokalen Default
  migriert. `hosts.json` (Domain-Zuordnung) bleibt zentral auf Samba01 —
  Metadaten, keine Secrets.

## 5 · Checkmk-REST-API — nicht-offensichtliche Regeln

Diese Punkte kosten sonst zuverlässig Zeit:

- **Pfad `v1`** (nicht `1.0`): `https://<host>/<site>/check_mk/api/v1/`. Site = URL-Segment
  hinter dem Host.
- **Bearer-Auth im Checkmk-Format:** `Authorization: Bearer <user> <secret>` — User und Secret
  durch **ein Leerzeichen** getrennt, *nicht* Base64. Falsches Format → `401 Wrong credentials`.
- **Automation-User + Automation-Secret** (nicht das GUI-Passwort). Seit 2.4/2.5 wird kein
  `automation`-User mehr auto-angelegt → eigenen anlegen, Rolle mind. für die genutzten Endpunkte.
- **`attributes` nie mit `null`-Werten senden.** Nicht gesetzte Attribute weglassen, sonst
  `400 "These fields have problems: attributes"`. Deshalb hat `JsonOpts` im Client
  `JsonIgnoreCondition.WhenWritingNull` — **nicht entfernen**.
- **Ordner = ID-Pfad, nicht Titel.** `folder` erwartet den ID-Pfad (`/datenbanken/db-mssql`)
  oder die 32-stellige Hex-ID; die Titel aus der Breadcrumb sind es *nicht*. ID steht in der
  Browser-URL hinter `folder=` bzw. via `folder_config`-Endpoint.
- **HTTP-Status ≠ fachlicher Erfolg.** Kommandos laufen serverseitig über Livestatus; bei
  Bedarf Zustand danach erneut abfragen. Discovery/Activate laufen als Hintergrund-Task.
- **Activate Changes:** `If-Match: *` erspart den ETag-Roundtrip.
- **Host anlegen ≠ Monitoring.** Nach dem Anlegen fehlt noch die Service-Discovery
  (`POST /domain-types/service_discovery_run/actions/start/invoke`, mode `fix_all`) + Aktivieren.

### Bootstrap-Datei — geteilt, also niemals user-spezifisch

`bootstrap.json` wird **zentral geteilt** (Samba01, mit Fallback auf `%APPDATA%`).
Daraus folgt eine Regel, die schon einmal produktiv gebrochen wurde: **kein
aufgelöster Benutzerpfad darf in die Datei geschrieben werden.** Genau das war
passiert — `SharedSettingsPath` enthielt `C:\Users\OsteL\AppData\Roaming\…`, jeder
andere Nutzer erbte den Pfad und die App **starb** beim Speichern der Einstellungen
(`DirectoryNotFoundException` aus dem RelayCommand → Avalonia-Dispatcher → Prozessende).

- `SharedSettingsPath` leer = user-lokal, und das ist der Default. `SettingsPathResolver`
  expandiert Umgebungsvariablen und verwirft Pfade, die in ein **fremdes** Benutzerprofil
  zeigen (UNC und `D:\…` bleiben unangetastet — die sind Absicht).
- `TryLoad` darf **nicht** wieder auf „SharedSettingsPath muss gesetzt sein" prüfen:
  dadurch galt die Datei als kaputt und wurde mit einem aufgelösten Profilpfad
  überschrieben — der Weg, auf dem der Fehler entstand.
- Schreibende Zugriffe auf Settings **immer** absichern. `SettingsViewModel.Save`
  fängt jetzt und lässt den Dialog offen; ein Schreibfehler darf nie die App beenden.

### Zentrale Datenbank (`CheckMK_Copilot` auf FOC-SQL01)

Löst die geteilten Teile von `bootstrap.json` und `hosts.json` auf dem Samba-Share
ab. Schema und Begründungen stehen in [`db/README.md`](db/README.md), die Skripte in
`db/`. Vier Punkte, die nicht „aufgeräumt" werden dürfen:

1. **Keine EF-Migrationen, kein `Database.Migrate()`.** Das Schema pflegen die
   SQL-Skripte in `db/`, ausgeführt vom Admin mit `CheckMK_Copilot_SA` (db_owner).
   Die App läuft als `CheckMK_Copilot_Worker` (nur datareader/datawriter) und
   *prüft* nur `SchemaVersion` gegen `CockpitDbContext.ExpectedSchemaVersion`.
   50 Clients, die beim Start gleichzeitig DDL versuchen, wären in keiner Lesart
   gut — und die meisten dürfen es ohnehin nicht. Deshalb ist auch
   `EntityFrameworkCore.Design` bewusst **nicht** referenziert.
2. **Der Ausfall-Cache ist tragend, kein Beiwerk.** Der Grund, vom Share
   wegzugehen, war dessen Verfügbarkeit — also darf die DB nicht der nächste
   Engpass werden. `GlobalSettingsProvider` schreibt nach jedem Erfolg
   `%APPDATA%\Kroste\Checkmk\globals-cache.json` und fällt beim Ausfall darauf
   zurück (`SettingsOrigin.Cache`), erst danach auf eingebaute Vorgaben.
3. **`GlobalSetting` ist Schlüssel/Wert, nicht eine Spalte je Einstellung.**
   Eine neue Einstellung soll keinen DDL-Termin mit dem SA-Konto brauchen.
   Fehlende, leere und kaputte Werte fallen einzeln auf ihren Default zurück
   (`CockpitGlobals.FromRows`) — ein halb gepflegter Datenbestand darf den Start
   nicht verhindern.
4. **Secrets bleiben user-lokal.** Verbindungs-Secret (`settings.json`) und
   SSH-Passwörter (`ssh-creds.json`) gehören nicht in eine Tabelle, die 48 Leute
   lesen dürfen — unabhängig von TDE. Der Verbindungsstring in `database.json`
   **neben der EXE** ist **Verschleierung, kein Zugriffsschutz** — der Schlüssel
   steckt im Binary daneben. Deshalb heißen die Methoden `Obfuscate`/
   `Deobfuscate` und nicht Encrypt/Decrypt; nicht in „Verschlüsselung"
   umbenennen, das ist dieselbe Ehrlichkeit wie bei `secretBase64` im
   Viewer-Profil (§4). Erzeugt wird die Datei mit
   `Checkmk.App.exe --protect-db "<String>"`. Quellenreihenfolge:
   `db-dev.json` (%APPDATA%, Entwicklung) → `database.json` (neben der EXE,
   Ausrollweg) → `bootstrap.json`.

5. **`DbHostDomainStore` hält eine Momentaufnahme im Speicher.** `Load()` macht
   kein I/O — `HostContext.DomainFor` ruft es für *jeden* Hostnamen auf, als
   Datenbank-Roundtrip wäre das absurd. Aktualisiert wird beim Start
   (`RefreshAsync`) und nach jedem Schreiben. Schlägt das Lesen fehl, bleibt die
   alte Momentaufnahme stehen: eine leere Zuordnung wäre schlimmer als eine
   veraltete, weil dann jeder Host auf die Default-Domain fiele und Ping/RDP/SSH
   ins Leere liefen. `Save()` diffed gegen die Tabelle statt alles
   zurückzuschreiben — das war der Fehler der alten `hosts.json`.
6. **Die Übernahme aus `hosts.json` läuft genau einmal** (`ImportLegacyIfEmptyAsync`,
   nur bei komplett leerer Tabelle). Danach ist die Tabelle die Wahrheit und die
   Datei wird nie wieder gelesen — sonst überschriebe ein Rechner mit altem
   Dateistand später zentrale Änderungen.

`DbContext` ist nicht threadsicher — deshalb `CockpitDatabase.CreateContext()`
je Vorgang statt eines Singletons; Hintergrund-Refresh und UI greifen parallel zu.

**Transitives Pinning ist Pflicht** (`CentralPackageTransitivePinningEnabled`).
Ohne es verlangt der Graph `Microsoft.Extensions.*` in 9.0.11, 10.0.0 und 10.0.8
nebeneinander, und jede dieser Versionen müsste in diesem Netz einzeln von Hand
ins Offline-Bundle geholt werden. Mit Pinning gibt es genau eine Version je Paket.
Die `Microsoft.Extensions.*`-Einträge in `Directory.Packages.props` stehen
deshalb dort, obwohl kein Projekt sie direkt referenziert — nicht entfernen.

**NuGet-Falle in diesem Netz:** Der Proxy liefert `.nupkg` von nuget.org mit
**403** aus (Metadaten/`index.json` kommen durch). Pakete kommen deshalb aus dem
Offline-Bundle `C:\NuGet-Local`. `Microsoft.Data.SqlClient` zieht die komplette
MSAL-/Azure-Identity-Kette nach (~15 Pakete), die wir bei einem SQL-Login nie
anfassen — vermeidbar ist das nicht, EF Cores SqlServer-Provider setzt sie voraus.
Und: NuGet löst transitive Abhängigkeiten auf die **niedrigste passende** Version
auf, nicht auf die höchste lokal vorhandene — ein neueres Paket im Ordner ersetzt
eine geforderte ältere Version also nicht.

## 6 · Abhängigkeiten — Fallen

- **Avalonia >= 12.1** (aktuell 12.1.0, nativer Wayland-Backend ab 12.1). Breaking vs. v11: `Avalonia.Diagnostics` ist raus →
  `AvaloniaUI.DiagnosticsSupport` (Debug-only). `Window.SystemDecorations` → `WindowDecorations`
  (`WindowDecorations.BorderOnly`). `TextBox.Watermark` → `PlaceholderText`.
  `Avalonia.Controls.DataGrid` und `AvaloniaUI.DiagnosticsSupport` haben eigene Versionskadenz.
- **FluentAssertions auf v7 pinnen** (`[7.2.2,8.0.0)`). v8 = kommerzielle Xceed-Lizenz.
  Bei Dependabot/Renovate die Obergrenze prüfen — automatische Updates heben den Pin sonst aus
  (Major-Bumps für FluentAssertions per `ignore` ausschließen).
- **`Microsoft.Toolkit.Uwp.Notifications`** zieht transitiv `System.Drawing.Common 4.7.0`
  hinein, das mit `GHSA-rxg9-xrhp-64gj` (kritisch) blockiert `NU1904` unter
  `TreatWarningsAsErrors`. Explizit auf **10.0.9** überschreiben.

## 7 · Projektstandard

Flach (kein `src/`), `.slnx`, CPM (`Directory.Packages.props`), `Directory.Build.props`
(net10, Nullable, `TreatWarningsAsErrors`, `RepositoryUrl github.com/Kroste/`), MinVer aus
Git-Tags (`v*`), `.editorconfig` (file-scoped namespaces), NLog (Secrets vor dem Loggen
maskieren), globaler Exception-Handler. **Single-TFM**: `Checkmk.App` und
`Checkmk.Core.Tests` targeten `net10.0-windows10.0.19041.0` (WinRT-Toasts +
DPAPI). `Checkmk.Core` bleibt `net10.0`. CI läuft auf `windows-latest`, Release
erzeugt bei Tag `v*` ausschließlich das Windows-ZIP.

**Release-Notes-Konvention:** Für ausführliche Notes eine Datei
`RELEASE_NOTES/<tag>.md` im Repo anlegen (Beispiel: `RELEASE_NOTES/v1.0.0.md`).
Der Release-Workflow liest sie bevorzugt; Fallback ist die Message des annotated
Git-Tags. `generate_release_notes` ist bewusst aus — sonst hängt GitHub redundant
den Commit-Log an.

## 8 · Roadmap (nach Priorität)

1. ✅ Ack + Downtime aus der Liste.
2. ✅ Host-Filter mit Regex + Favoriten (Include-Listen).
3. ✅ Zentrale Windows-Verbindungsdatei auf Fileshare (Samba01 542$).
4. ✅ Service Discovery für bestehende Hosts (Config-Tab: Host → `fix_all` → aktivieren).
5. ✅ Host-Detailansicht (Doppelklick oder Rechtsklick → eigenes Fenster).
6. ✅ Autoupdater (Phase 1): GitHub-Releases-Check + Statusleisten-Badge + Dialog.
   Phase 2 (Selbst-Ersetzen + signierter Manifest) siehe Punkt 17.
7. ✅ Bulk-Ack/Downtime (Status-Tab + Host-Detail: Ctrl/Shift-Klick auf Services →
   ein Kommentar, iterative Ausführung, Einzelfehler brechen den Bulk nicht ab).
8. ✅ Kommentare (Anzeige im Host-Detail + Add auf Host/Service).
   DB-Health-Board wurde als „durch Host-Filter mit Regex/Include-Liste ausreichend
   abgedeckt" verworfen — statt eines eigenen Tabs legt jeder DB-Admin sich einen
   Favoriten „DB-Server" an (Regex `.*sql.*|.*ora.*` oder Include-Liste der Instanzen)
   und sieht seine DBs in Status/Konfig gefiltert.
9. ✅ Baumansicht (Hosts → Services) mit OS-Pictogrammen (`OsDetection`).
10. ✅ Tray + Status-Notifications (WinRT-Toast, Action-Center-kompatibel).
11. ✅ CSV-Export + Freitext-Filter über Ausgabe/Alias.
12. ✅ IP-Fallback per Ping/DNS im Host-Detail, wenn Checkmk keine liefert.
13. ✅ Client-Aktualisierung (Kontextmenü, Remote-PowerShell, Agent-Deinstall/Install/Register)
    — seit v1.7.0 ausgelagert ins Plugin
    [`Checkmk-Plugin-AgentUpdater`](https://github.com/Kroste/Checkmk-Plugin-AgentUpdater).
14. ✅ **Client-Aktualisierung härten**: `Start-Process msiexec` mit `-PassThru`
    + Exit-Code-Prüfung. Wanderte mit dem Plugin-Auszug in v1.7.0 in dessen
    Default-Skript-Vorlage.
15. ✅ **Kommentare löschen** — `DeleteCommentAsync` mit Dual-Fallback:
    `POST /domain-types/comment/actions/delete/invoke` (`delete_type: "by_id"`) und bei
    404/405 `DELETE /objects/comment/{id}`. Roter ✕-Button an jedem Kommentar im Host-Detail.
16. ✅ **OS-Familie aus Custom Host Attribute** statt Agent-PluginOutput-Parse. Der
    HW/SW-Inventur-Weg wurde als Umweg verworfen — verlässlicher ist das Custom
    Attribute (z. B. „Operation System"), das auf Folder-Ebene gesetzt und vererbt
    wird. Umsetzung: `HostAttributes.AdditionalProperties` als Catch-All,
    `Bootstrap.HostOsAttributeKeys` als Kandidatenliste, `IHostOsCache` als
    prozessweiter Cache. StatusViewModel.OsFor bevorzugt Cache, fällt auf
    OsDetection zurück. Vollständige OS-Version (2022, RHEL 9 usw.) bleibt offen.
17. **Autoupdater Phase 2**: **Selbst-Ersetzen des Binary** (Update.exe-Helper mit
    atomic swap) und **signierter Manifest-JSON** (Ed25519), sobald der Kanal von
    GitHub auf einen internen Fileshare umgestellt wird.
18. **DPAPI-NG mit AD-Gruppen-SID** — obsolet, seit die Verbindung wieder user-lokal
    liegt (DPAPI-CurrentUser reicht). Nur relevant, falls wir irgendwann doch wieder
    einen geteilten Store brauchen.
19. ✅ **Zweite Checkmk-Instanz (Schulen)** — verifiziert: gleicher Server, nur
    andere Site (`Schul_IT`). Umgesetzt als leichter Site-Umschalter in der Titelleiste
    (`ConnectionSettings.KnownSites` + `UpdateActiveSite`), statt vollem Profil-Manager.
    Volle benannte Verbindungsprofile bleiben offen für den Fall dass es doch ein
    zweiter Server wird.
20. **Verbindungsdaten wieder user-lokal** (fertig): Nach kurzem Fileshare-Experiment
    (SharedAes) zurück nach `%APPDATA%\Kroste\Checkmk\settings.json` (DPAPI-CurrentUser).
    Anmeldedaten gehören pro Nutzer; der SharedAes-Trick war nur Zufalls-Einsichts-Schutz,
    kein echter Zugriffsschutz. `hosts.json` (Domain-Zuordnung) bleibt zentral —
    das sind Metadaten, keine Secrets.
21. ✅ **Viewer-Modus für Nur-Gucker** — `viewer.json` neben der Exe (Verbindung,
    Spaltensatz, Start-Filter) schaltet Kiosk-Betrieb: nur Status-Tab, keine
    Schreibaktionen, keine Plugins. Details und die drei Nicht-Aufräumen-Punkte
    in §4. Ein Profil-Manager mit mehreren benannten Viewer-Sichten in *einer*
    Datei wurde nicht gebaut — eine Sicht pro Ausgabe ist die Verteil-Einheit.
22. ✅ **Spalten frei konfigurierbar** (Status-Tab, Normalmodus) — Rechtsklick auf die
    Kopfzeile, Checkbox-Liste, Drag zum Umsortieren, persistent in `columns.json`.
    Details und die drei Fallen in §4. Für die Host-Detail-Tabelle bewusst *nicht*
    umgesetzt: dort ist der Spaltensatz kurz und `host` wäre redundant.
23. ✅ **Refresh ohne Einfrieren** — Abruf/Parse/Filtern auf dem ThreadPool,
    Fortschrittsbalken mit Restzeit in der Statusleiste, Collection-Austausch per
    Reset statt Einzel-Adds. Details und die drei Nicht-Zurückbauen-Punkte in §4.
24. ✅ **Zentrale Datenbank statt Fileshare** (`CheckMK_Copilot` auf FOC-SQL01,
    EF Core 10). Die geteilten Teile von `bootstrap.json` und `hosts.json` liegen
    in Tabellen; `hosts.json` wird einmalig übernommen. Ausfall-Cache,
    Zwei-Konten-Modell, `database.json` neben der EXE. Details in §5.
    Gründe: Schreibrechte auf dem Share hatten nur wenige, und das
    Read-Modify-Write der ganzen `hosts.json` verlor bei zwei gleichzeitigen
    Bearbeitern lautlos Einträge.

### In Arbeit: Standort-Karte (Punkte 25–28)

Fachlicher Hintergrund: 1105 Hosts über Potsdam verteilt (Stadtverwaltung mit
Außenstellen), 48 Nutzer in Teams von 2–3 Personen (DB, Netzwerk, Backup, ESX,
Fileservice, AD, Exchange, …), Mehrfachmitgliedschaft normal. Ziel ist eine
Karte, auf der ein Bereich grün/gelb/rot den schlechtesten Status seiner Hosts
zeigt.

**Die tragende Entscheidung: geteilte Karte, Linse pro Team.** Ein Serverraum ist
ein physischer Ort — er wird **einmal** gezeichnet und ein Gerät **einmal**
zugeordnet. Was ein Team davon sieht, entscheidet allein sein Host-Filter. Die
Bereichsfarbe entsteht deshalb erst in der `TeamView`, nicht in `Area`:
schlechtester Status der Hosts, die im Bereich stehen **und** auf den Filter der
Sicht passen. Derselbe Raum ist für das DB-Team grün und für den Wachschutz rot,
wenn die USV Netzausfall meldet. Nicht auf „jedes Team zeichnet seine eigene
Karte" zurückbauen: dann driften acht Polygone desselben Raums auseinander, und
wer einen Switch umträgt, müsste es acht Teams sagen. `HostArea.HostName` ist
Primärschlüssel — genau ein Bereich pro Host — und trägt `AssignedBy`.

Teams sind **Organisation, kein Zugriffsschutz** (alle 48 dürfen alle Hosts
sehen, so gewollt). Admin-Zuordnung über `dbo.AppAdmin`; wer in keinem Team ist,
sieht alles.

25. **Bereiche ohne Karte** — Bereichsbaum, Zuweisung per Mehrfachauswahl
    (die Geste „Auswahl als Favorit" gibt es schon; 1105 Hosts einzeln
    zuzuweisen ist keine Option), Status-Rollup nach oben. Schon nutzbar,
    bevor eine Karte existiert — deshalb bewusst **vor** Punkt 27.
26. **Teams + geteilte Filter** — `filter.json` zieht in die DB, ein Filter
    gehört entweder einem Team oder einer Person. Der Alltagsgewinn: heute baut
    sich jeder der 48 seinen eigenen, und die Urlaubsvertretung fängt bei null an.
27. **Karte** — eigenes Kachel-Canvas in Avalonia (Slippy-Map-Mathematik,
    Polygone als Overlay, Treffer-Erkennung für den Rechtsklick). **Kein
    WebView, kein Google Maps**: Maps Platform kostet pro Load, verbietet
    Kachel-Caching und schickt die Standorte der eigenen IT-Infrastruktur an
    Google — das übersteht keine Datenschutzprüfung einer Stadtverwaltung.
    Stattdessen die **Geobasisdaten der LGB Brandenburg** (Open Data,
    dl-de/by-2.0, WMS/WMTS über den Geobroker): amtliche Orthophotos von
    Potsdam, dürfen gespiegelt und gecacht werden, verlassen das LVN nicht.
    Für die Campus-Ebene ist ein Luftbild die Rasterquelle, `Area.MapLayerKey`
    benennt sie je Bereich.
28. **Team-Sichten/Kiosk** — Viewer-Modus um Startbereich + Zoom erweitern.
    Wie beim Viewer-Modus gilt: Sichtbarkeitsgrenzen sind Bedienkomfort, die
    echte Grenze ist die Checkmk-Rolle.

**Nicht gebaut und warum:** Koordinate je Host (unnötig — Hosts hängen an
Bereichen, Bereiche haben die Geometrie; spart Geocoding komplett).
`geography`-Spaltentyp (bräuchte NetTopologySuite als weiteres Paket, und wir
rechnen nichts räumlich — GeoJSON in `nvarchar(max)` reicht). Ein Dienst vor der
Datenbank (die Sichten sollen nur in der Anwendung sichtbar sein, also verbinden
sich die Clients direkt).

## 9 · Deal

Lars liefert Ideen, Claude implementiert. Immer auf frischem `origin/main` aufsetzen, Änderungen
als Commit/Patch liefern (kein Push aus der Sandbox möglich).
