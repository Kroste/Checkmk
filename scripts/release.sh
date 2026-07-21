#!/usr/bin/env bash
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
set -euo pipefail

# MinVer-Projekt: Version aus letztem Tag ableiten und Bump erfragen.
LAST=$(git describe --tags --abbrev=0 --match 'v*' 2>/dev/null || echo v0.0.0)
LAST=${LAST#v}
IFS=. read -r MA MI PA <<< "$LAST"
SUGGEST="${MA}.${MI}.$((PA+1))"
read -r -p "Neue Version [${SUGGEST}]: " VERSION
VERSION=${VERSION:-$SUGGEST}

if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "FEHLER: '$VERSION' ist keine gueltige SemVer-Version (X.Y.Z)." >&2
  exit 1
fi
TAG="v${VERSION}"

if [ -n "$(git status --porcelain)" ]; then
  echo "FEHLER: Es gibt uncommittete Aenderungen. Erst committen." >&2
  exit 1
fi
if [ -n "$(git log --branches --not --remotes --oneline 2>/dev/null)" ]; then
  echo "FEHLER: Es gibt ungepushte Commits. Erst pushen." >&2
  exit 1
fi
if [ ! -f "RELEASE_NOTES/${TAG}.md" ]; then
  echo "WARNUNG: RELEASE_NOTES/${TAG}.md fehlt - das Release nimmt die Tag-Message als Body." >&2
fi
if git rev-parse "$TAG" >/dev/null 2>&1; then
  read -r -p "Tag $TAG existiert bereits. Loeschen und neu setzen? [j/N] " answer
  if [ "${answer,,}" != "j" ]; then
    echo "Abgebrochen."
    exit 0
  fi
  git tag -d "$TAG"
  git push origin ":refs/tags/$TAG" || true
fi

git tag -a "$TAG" -m "Release $TAG"
git push origin "$TAG"
echo "Tag $TAG gepusht - die Release-Action baut jetzt die Windows-ZIP."
