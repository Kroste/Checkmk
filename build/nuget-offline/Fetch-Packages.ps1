<#
.SYNOPSIS
    Lädt .nupkg-Dateien ins lokale Offline-Bundle.

.DESCRIPTION
    Auf einer Maschine ausführen, die .nupkg von nuget.org laden darf — im
    Fachbereichsnetz blockt der Proxy das mit HTTP 403.

    Nimmt URLs über die Pipeline (etwa direkt aus Resolve-MissingPackages.ps1),
    als Parameter oder aus einer Datei. Vorhandene Dateien werden übersprungen,
    ein Fehlschlag hinterlässt keine halbe Datei.

.EXAMPLE
    .\Resolve-MissingPackages.ps1 -Roots "Foo:1.2.3" | .\Fetch-Packages.ps1

.EXAMPLE
    Get-Content urls.txt | .\Fetch-Packages.ps1 -Target D:\Bundle
#>
[CmdletBinding()]
param(
    [Parameter(ValueFromPipeline)]
    [string[]]$Url,

    [string]$Target = 'C:\NuGet-Local'
)

begin {
    New-Item -ItemType Directory -Force -Path $Target | Out-Null
    $ok = 0; $skip = 0; $fail = @()
}

process {
    foreach ($u in $Url) {
        if ([string]::IsNullOrWhiteSpace($u)) { continue }
        $u = $u.Trim()
        if (-not $u.StartsWith('http')) { continue }   # Kopfzeilen ueberspringen

        $file = Join-Path $Target ([System.IO.Path]::GetFileName($u))
        if (Test-Path $file) { $skip++; continue }

        # -f laesst curl bei HTTP-Fehlern scheitern, statt die Fehlerseite in
        # die .nupkg zu schreiben -- sonst liegt eine kaputte Datei im Bundle
        # und der Restore meldet etwas voellig Irrefuehrendes.
        & curl.exe -s -S -f -o $file $u
        if ($LASTEXITCODE -eq 0 -and (Test-Path $file)) {
            $ok++
        } else {
            $fail += $u
            Remove-Item $file -ErrorAction SilentlyContinue
        }
    }
}

end {
    Write-Host "geladen: $ok, schon da: $skip, fehlgeschlagen: $($fail.Count)" -ForegroundColor Cyan
    $fail | ForEach-Object { Write-Warning "FEHLER: $_" }
    if ($ok -gt 0) {
        Write-Host "Danach: dotnet build -- der Restore ist der Schiedsrichter, nicht dieses Skript." -ForegroundColor Yellow
    }
}
