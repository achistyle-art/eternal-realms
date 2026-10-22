#!/usr/bin/env bash
# Eternal Realms – Alpha-Wipe: alle Spielstände zurücksetzen (zwischen zwei Alpha-Phasen).
# Vorher wird IMMER eine Sicherung mit tools/backup.sh erstellt; zurück geht es mit tools/restore.sh.
#
# Gelöscht werden: Charaktere (inkl. KI-Mitspieler, Taschen, Quests, Ruf, Freunde), Gilden, Post, Auktionen,
# Geldbuch und Wiederkehr-Zeiten der Weltbosse. Erhalten bleiben: Konten (Einladungscodes bleiben eingelöst),
# Spielermeldungen, Fehlermeldungen, Prüfprotokoll und Realm-Informationen.
#
#   PGHOST, PGPORT, PGUSER, PGDATABASE   Verbindung (Passwort über ~/.pgpass oder PGPASSWORD – nie in diesem Skript)
#   ER_WIPE_CONFIRM      muss genau dem Datenbanknamen entsprechen (Schutz vor Versehen)
#   ER_WIPE_ACCOUNTS     true = auch Spielerkonten löschen (Spielleiter-Konten bleiben)
#   ER_STATUS_FILE       Statusdatei des Servers; steht dort „running“, bricht das Skript ab
#   ER_BACKUP_DIR        Zielordner der Sicherung (siehe backup.sh)
#
# Beispiel:
#   systemctl stop eternal-realms
#   PGHOST=localhost PGUSER=realms PGDATABASE=eternal_realms ER_WIPE_CONFIRM=eternal_realms tools/alpha-wipe.sh
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
db="${PGDATABASE:?PGDATABASE setzen (Name der Datenbank)}"

if [ "${ER_WIPE_CONFIRM:-}" != "$db" ]; then
  echo "Abbruch: Zur Bestätigung ER_WIPE_CONFIRM=$db setzen. Es werden ALLE Charaktere gelöscht." >&2
  exit 2
fi
# Ein laufender Server würde seine Charaktere beim nächsten Speichern zurückschreiben.
if [ -n "${ER_STATUS_FILE:-}" ] && [ -f "$ER_STATUS_FILE" ] && grep -q '^running' "$ER_STATUS_FILE"; then
  echo "Abbruch: Der Server läuft noch ($ER_STATUS_FILE). Erst stoppen." >&2
  exit 3
fi

echo "1/2 Sicherung …"
"$here/backup.sh"

accounts_sql=""
if [ "${ER_WIPE_ACCOUNTS:-false}" = "true" ]; then
  accounts_sql="DELETE FROM accounts WHERE gm_level = 0;"
fi

echo "2/2 Spielstände löschen …"
psql --no-psqlrc -v ON_ERROR_STOP=1 --single-transaction --quiet "$db" <<SQL
TRUNCATE characters, guilds, mail, auctions, economy_log, spawn_timers RESTART IDENTITY CASCADE;
DELETE FROM session_tokens;
$accounts_sql
INSERT INTO audit_log (actor, action, detail) VALUES ('alpha-wipe.sh', 'admin.wipe', 'Konten gelöscht: ${ER_WIPE_ACCOUNTS:-false}');
SQL

left="$(psql --no-psqlrc -tA "$db" -c 'SELECT count(*) FROM characters')"
echo "Fertig: $left Charaktere übrig, Konten $( [ "${ER_WIPE_ACCOUNTS:-false}" = "true" ] && echo "der Spieler gelöscht" || echo "erhalten" ). Server kann neu starten."
