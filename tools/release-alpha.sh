#!/usr/bin/env bash
# Eternal Realms – Download-Paket für die Alpha: eigenständige ZIPs (ohne .NET-Installation) für Tester und Betrieb.
#   tools/release-alpha.sh <version> [Zielordner]          z. B. tools/release-alpha.sh alpha-1
#   ER_ALPHA_SERVER=alpha.example.org:7777 tools/release-alpha.sh alpha-1   (Serveradresse im Textclient voreinstellen)
#
# Ergebnis in dist/<version>/:
#   eternal-realms-<version>-textclient-<system>.zip   Textclient für windows-x64, linux-x64, macos-arm64, macos-x64
#   eternal-realms-<version>-server-<system>.zip       Realm-Server für linux-x64 und windows-x64 (mit Content, Migrationen, Werkzeugen, Doku)
#   SHA256SUMS
set -euo pipefail
version="${1:?Version angeben, z. B. alpha-1}"
[[ "$version" =~ ^[A-Za-z0-9._-]+$ ]] || { echo "Version nur aus Buchstaben, Ziffern, . _ -" >&2; exit 2; }
root="$(cd "$(dirname "$0")/.." && pwd)"
dist="${2:-$root/dist/$version}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$dist"
server="${ER_ALPHA_SERVER:-}"
if [ -n "$server" ] && ! [[ "$server" =~ ^[A-Za-z0-9.-]+(:[0-9]{1,5})?$ ]]; then echo "ER_ALPHA_SERVER ungültig: $server" >&2; exit 2; fi
manifest="$(python3 -c 'import json,sys; m=json.load(open(sys.argv[1])); print(m.get("stage",""), m["version"])' "$root/content/manifest.json")"

publish() { # Projekt, RID, Ziel
  dotnet publish "$root/src/$1" -c Release -r "$2" --self-contained true -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o "$3" --nologo -v quiet
}

for pair in win-x64:windows-x64 linux-x64:linux-x64 osx-arm64:macos-arm64 osx-x64:macos-x64; do
  rid="${pair%%:*}"; name="${pair##*:}"
  dir="$work/textclient-$name/eternal-realms"
  publish EternalRealms.ConsoleClient "$rid" "$dir"
  cp -r "$root/content" "$dir/content"
  [ -n "$server" ] && echo "$server" > "$dir/server.txt"
  exe="EternalRealms.ConsoleClient"; [ "$rid" = win-x64 ] && exe="EternalRealms.ConsoleClient.exe"
  cat > "$dir/LIESMICH.txt" <<TXT
Eternal Realms $manifest – Textclient (Testversion)

Starten:   $exe   (Windows: Doppelklick; Linux/macOS: im Terminal ./$exe)
Server:    ${server:-wird beim Start als Argument angegeben: $exe <server> 7777}
Konto:     register <konto> <passwort> <Einladungscode>   (Passwort: mind. 8 Zeichen, Buchstaben und Ziffern)
Danach:    create <Name> <Volk> <Klasse>, play 1 – 'hilfe' zeigt alle Befehle.
Fehler:    bug <Text>   oder mit Kategorie: bug balance: <Text>
Absturz:   fehler.log liegt neben dem Programm – bitte an die Spielleitung schicken.
macOS:     beim ersten Start Rechtsklick → Öffnen (das Programm ist nicht signiert).
TXT
  (cd "$work/textclient-$name" && zip -qr "$dist/eternal-realms-$version-textclient-$name.zip" eternal-realms)
  echo "Textclient $name fertig"
done

for pair in linux-x64:linux-x64 win-x64:windows-x64; do
  rid="${pair%%:*}"; name="${pair##*:}"
  dir="$work/server-$name/eternal-realms-server"
  publish EternalRealms.ServerHost "$rid" "$dir"
  cp -r "$root/content" "$dir/content"
  mkdir -p "$dir/db" "$dir/tools" "$dir/docs"
  cp -r "$root/db/migrations" "$dir/db/migrations"
  cp "$root/tools/backup.sh" "$root/tools/restore.sh" "$root/tools/update.sh" "$root/tools/alpha-wipe.sh" "$dir/tools/"
  cp "$root/docs/ALPHA.md" "$root/docs/BETRIEB.md" "$root/docs/UPDATES.md" "$root/docs/DATENMODELL.md" "$dir/docs/"
  cat > "$dir/LIESMICH.txt" <<TXT
Eternal Realms $manifest – Realm-Server

Schnellstart ohne Datenbank (Daten nur im Arbeitsspeicher, zum Ausprobieren):
  ER_CONSOLE=true ./EternalRealms.ServerHost        (Windows: EternalRealms.ServerHost.exe)
Alpha-Betrieb mit PostgreSQL, Einladungscodes und Sicherungen: docs/ALPHA.md
Der Server lauscht auf UDP 7777 (ER_PORT).
TXT
  (cd "$work/server-$name" && zip -qr "$dist/eternal-realms-$version-server-$name.zip" eternal-realms-server)
  echo "Server $name fertig"
done

(cd "$dist" && sha256sum ./*.zip | sed 's| \./| |' > SHA256SUMS)
echo "Download-Paket $version: $dist"
ls -lh "$dist"
