# Kroste-Release (Checkmk Cockpit): prueft den Git-Zustand, erstellt einen
# annotierten Tag vX.Y.Z und pusht ihn. Der Push loest die Release-Action
# (.github/workflows/release.yml) aus, die die self-contained Windows-Single-File-EXE
# baut, als Checkmk-X.Y.Z-win-x64.zip verpackt und an das GitHub-Release haengt.
#
# Windows-only: es entsteht KEIN Linux-Build und KEIN AppImage.
# Autoupdate: der In-App-UpdateChecker liest /releases/latest und zieht das
# .zip-Asset — dieses Release bedient ihn also automatisch.
# Versionierung via MinVer (Tag-Prefix v); kein <Version> im Projekt, daher
# schlaegt das Skript "letzter Tag + 1" vor.
# Release-Notes: optional RELEASE_NOTES/vX.Y.Z.md vor dem Tag committen+pushen.
#
# Pure ASCII wegen Windows PowerShell 5.1 ANSI-Decoding; Aufruf bevorzugt via pwsh.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

# MinVer-Projekt: Version aus letztem Tag ableiten und Bump erfragen.
$last = git describe --tags --abbrev=0 --match 'v*' 2>$null
if (-not $last) { $last = 'v0.0.0' }
$parts = $last.TrimStart('v').Split('.')
$suggest = '{0}.{1}.{2}' -f $parts[0], $parts[1], ([int]$parts[2] + 1)
$version = Read-Host "Neue Version [$suggest]"
if (-not $version) { $version = $suggest }

if ($version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Error "'$version' ist keine gueltige SemVer-Version (X.Y.Z)."
    exit 1
}
$tag = "v$version"

if (git status --porcelain) {
    Write-Error 'Es gibt uncommittete Aenderungen. Erst committen.'
    exit 1
}
if (git log --branches --not --remotes --oneline) {
    Write-Error 'Es gibt ungepushte Commits. Erst pushen.'
    exit 1
}
if (-not (Test-Path "RELEASE_NOTES/$tag.md")) {
    Write-Warning "RELEASE_NOTES/$tag.md fehlt - das Release nimmt die Tag-Message als Body."
}

git rev-parse $tag 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
    $answer = Read-Host "Tag $tag existiert bereits. Loeschen und neu setzen? [j/N]"
    if ($answer -ne 'j') {
        Write-Host 'Abgebrochen.'
        exit 0
    }
    git tag -d $tag
    git push origin ":refs/tags/$tag"
}

git tag -a $tag -m "Release $tag"
git push origin $tag
Write-Host "Tag $tag gepusht - die Release-Action baut jetzt die Windows-ZIP."
