#!/usr/bin/env bash
# Baut die gemeinsame Bibliothek und kopiert sie samt Content in das Unity-Projekt.
# (Dasselbe macht im Editor das Menü "Eternal Realms > Shared-DLL und Content kopieren".)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/build/unity-plugins"
dotnet publish "$ROOT/src/EternalRealms.Shared" -c Release -o "$OUT"
PLUGINS="$ROOT/client/Unity/Assets/Plugins/EternalRealms"
mkdir -p "$PLUGINS"
# Diese Bibliotheken bringt Unitys .NET-Standard-2.1-Profil bereits mit; doppelt führen sie zu Konflikten.
for dll in "$OUT"/*.dll; do
  case "$(basename "$dll")" in
    System.Buffers.dll|System.Memory.dll|System.Numerics.Vectors.dll|System.Threading.Tasks.Extensions.dll) ;;
    *) cp "$dll" "$PLUGINS/" ;;
  esac
done
TARGET="$ROOT/client/Unity/Assets/StreamingAssets/content"
mkdir -p "$TARGET"
cp -r "$ROOT/content/." "$TARGET/"
echo "Fertig: $(ls "$PLUGINS" | wc -l) Bibliotheken, Content nach StreamingAssets kopiert."
