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
  (löscht rekursiv alle `bin/`/`obj/` — plattformspezifisch für Windows/Linux/macOS).
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

- **Status-Tab:** Host-/Service-Livestatus (Polling, Auto-Refresh), Ampel-Punkte, Filter,
  „Nur Probleme". **Ack + Downtime direkt aus der Liste** (Toolbar-Button + Rechtsklick):
  Zeile wählen → Dialog mit Pflicht-Kommentar; Downtime mit Dauer-Presets (1h/2h/4h/bis 06:00).
- **Konfig-Tab:** Host anlegen (Name/Ordner/IP/Alias), Host-Liste, „Änderungen aktivieren".
- **Settings:** Verbindung (Host/Site/User/Secret/HTTPS/Cert), Secret **DPAPI-verschlüsselt**
  in `%APPDATA%/Kroste/Checkmk/settings.json`. About mit GitHub + Buy-Me-a-Coffee.

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
- **`Tmds.DBus.Protocol`** (transitiv via Avalonia/Linux): aktuelle Version explizit pinnen
  (z. B. 0.94.2), *nicht* das Audit unterdrücken. Alte Versionen haben GHSA-xrw6-gwf8-vvr9 →
  bricht mit `TreatWarningsAsErrors` (NU1903).

## 7 · Projektstandard

Flach (kein `src/`), `.slnx`, CPM (`Directory.Packages.props`), `Directory.Build.props`
(net10, Nullable, `TreatWarningsAsErrors`, `RepositoryUrl github.com/Kroste/`), MinVer aus
Git-Tags (`v*`), `.editorconfig` (file-scoped namespaces), NLog (Secrets vor dem Loggen
maskieren), globaler Exception-Handler. CI + Release als GitHub Actions: Release erzeugt bei
Tag `v*` Windows-ZIP, Linux-tar.gz und AppImage.

## 8 · Roadmap (nach Priorität)

1. ✅ Ack + Downtime aus der Liste.
2. **Service Discovery für bestehende Hosts** (Config-Tab: Host → `fix_all` → aktivieren) —
   bringt vorhandene Hosts wie `DBSQL01` ins Monitoring.
3. **Host-Detailansicht** (alle Services + Attribute) — adressiert den größten Schmerzpunkt
   (unübersichtliche Navigation).
4. **Filter / Suche / Gruppierung** (nach Ordner/Host/Status, OK-Zeilen einklappen).
5. Tier 3: Bulk-Ack/Downtime, Host-Downtime („ganzer Host in Wartung"), Kommentare,
   DB-Health-Board (MSSQL/Oracle-Services über alle DB-Hosts).

## 9 · Deal

Lars liefert Ideen, Claude implementiert. Immer auf frischem `origin/main` aufsetzen, Änderungen
als Commit/Patch liefern (kein Push aus der Sandbox möglich).
