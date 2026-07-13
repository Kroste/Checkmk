# Checkmk Cockpit

Avalonia-12-Desktop-Tool, das die **täglichen Checkmk-Admin-Handgriffe entwirrt** — die
Aktionen, die das Webinterface tief in Menüs vergräbt, liegen hier flach an der Zeile, wo
man das Problem sieht. Ziel-Backend: **Checkmk 2.5.x Pro** über die **REST-API v1**.
Ziel-Plattform primär **Windows (win-x64)**.

> Diese Datei wird von Copilot/Claude in VS Code als always-on-Kontext gelesen. Regeln sind
> bewusst kurz, begründet und mit Beispielen — nicht wiederholen, was Linter/`.editorconfig`
> ohnehin erzwingen.

---

## 1 · Build, Test, Run (immer zuerst)

```bash
dotnet build Checkmk.slnx -c Release          # muss 0 Warnings / 0 Errors sein
dotnet test  Checkmk.slnx                      # xUnit + FluentAssertions v7
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
| `Checkmk.App` | Avalonia-UI: Tabs, Dialoge, DI-Bootstrap. |
| `Checkmk.Core.Tests` | xUnit + FluentAssertions **v7** (v8 = kommerzielle Xceed-Lizenz, siehe §6). |

**Muster:** MVVM mit CommunityToolkit.Mvvm (Source Generators, `[ObservableProperty]`,
`[RelayCommand]`); manuelles DI via `ServiceCollection` in `Program.cs`; NLog (Secrets maskiert).
`CheckmkClient` ist bewusst frei von UI/DI, damit er wiederverwendbar bleibt.

**Laufzeit-Client:** Verbindung ist zur Laufzeit änderbar → `ICheckmkClientProvider` baut den
`CheckmkClient` aus den aktuellen Settings neu (statt statischem `IOptions`). Nach dem Speichern
der Settings `Configure(...)` aufrufen, nicht die App neu starten.

**Fenster:** alle Fenster erben von `ChromeWindow` (randlos, `WindowDecorations.BorderOnly`,
`CanResize=true`, eigene Titelleiste). Dialoge mit Laufzeitdaten (z. B. `ServiceActionDialog`)
werden direkt instanziiert, nicht über DI.

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
- **Client-Aktualisierung** (`AgentUpdater` + `AgentUpdateWindow` + `CredentialDialog`):
  aus dem Kontextmenü (Zeile/Baum-Knoten) den Checkmk-Agent auf einem Zielhost
  aktualisieren. Ablauf: `CredentialDialog` fragt Admin-Credentials → Remote-PowerShell
  zum Ziel → **Installer per `Copy-Item -ToSession`** auf den Host kopiert (umgeht
  Double-Hop) → editierbare **Skript-Vorlage** ausführen (Deinstall → Install → Register).
  Agent-Share und Skript-Vorlage in den Settings pflegbar. Windows-only, WinRM
  vorausgesetzt (in DMZ i. d. R. geblockt). **Fallen (aus Fixes gelernt):**
  Skript-Ausführung über `powershell.exe -File <tmp.ps1>`, **nicht** `-Command -` via
  STDIN (verschluckt mehrzeilige Skripte). Erfolg am **Exit-Code**, nicht an stderr
  (native Tools wie `cmk-agent-ctl`/`msiexec` schreiben Infos auch nach stderr).
  `cmk-agent-ctl register` braucht **`--trust-cert`**, sonst interaktive Cert-Abfrage
  → `NativeCommandError`. Skript/Passwörter **niemals loggen** (Regression in v1.2.0
  gefixt). `Start-Process msiexec -Wait` meldet Nicht-Null-Exits nicht automatisch —
  für harte Fehlererkennung `-PassThru` + `$proc.ExitCode`-Prüfung in die Vorlage.
- **Autoupdater (Phase 1):** Beim Start fragt `GitHubReleasesUpdateChecker` den
  `Bootstrap.UpdateChannelUrl` ab (Default `api.github.com/repos/Kroste/Checkmk/releases/
  latest`), vergleicht mit `Assembly.Version` und meldet bei neuerer Version einen
  gelben Badge in der Statusleiste. Klick öffnet den `UpdateDialog` (Release-Notes +
  „Release-Seite öffnen"/„Später"/„Diese Version überspringen"). Skip-Version liegt in
  `%APPDATA%\Kroste\Checkmk\updates.json`.
  Kein Selbst-Ersetzen des Binary — Roadmap-Phase 2.
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

## 6 · Abhängigkeiten — Fallen

- **Avalonia >= 12** (min. 12.0.4). Breaking vs. v11: `Avalonia.Diagnostics` ist raus →
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
13. ✅ Client-Aktualisierung (Kontextmenü, Remote-PowerShell, Agent-Deinstall/Install/Register).
14. ✅ **Client-Aktualisierung härten**: `Start-Process msiexec` in der Skript-Vorlage nutzt
    jetzt `-PassThru` + `$proc.ExitCode`-Prüfung (Nicht-Null-Exits werfen). Site-CA-Push
    und Entfernen von `--trust-cert` bleibt offen — braucht AD-Rollout und ist eigener Arbeitspunkt.
15. ✅ **Kommentare löschen** — `DeleteCommentAsync` mit Dual-Fallback:
    `POST /domain-types/comment/actions/delete/invoke` (`delete_type: "by_id"`) und bei
    404/405 `DELETE /objects/comment/{id}`. Roter ✕-Button an jedem Kommentar im Host-Detail.
16. **OS-Version** aus der Checkmk-HW/SW-Inventur (`os_version`) statt nur Familie
    aus dem Agent-Output — braucht einen weiteren Endpunkt-Aufruf.
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

## 9 · Deal

Lars liefert Ideen, Claude implementiert. Immer auf frischem `origin/main` aufsetzen, Änderungen
als Commit/Patch liefern (kein Push aus der Sandbox möglich).
