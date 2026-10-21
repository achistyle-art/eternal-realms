#!/usr/bin/env bash
# Eternal Realms – Release-Ordner bauen (Kapitel 56): Server, Content, Migrationen und Werkzeuge in einem Verzeichnis.
#   tools/package.sh [Zielordner]   (Standard: ./releases/<content-version>-<zeit>)
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
version="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' "$root/content/manifest.json")"
target="${1:-$root/releases/$version-$(date -u +%Y%m%d-%H%M%S)}"
[ -e "$target" ] && { echo "Ziel existiert bereits: $target" >&2; exit 2; }
dotnet publish "$root/src/EternalRealms.ServerHost" -c Release -o "$target" --nologo -v quiet
cp -r "$root/content" "$target/content"
mkdir -p "$target/db" && cp -r "$root/db/migrations" "$target/db/migrations"
mkdir -p "$target/tools" && cp "$root/tools/backup.sh" "$root/tools/restore.sh" "$root/tools/update.sh" "$target/tools/"
git -C "$root" rev-parse --short HEAD > "$target/COMMIT" 2>/dev/null || echo "unbekannt" > "$target/COMMIT"
echo "$version" > "$target/VERSION"
echo "Release gebaut: $target (Content $version, Commit $(cat "$target/COMMIT"))"
