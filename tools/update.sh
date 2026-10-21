#!/usr/bin/env bash
# Eternal Realms – Update mit automatischem Rückweg (Kapitel 56).
#   tools/update.sh <neuer-release-ordner>
#
# Ablauf: Prüflauf des neuen Releases (--check) → Datenbanksicherung → Server stoppen → Verweis „current“ umstellen →
# starten → warten, bis der Server „running“ meldet. Scheitert der Start, wird automatisch zurückgerollt: alter Release,
# und – falls das Update Datenbankmigrationen angewendet hat – die Sicherung von eben zurückgespielt.
#
# Umgebung (Zugangsdaten nie im Skript):
#   ER_CURRENT        Verweis auf den laufenden Release (Standard /opt/eternal-realms/current)
#   ER_STOP_CMD       Befehl zum Stoppen, z. B. "systemctl stop eternal-realms"
#   ER_START_CMD      Befehl zum Starten, z. B. "systemctl start eternal-realms"
#   ER_STATUS_FILE    Statusdatei des Servers (gleicher Wert wie im Dienst)
#   ER_DATABASE       Npgsql-Verbindung für den Prüflauf; PGHOST/PGUSER/PGDATABASE (+ ~/.pgpass) für Sicherung/Wiederherstellung
#   ER_BACKUP_DIR     Ordner der Sicherungen (Standard ./backups)
#   ER_START_TIMEOUT  Sekunden bis „running“ (Standard 120)
set -euo pipefail

new="$(cd "${1:?Aufruf: update.sh <neuer-release-ordner>}" && pwd)"
current="${ER_CURRENT:-/opt/eternal-realms/current}"
status="${ER_STATUS_FILE:?ER_STATUS_FILE setzen (wie beim Serverdienst)}"
stop_cmd="${ER_STOP_CMD:?ER_STOP_CMD setzen}"
start_cmd="${ER_START_CMD:?ER_START_CMD setzen}"
timeout="${ER_START_TIMEOUT:-120}"
db="${PGDATABASE:?PGDATABASE setzen}"
here="$(cd "$(dirname "$0")" && pwd)"
log() { echo "[$(date -u +%H:%M:%S)] $*"; }

for f in EternalRealms.ServerHost.dll content/manifest.json db/migrations; do
  [ -e "$new/$f" ] || { echo "Kein gültiger Release (fehlt: $f): $new" >&2; exit 2; }
done
previous="$(readlink -f "$current" 2>/dev/null || true)"
[ "$previous" = "$new" ] && { echo "Dieser Release läuft bereits." >&2; exit 2; }

log "1/6 Prüflauf des neuen Releases"
ER_CONTENT="$new/content" ER_MIGRATIONS="$new/db/migrations" dotnet "$new/EternalRealms.ServerHost.dll" --check \
  || { echo "Prüflauf fehlgeschlagen – nichts wurde verändert." >&2; exit 3; }

log "2/6 Datenbanksicherung"
backup_out="$("$here/backup.sh")"; echo "$backup_out"
backup="$(echo "$backup_out" | sed -n 's/^Sicherung erstellt: \(.*\.dump\) (.*/\1/p' | head -1)"
[ -f "$backup" ] || { echo "Sicherung nicht gefunden – Abbruch." >&2; exit 4; }
migrations_before="$(psql -d "$db" -Atc "SELECT string_agg(version, ',' ORDER BY version) FROM schema_migrations")"

wait_status() { # $1 = erwartetes Wort, $2 = Sekunden; „failed“ (Start abgebrochen) beendet das Warten sofort
  for _ in $(seq 1 "$2"); do
    grep -q "^$1" "$status" 2>/dev/null && return 0
    grep -q "^failed" "$status" 2>/dev/null && { log "Server meldet: $(cat "$status")"; return 1; }
    sleep 1
  done
  return 1
}

log "3/6 Server stoppen"
eval "$stop_cmd"
wait_status stopped 60 || log "Warnung: kein „stopped“ in $status – fahre fort"

log "4/6 Release umstellen: ${previous:-keiner} → $new"
ln -sfn "$new" "$current"

log "5/6 Server starten"
: > "$status"
eval "$start_cmd"
if wait_status running "$timeout"; then
  log "6/6 Update erfolgreich: $(cat "$status")"
  exit 0
fi

log "Start fehlgeschlagen – Rollback"
eval "$stop_cmd" || true
sleep 2
if [ -n "$previous" ]; then ln -sfn "$previous" "$current"; else rm -f "$current"; fi
migrations_after="$(psql -d "$db" -Atc "SELECT string_agg(version, ',' ORDER BY version) FROM schema_migrations")"
if [ "$migrations_after" != "$migrations_before" ]; then
  log "Das Update hat Migrationen angewendet ($migrations_after) – Sicherung wird zurückgespielt"
  ER_RESTORE_RECREATE=true "$here/restore.sh" "$backup" "$db"
fi
[ -n "$previous" ] || { echo "Kein vorheriger Release – Server bleibt aus." >&2; exit 5; }
: > "$status"
eval "$start_cmd"
if wait_status running "$timeout"; then log "Rollback erfolgreich: $(cat "$status")"; exit 6; fi
echo "Rollback gestartet, aber der alte Release meldet sich nicht – bitte manuell prüfen." >&2
exit 7
