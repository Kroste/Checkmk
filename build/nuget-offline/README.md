# Neue NuGet-Pakete in diesem Netz

Der Proxy im Fachbereichsnetz liefert `.nupkg` von nuget.org mit **HTTP 403**
aus; Metadaten (`index.json`, `.nuspec`) kommen durch. Pakete stammen deshalb
aus dem Offline-Bundle `C:\NuGet-Local`, erzeugt vom Werkzeug „Nougat".

Kommt eine neue Abhängigkeit dazu, muss sie von Hand beschafft werden. Die
Reihenfolge ist wichtiger als die Werkzeuge:

## 1. Zuerst pinnen, dann laden

In `Directory.Packages.props` steht
`<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>`.
**Das ist keine Kosmetik.** Ohne transitives Pinning löst NuGet jede transitive
Abhängigkeit auf die *niedrigste passende* Version auf — nicht auf die höchste
lokal vorhandene. Der Graph verlangt dann dieselbe Bibliothek in drei Versionen
nebeneinander (bei EF Core waren es `Microsoft.Extensions.*` in 9.0.11, 10.0.0
und 10.0.8), und **jede** dieser Versionen müsste einzeln beschafft werden,
obwohl 10.0.11 längst im Bundle liegt.

Also: neue transitive Abhängigkeiten als `PackageVersion` eintragen, bevor
irgendetwas geladen wird. Das erspart die meisten Downloads.

Nougat liest `Directory.Packages.props` — was dort steht, nimmt ein späterer
Bundle-Lauf automatisch mit.

## 2. Fehlliste ermitteln

```powershell
.\Resolve-MissingPackages.ps1 -Roots "Microsoft.EntityFrameworkCore.SqlServer:10.0.11"
```

Läuft den Abhängigkeitsbaum über die `.nuspec`-Metadaten ab, ohne ein Paket zu
laden, und gibt die fehlenden Download-URLs aus.

## 3. Laden

Auf einer Maschine mit nuget.org-Zugriff:

```powershell
.\Resolve-MissingPackages.ps1 -Roots "…" | .\Fetch-Packages.ps1
```

oder die URL-Liste in eine Datei kopieren und dort abarbeiten.

## 4. `dotnet build` ist der Schiedsrichter

`Resolve-MissingPackages.ps1` ist eine gute Näherung, **kein Ersatz** für den
Restore. Es weicht in beide Richtungen ab:

- **Zu wenig**: NuGet lädt beim Auflösen auch Knoten, die es später verwirft,
  und braucht deshalb manchmal ein, zwei Pakete mehr.
- **Zu viel**: Das Skript kennt das transitive Pinning nicht und meldet
  Versionen, die durch das Pinning gar nicht mehr angefragt werden. Beim
  aktuellen Stand sind das `Microsoft.Extensions.DependencyInjection.Abstractions
  8.0.2` und `System.Memory 4.5.0` — der Build läuft ohne beide sauber durch.

Also nicht versuchen, das vorherzusagen: bauen lassen und die Fehlermeldung
lesen. Das Skript liefert nur den Startpunkt.

Beim Einbau von EF Core hat das Vorhersagen vier Runden Nachladen gekostet
(12 → 17 → 2 → 1 Paket). Mit „erst pinnen, dann Restore fragen" wären es zwei
gewesen.
