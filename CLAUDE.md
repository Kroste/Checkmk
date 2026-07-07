# Checkmk Cockpit

Avalonia-12-Desktop-Tool für Checkmk **2.5.x** (Pro/Enterprise) über die **REST-API v1**.
Zwei Tabs: **Status** (read-only, Auto-Refresh, Filter) und **Konfiguration**
(Host anlegen, Änderungen aktivieren). Zielplattform primär **Windows (win-x64)**.

## Grundlagen

- **Stack:** .NET 10, Avalonia 12 (MVVM), CommunityToolkit.Mvvm (Source Generators),
  manuelles DI via `Microsoft.Extensions.Hosting`-ServiceCollection, NLog.
- **Struktur:** flach (kein `src/`), `.slnx`, CPM (`Directory.Packages.props`),
  `Directory.Build.props` (net10, Nullable, `TreatWarningsAsErrors`, Repo `github.com/Kroste/`).
- **Fenster:** `ChromeWindow`-Basis (`WindowDecorations.BorderOnly`, `CanResize=true`).
- **Secrets:** Automation-Secret wird per **DPAPI (CurrentUser)** verschlüsselt in
  `%APPDATA%/Kroste/Checkmk/settings.json` abgelegt; im Log wird es maskiert.

## Projekte

| Projekt | Zweck |
|---|---|
| `Checkmk.Core` | REST-API-Client (`CheckmkClient`), Modelle, Optionen. UI-unabhängig. |
| `Checkmk.App` | Avalonia-UI: Tabs, Settings, About, DI-Bootstrap. |
| `Checkmk.Core.Tests` | xUnit + FluentAssertions v7 (Xceed-Lizenz von v8 vermeiden). |

## API-Besonderheiten (2.5)

- **Pfad `v1`** statt `1.0` (bis 2.4.0). `1.0` bleibt kompatibel, `v1` ist empfohlen.
- **Bearer-Auth im Checkmk-Format:** `Authorization: Bearer {user} {secret}`
  (User + Secret durch Leerzeichen getrennt — **nicht** Base64).
- **HTTP-Status ≠ fachlicher Erfolg:** Kommandos laufen serverseitig über Livestatus;
  bei Bedarf Zustand danach erneut abfragen.
- **Activate Changes:** `If-Match: *` erspart das ETag-Roundtrip.
- Pro-Edition = voller Endpunkt-Umfang (SLA, Agent Bakery) — aktuell ungenutzt.

## Build & Run

```bash
dotnet build Checkmk.slnx -c Release
dotnet test  Checkmk.Core.Tests/Checkmk.Core.Tests.csproj
# Self-contained Distribution (bevorzugt, kein System-.NET nötig):
dotnet publish Checkmk.App/Checkmk.App.csproj -c Release -r win-x64 \
  --self-contained -p:PublishSingleFile=true
```

## Roadmap / offen

- Ack- und Downtime-Aktionen aus der Status-Tabelle heraus (Client-Methoden liegen bereits vor).
- Livestatus-TCP als zweiter Read-Provider für Kiosk/Sekunden-Refresh (Provider-Abstraktion).
- Host-Bearbeiten/Löschen (ETag-Handling im Client ergänzen) im Config-Tab.
- MinVer/GitHub-Actions (CI + Release: Windows-ZIP), `.editorconfig`, Buy-Me-a-Coffee-URL prüfen.

## Deal

Lars liefert Ideen, Claude implementiert.
