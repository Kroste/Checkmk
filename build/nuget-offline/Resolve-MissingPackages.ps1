<#
.SYNOPSIS
    Findet heraus, welche NuGet-Pakete im lokalen Offline-Bundle fehlen — ohne
    ein einziges Paket herunterzuladen.

.DESCRIPTION
    Im Fachbereichsnetz liefert der Proxy .nupkg von nuget.org mit HTTP 403 aus;
    nur Metadaten (index.json, .nuspec) kommen durch. Pakete stammen deshalb aus
    C:\NuGet-Local. Kommt eine neue Abhängigkeit dazu, muss sie von Hand
    beschafft werden.

    Dieses Skript läuft den Abhängigkeitsbaum über die .nuspec-Metadaten ab und
    meldet, was fehlt. Die Ausgabe geht direkt in Fetch-Packages.ps1.

    WICHTIG — vorher prüfen: Ist in Directory.Packages.props
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
    gesetzt und die neue Abhängigkeit dort gepinnt? Das kollabiert die
    Versionsvielfalt auf eine Version je Paket und erspart die meisten
    Downloads. Ohne Pinning verlangt der Graph dieselbe Bibliothek in drei
    Versionen nebeneinander.

    Und: Maßgeblich ist am Ende immer `dotnet build`. Dieses Skript ist eine
    gute Näherung, kein Ersatz — NuGet lädt beim Auflösen auch Knoten, die es
    später verwirft.

.PARAMETER Roots
    Wurzelpakete als "Id:Version", kommasepariert.

.EXAMPLE
    .\Resolve-MissingPackages.ps1 -Roots "Microsoft.EntityFrameworkCore.SqlServer:10.0.11"

.EXAMPLE
    .\Resolve-MissingPackages.ps1 -Roots "Foo:1.2.3" | .\Fetch-Packages.ps1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Roots,

    [string]$BundleDir = 'C:\NuGet-Local',

    [string]$TargetFramework = 'net10.0'
)

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

$cacheRoot = Join-Path $env:USERPROFILE '.nuget\packages'

$localFiles = @{}
Get-ChildItem "$BundleDir\*.nupkg" -ErrorAction SilentlyContinue |
    ForEach-Object { $localFiles[$_.BaseName.ToLower()] = $true }

# Exakte Version. NuGet vereinheitlicht auf die hoechste im Graph GEFORDERTE
# Version -- nicht auf die hoechste lokal vorhandene. Liegt 10.0.5 im Ordner und
# fordert der Graph 9.0.11, wird 9.0.11 geladen.
function Test-Have([string]$id, [string]$ver) {
    if ($localFiles.ContainsKey("$id.$ver".ToLower())) { return $true }
    return (Test-Path (Join-Path $cacheRoot ("{0}\{1}" -f $id.ToLower(), $ver)))
}

$tfmMajor = if ($TargetFramework -match '(\d+)\.(\d+)') { [int]$Matches[1] } else { 10 }

# NICHT $roots nennen: PowerShell-Variablen sind case-insensitiv, $roots und
# der Parameter $Roots waeren dieselbe Variable — die Zuweisung wuerde den
# Parameter ueberschreiben, bevor er gelesen ist.
$rootList = @()
foreach ($r in ($Roots -split ',')) {
    $p = $r.Trim() -split ':'
    if ($p.Count -eq 2) { $rootList += , @($p[0], $p[1]) }
}
if ($rootList.Count -eq 0) { throw "Keine gueltigen Wurzeln in '$Roots' (Format: Id:Version)." }

$seen = @{}
$missing = @{}
$queue = New-Object System.Collections.Queue
foreach ($r in $rootList) { $queue.Enqueue($r) }

while ($queue.Count -gt 0) {
    $item = $queue.Dequeue()
    $id = $item[0]; $ver = $item[1]
    $key = "$id/$ver"
    if ($seen.ContainsKey($key)) { continue }
    $seen[$key] = $true

    if (-not (Test-Have $id $ver)) { $missing[$key] = $true }

    $lid = $id.ToLower()
    $url = "https://api.nuget.org/v3-flatcontainer/$lid/$ver/$lid.nuspec"

    # curl.exe statt Invoke-WebRequest: letzteres laeuft am FortiProxy in ein 407
    # (Proxyauthentifizierung). Mit Retry, weil ein einzelner Aussetzer sonst den
    # ganzen Teilbaum verschluckt -- und das faellt nur als zu kurze
    # Ergebnisliste auf, nicht als Fehler.
    $xml = $null
    for ($attempt = 1; $attempt -le 3 -and -not $xml; $attempt++) {
        $raw = (& curl.exe -s -S --max-time 30 $url) 2>&1
        $text = ($raw | Out-String).Trim()
        if ($text -and $text.StartsWith('<')) {
            try { $xml = [xml]$text } catch { $xml = $null }
        }
        if (-not $xml) { Start-Sleep -Milliseconds 400 }
    }
    if (-not $xml) { Write-Warning "nuspec nicht lesbar: $id $ver"; continue }

    $deps = $xml.package.metadata.dependencies
    if ($null -eq $deps) { continue }

    $list = @()
    if ($deps.group) {
        # NuGet nimmt die naechstkompatible Gruppe, nicht nur eine exakte
        # net10.0-Gruppe. Microsoft.Data.SqlClient etwa hat gar keine --
        # mit exakter Suche fielen dessen ~12 Abhaengigkeiten unter den Tisch.
        $best = $null; $bestRank = -1
        foreach ($g in $deps.group) {
            $tfm = ("$($g.targetFramework)").Trim().ToLower() -replace '[\s,]', ''
            $rank = -1
            if (-not $tfm) {
                $rank = 100
            } elseif ($tfm -match '^(?:net|\.netcoreapp)(\d+)\.(\d+)$') {
                $maj = [int]$Matches[1]; $min = [int]$Matches[2]
                if ($maj -ge 5 -and $maj -le $tfmMajor) { $rank = 1000 + $maj * 10 + $min }
            } elseif ($tfm -match '^\.?netstandard2\.1$') { $rank = 500 }
              elseif ($tfm -match '^\.?netstandard2\.0$') { $rank = 400 }
            if ($rank -gt $bestRank) { $bestRank = $rank; $best = $g }
        }
        if ($best) { $list = @($best.dependency) }
    } else {
        $list = @($deps.dependency)
    }

    foreach ($d in $list) {
        if (-not $d -or -not $d.id -or -not $d.version) { continue }
        # Versionsbereiche wie "[10.0.11, )" auf die Untergrenze reduzieren --
        # genau das waehlt NuGet, wenn nichts Hoeheres gefordert ist.
        $dv = $d.version -replace '^\s*[\[\(]', ''
        $dv = (($dv -split ',')[0] -replace '[\]\)]\s*$', '').Trim()
        if ($dv) { $queue.Enqueue(@($d.id, $dv)) }
    }
}

Write-Host "Geprueft: $($seen.Count) Knoten, fehlen: $($missing.Count)" -ForegroundColor Cyan
foreach ($k in ($missing.Keys | Sort-Object)) {
    $p = $k -split '/'
    $lid = $p[0].ToLower(); $v = $p[1]
    "https://api.nuget.org/v3-flatcontainer/$lid/$v/$lid.$v.nupkg"
}
