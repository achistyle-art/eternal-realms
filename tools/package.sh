#!/usr/bin/env bash
# Eternal Realms – Release-Ordner bauen (Kapitel 56): Server, Content, Migrationen, Werkzeuge, Textclient und Alpha-Doku.
#   tools/package.sh [Zielordner]   (Standard: ./releases/<content-version>-<zeit>)
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
version="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' "$root/content/manifest.json")"
target="${1:-$root/releases/$version-$(date -u +%Y%m%d-%H%M%S)}"
[ -e "$target" ] && { echo "Ziel existiert bereits: $target" >&2; exit 2; }
dotnet publish "$root/src/EternalRealms.ServerHost" -c Release -o "$target" --nologo -v quiet
cp -r "$root/content" "$target/content"
mkdir -p "$target/db" && cp -r "$root/db/migrations" "$target/db/migrations"
mkdir -p "$target/tools" && cp "$root/tools/backup.sh" "$root/tools/restore.sh" "$root/tools/update.sh" "$root/tools/alpha-wipe.sh" "$target/tools/"
# Textclient für Tester ohne Unity-Build (gleiches Protokoll, eigener Content-Ordner)
dotnet publish "$root/src/EternalRealms.ConsoleClient" -c Release -o "$target/client-konsole" --nologo -v quiet
cp -r "$root/content" "$target/client-konsole/content"
mkdir -p "$target/docs" && cp "$root/docs/ALPHA.md" "$root/docs/BETRIEB.md" "$root/docs/UPDATES.md" "$target/docs/"
git -C "$root" rev-parse --short HEAD > "$target/COMMIT" 2>/dev/null || echo "unbekannt" > "$target/COMMIT"
echo "$version" > "$target/VERSION"
echo "Release gebaut: $target (Content $version, Commit $(cat "$target/COMMIT"))"
