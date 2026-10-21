#!/usr/bin/env bash
# Eternal Realms – Sicherung zurückspielen (Kapitel 42).
#   tools/restore.sh <sicherung.dump> <zieldatenbank>
# Die Zieldatenbank wird angelegt, falls sie fehlt. Ist sie nicht leer, bricht das Skript ab –
# außer mit ER_RESTORE_OVERWRITE=true (dann werden vorhandene Objekte ersetzt) oder ER_RESTORE_RECREATE=true
# (Datenbank wird gelöscht und neu angelegt). Server vorher stoppen!
# Zugangsdaten wie bei backup.sh über libpq-Umgebungsvariablen bzw. ~/.pgpass.
set -euo pipefail

file="${1:?Aufruf: restore.sh <sicherung.dump> <zieldatenbank>}"
target="${2:?Aufruf: restore.sh <sicherung.dump> <zieldatenbank>}"
[ -f "$file" ] || { echo "Datei nicht gefunden: $file" >&2; exit 2; }
if [ -f "$file.sha256" ]; then
  (cd "$(dirname "$file")" && sha256sum --check --quiet "$(basename "$file").sha256") || { echo "Prüfsumme stimmt nicht – Sicherung beschädigt." >&2; exit 3; }
fi
pg_restore --list "$file" > /dev/null

# ER_RESTORE_RECREATE=true: Zieldatenbank vollständig neu anlegen (Rollback – nichts vom fehlgeschlagenen Update bleibt übrig).
if [ "${ER_RESTORE_RECREATE:-false}" = "true" ]; then
  dropdb --if-exists "$target"
  echo "Datenbank $target entfernt (wird neu angelegt)."
fi
if ! psql -d postgres -Atc "SELECT 1 FROM pg_database WHERE datname = '$(printf "%s" "$target" | sed "s/'/''/g")'" | grep -q 1; then
  createdb "$target"
  echo "Datenbank $target angelegt."
fi
tables="$(psql -d "$target" -Atc "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'")"
if [ "$tables" != "0" ] && [ "${ER_RESTORE_OVERWRITE:-false}" != "true" ]; then
  echo "Zieldatenbank $target enthält bereits $tables Tabellen. Abbruch (ER_RESTORE_OVERWRITE=true ersetzt sie)." >&2
  exit 4
fi
pg_restore --no-owner --exit-on-error $([ "$tables" != "0" ] && echo "--clean --if-exists") --dbname="$target" "$file"
echo "Wiederhergestellt: $file → $target"
psql -d "$target" -Atc "SELECT 'Konten: ' || count(*) FROM accounts UNION ALL SELECT 'Charaktere: ' || count(*) FROM characters UNION ALL SELECT 'Migrationen: ' || string_agg(version, ', ' ORDER BY version) FROM schema_migrations"
