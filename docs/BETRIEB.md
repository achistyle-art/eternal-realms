# Betrieb eines Realms

Kontoschutz, Wiederverbindung, Spielleitung, Anti-Cheat und Datensicherung (Kapitel 40–42, 49, 50, 54).

## Erste Spielleitung einrichten

Rechte werden nie im Code oder in der Konfiguration hinterlegt. Zwei Wege:

```bash
# 1. Serverkonsole (ER_CONSOLE=true), Befehle ohne Punkt:
rechte meinkonto 2

# 2. direkt in der Datenbank:
psql -d eternal_realms -c "UPDATE accounts SET gm_level = 2 WHERE lower(username) = 'meinkonto'"
```

Stufen: 0 = Spieler, 1 = Spielleiter, 2 = Admin. Die Rechte gelten sofort, auch für Charaktere, die bereits in der Welt sind.

## Befehle der Spielleitung

Im Spiel als Chat mit Punkt (`.kick Name Grund`), im Konsolenclient mit `gm kick Name Grund`, an der Serverkonsole ohne Punkt.

| Befehl | Stufe | Wirkung |
|---|---|---|
| `.hilfe` | 1 | Übersicht der erlaubten Befehle |
| `.wer [Filter]` | 1 | Spieler online (Name oder Region), Warteschlange, verbindungslose Charaktere |
| `.info Name` | 1 | Charakter und Konto: Ort, Stufe, Gold, Anti-Cheat-Punkte, Adresse, Sperren, 2FA |
| `.ansage Text` | 1 | Nachricht an alle |
| `.kick Name [Grund]` | 1 | trennen (sofort abmelden, kein automatisches Wiederverbinden) |
| `.stumm Name Minuten [Grund]` / `.laut Name` | 1 | Chat sperren / freigeben (am Konto gespeichert) |
| `.bann Name Minuten\|dauerhaft Grund` / `.entbann Name` | 1 | Konto sperren / entsperren (Name = Charakter oder Konto); Entsperren hebt auch eine Fehlversuchssperre auf |
| `.gehezu Name`, `.hol Name`, `.tele Zone [x z]` | 1 | Reisen |
| `.auffaellig` | 1 | letzte Meldungen der Anti-Cheat-Erkennung |
| `.meldungen`, `.erledigt Nummer` | 1 | offene Spielermeldungen (mit den letzten Chatzeilen als Beleg) |
| `.protokoll [Aktion\|Name]` | 1 | Prüfprotokoll, z. B. `.protokoll gm.ban` oder `.protokoll Laermer` |
| `.wartung an [Text]`, `.wartung leeren`, `.wartung aus` | 1 | Wartungsmodus: nur Spielleiter kommen herein; „leeren“ speichert und trennt alle übrigen |
| `.speichern` | 1 | alle Charaktere speichern |
| `.gold Name Gold`, `.gegenstand Name Id [Anzahl]` | 2 | Gutschriften (im Geldbuch als „gm“ gebucht) |
| `.rechte Konto 0\|1\|2` | 2 | Spielleiter-Stufe setzen |

Grundsätze:

- Niemand kann gegen Gleich- oder Höhergestellte vorgehen (die Konsole ausgenommen).
- Jede verändernde Handlung wird mit Handelndem, Ziel, Detail und Adresse in `audit_log` geschrieben.
- Ebenfalls protokolliert werden Anmeldungen (`login.ok`, `login.token`, `login.fail`), Kontosperren (`account.locked`), Sicherheitsänderungen (`account.password`, `account.2fa.on/off`) und Anti-Cheat-Meldungen (`anticheat.flag`, `anticheat.kick`).

## Anti-Cheat

Der Server entscheidet ohnehin alles selbst: Er begrenzt Bewegung und lehnt unmögliche Aktionen ab. Die Erkennung bewertet nur, wie auffällig jemand ist:

| Auffälligkeit | Punkte |
|---|---|
| Bewegung mehr als doppelt so weit wie möglich | 4 je Paket |
| Nachrichtenflut (über 80 Nachrichten/s) | 15 |
| Fähigkeit, die der Charakter nicht kennt | 3 |
| neu entstandenes Gold über 40 Gold pro Stunde und Stufe | 40 (höchstens alle 10 min) |

- Punkte klingen mit 1 pro Sekunde ab, deshalb fallen einzelne Ruckler durch Verzögerung nicht ins Gewicht.
- Ab `ER_CHEAT_FLAG_SCORE` (50) werden alle Spielleiter benachrichtigt, und es entsteht ein Protokolleintrag.
- Ab `ER_CHEAT_KICK_SCORE` (150) trennt der Server die Verbindung.
- Eine Sperre spricht immer ein Mensch aus.

## Kontoschutz

- **Fehlversuche:** nach 5 falschen Anmeldungen wird das Konto 15 Minuten gesperrt; das wird gespeichert und übersteht Neustarts. Pro IP-Adresse gelten höchstens 20 Fehlversuche bzw. 5 neue Konten pro Stunde.
- **Passwörter:** PBKDF2-SHA256 mit 100 000 Durchläufen. Neue Passwörter brauchen mindestens 8 Zeichen, Buchstaben und Ziffern und dürfen den Kontonamen nicht enthalten.
- **Zwei-Faktor-Anmeldung:**
  - Einrichten über „Kontoschutz“ auf der Charakterauswahl, im Konsolenclient mit `2fa an` und danach `2fa bestätigen <Code>`.
  - Funktioniert mit jeder TOTP-App (30 s, 6 Ziffern). Jeder Code gilt nur einmal.
  - Bei verlorenem Gerät setzt ein Admin `totp_secret` in der Datenbank auf NULL.
- **Wiederverbindungstokens:**
  - Nach jeder Anmeldung erhält der Client ein einmaliges Token, das er nur im Speicher hält. Der Server speichert nur dessen SHA-256-Hash.
  - Das Token ersetzt bei der Wiederverbindung Passwort und Code.
  - Es verfällt nach `ER_SESSION_TOKEN_HOURS` und bei jeder Passwortänderung.
- **Noch offen:** Die Verbindung ist unverschlüsselt. Vor einem öffentlichen Betrieb gehört die Anmeldung hinter ein TLS-Gateway (Meilenstein 5).

## Verbindungsabbrüche und volle Realms

- **Verbindungsabbruch:**
  - Bricht die Verbindung ab, bleibt der Charakter `ER_LINKDEAD_SECONDS` (60 s) in der Welt, auch im Kampf. Ein Abbruch ist also keine Flucht.
  - Der Client verbindet sich selbst neu, bis zu 8 Versuche in etwa einer Minute.
  - Er bekommt denselben Charakter aus dem Speicher zurück: kein Neuladen, kein Doppelstand.
- **Zweite Anmeldung** des Kontos von woanders: Sie übernimmt den Charakter, die alte Verbindung wird getrennt.
- **Voller Realm:** Ist er voll (`ER_MAX_PLAYERS`), kommen Spieler in eine Warteschlange und sehen ihren Platz. Spielleiter sind ausgenommen.

## Datensicherung

Zugangsdaten kommen aus den üblichen PostgreSQL-Umgebungsvariablen (`PGHOST`, `PGUSER`, `PGDATABASE`) und `~/.pgpass`, nie aus den Skripten.

```bash
# Sicherung (Custom-Format, komprimiert, mit .sha256-Prüfsumme); behält die neuesten ER_BACKUP_KEEP (14)
PGHOST=localhost PGUSER=realms PGDATABASE=eternal_realms ER_BACKUP_DIR=/var/backups/er tools/backup.sh

# täglich per cron
17 4 * * *  PGHOST=localhost PGUSER=realms PGDATABASE=eternal_realms ER_BACKUP_DIR=/var/backups/er /opt/er/tools/backup.sh

# Wiederherstellen (Server vorher stoppen); legt die Zieldatenbank bei Bedarf an
tools/restore.sh /var/backups/er/eternal-realms-eternal_realms-20261021-041700.dump eternal_realms_restore
# belegte Zieldatenbank ersetzen
ER_RESTORE_OVERWRITE=true tools/restore.sh <datei> eternal_realms
```

Der Ablauf schützt vor halben oder beschädigten Sicherungen:

- `pg_dump` liefert eine in sich stimmige Momentaufnahme, auch während der Server läuft.
- Die Datei entsteht zuerst als `.part`. Erst nachdem ihr Inhaltsverzeichnis lesbar ist, wird sie umbenannt; eine abgebrochene Sicherung sieht deshalb nie vollständig aus.
- `restore.sh` prüft die Prüfsumme, bricht bei einer belegten Zieldatenbank ab und zeigt danach Konten, Charaktere und Migrationsstand.
- Fehlende Migrationen wendet der Server beim nächsten Start selbst an.
- Empfehlung: Sicherungen zusätzlich auf einen anderen Rechner kopieren und die Wiederherstellung regelmäßig in eine Testdatenbank üben.
