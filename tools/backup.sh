#!/usr/bin/env bash
# Eternal Realms – Datenbanksicherung (Kapitel 42).
# Erstellt eine konsistente Momentaufnahme mit pg_dump (Custom-Format, komprimiert) und löscht alte Sicherungen.
# Zugangsdaten kommen NIE aus diesem Skript: libpq-Umgebungsvariablen (PGHOST, PGPORT, PGUSER, PGDATABASE)
# und das Passwort über ~/.pgpass bzw. PGPASSFILE (oder PGPASSWORD).
#
#   ER_BACKUP_DIR   Zielordner (Standard: ./backups)
#   ER_BACKUP_KEEP  Anzahl der aufbewahrten Sicherungen (Standard: 14)
#
# Beispiel (täglich um 4:17 Uhr per cron):
#   17 4 * * *  PGHOST=localhost PGUSER=realms PGDATABASE=eternal_realms ER_BACKUP_DIR=/var/backups/er /opt/er/tools/backup.sh
set -euo pipefail

dir="${ER_BACKUP_DIR:-./backups}"
keep="${ER_BACKUP_KEEP:-14}"
db="${PGDATABASE:?PGDATABASE setzen (Name der Datenbank)}"

if ! [[ "$keep" =~ ^[0-9]+$ ]] || [ "$keep" -lt 1 ]; then echo "ER_BACKUP_KEEP muss eine positive Zahl sein." >&2; exit 2; fi
mkdir -p "$dir"
stamp="$(date -u +%Y%m%d-%H%M%S)"
file="$dir/eternal-realms-$db-$stamp.dump"
tmp="$file.part"

# Erst in eine Teildatei schreiben: eine abgebrochene Sicherung sieht nie wie eine vollständige aus.
pg_dump --format=custom --compress=6 --no-owner --file="$tmp" "$db"
# Lesbarkeit prüfen (Inhaltsverzeichnis des Archivs), dann umbenennen
pg_restore --list "$tmp" > /dev/null
mv "$tmp" "$file"
(cd "$dir" && sha256sum "$(basename "$file")" > "$(basename "$file").sha256")
echo "Sicherung erstellt: $file ($(du -h "$file" | cut -f1))"

# Aufbewahrung: nur die neuesten $keep Sicherungen dieser Datenbank behalten
mapfile -t old < <(ls -1t "$dir"/eternal-realms-"$db"-*.dump 2>/dev/null | tail -n +"$((keep + 1))")
for f in "${old[@]}"; do
  rm -f -- "$f" "$f.sha256"
  echo "Alte Sicherung entfernt: $f"
done
