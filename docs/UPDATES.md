# Updates und Rollback

Patches (Kapitel 56): wie ein neuer Stand sicher auf einen laufenden Realm kommt und wie man zurückkehrt, wenn etwas schiefgeht.

## Versionen

Was einen Stand ausmacht und wie er geprüft wird:

| Was | Wo | Wird geprüft durch |
|---|---|---|
| **Serverprogramm** | Release-Ordner (`tools/package.sh`) | – |
| **Datenbankschema** | `db/migrations/NNN_name.sql`, nur vorwärts, in `schema_migrations` vermerkt | Server beim Start |
| **Content** | `content/manifest.json` (Versionsnummer) + Prüfsumme aller Dateien, z. B. `0.9.0+9dc11753` | Client beim Anmelden |
| **Gespeicherte Daten** | `content/migrations.json` (umbenannte/entfernte IDs) | Server beim Laden jedes Charakters |

- **Content-Version:** Die Prüfsumme ignoriert Zeilenenden. Windows und Linux, Server und Unity-Client (StreamingAssets) kommen deshalb auf denselben Wert, wenn sie denselben Stand haben. Weicht der Client ab, zeigt er beim Anmelden eine Warnung.
- **Realm-Informationen:** In `realm_info` stehen die zuletzt gestartete Content- und Serverversion, die Betriebsart und die Startzeit. Ändert sich der Content, protokolliert der Server `Content-Update: alt → neu`.

## Inhalte ändern, ohne Spielstände zu beschädigen

Gespeicherte Charaktere, Briefe und Auktionen verweisen auf IDs: Gegenstände, Quests, Fähigkeiten, Erfolge und Regionen. Wird eine ID umbenannt oder entfernt, braucht es eine Content-Migration:

```json
[
 { "id": "20261021-umbenannt-item_wolf_fang", "description": "item item_wolf_fang → item_waldwolf_fang",
   "renameItems": { "item_wolf_fang": "item_waldwolf_fang" },
   "removeItems": { "item_alte_muenze": 25 },
   "renameQuests": {}, "removeQuests": [], "renameAbilities": {}, "removeAbilities": [], "renameAchievements": {}, "renameZones": {} }
]
```

Umbenennen erledigt das Content-Werkzeug vollständig (siehe [Inhalte erstellen](CONTENT.md#content-werkzeug)).

Was der Server mit gespeicherten Daten macht:

- **Charaktere:** Jede Migration wird genau einmal je Charakter angewendet; der Charakter merkt sich die angewendeten IDs.
  - Entfernte Gegenstände werden mit dem angegebenen Kupferbetrag je Stück erstattet und im Geldbuch als `content_refund` gebucht. Der Spieler erhält einen Hinweis.
- **Post:** Anhänge werden umbenannt; entfernte werden als Gold beigelegt.
- **Auktionen:** Auktionen entfernter Gegenstände werden aufgelöst. Der Verkäufer erhält die Erstattung per Brief, der Höchstbietende sein Gebot zurück – alles in einer Transaktion.
- **Fehlende Definition ohne Migration:** Steht ein Gegenstand weder im Content noch in einer Migration, wird er **verwahrt** statt gelöscht. Kommt die Definition zurück (Rollback des Contents), legt der Server ihn beim nächsten Laden wieder in die Tasche.

Ein Patch kann so nie Gegenstände oder Gold verdoppeln oder vernichten.

## Prüflauf vor dem Update

```bash
ER_DATABASE="…" ER_CONTENT=release/content ER_MIGRATIONS=release/db/migrations \
  dotnet release/EternalRealms.ServerHost.dll --check
```

Der Prüflauf ändert nichts. Er prüft:

- den Content (Gültigkeit, Version, Migrationen);
- den Datenbankstand (angewendete und ausstehende Migrationen sowie unbekannte, also neuere Migrationen);
- die gespeicherten Daten im Probelauf: wie viele Charaktere beim nächsten Laden angepasst würden und was mit ihnen geschieht.

Exitcode 0 heißt bestanden. Dieselbe Prüfung gibt es im Spiel für Admins als `.pruefen` und an der Serverkonsole als `pruefen`.

## Update mit automatischem Rückweg

```bash
tools/package.sh /opt/eternal-realms/releases/0.9.1      # Release-Ordner bauen (Server, Content, Migrationen, Werkzeuge)

export ER_CURRENT=/opt/eternal-realms/current            # Verweis auf den laufenden Release
export ER_STOP_CMD="systemctl stop eternal-realms" ER_START_CMD="systemctl start eternal-realms"
export ER_STATUS_FILE=/run/eternal-realms/status         # muss auch im Dienst gesetzt sein
export PGHOST=localhost PGUSER=realms PGDATABASE=eternal_realms ER_BACKUP_DIR=/var/backups/er   # Passwort über ~/.pgpass
export ER_DATABASE="Host=localhost;Username=realms;Database=eternal_realms"                    # für den Prüflauf
tools/update.sh /opt/eternal-realms/releases/0.9.1
```

1. **Prüflauf** des neuen Releases (`--check`). Schlägt er fehl, bleibt alles unverändert.
2. **Datenbanksicherung** mit `tools/backup.sh`.
3. **Server stoppen.** Er speichert alle Charaktere und schreibt `stopped` in die Statusdatei.
4. **Release umstellen:** Der Verweis `current` zeigt auf den neuen Ordner.
5. **Starten** und warten, bis der Server `running <content-version>` meldet. Ein abgebrochener Start meldet `failed <Grund>`; dann wird sofort zurückgerollt.
6. **Bei Fehlschlag:**
   - Den alten Release starten.
   - Hat das Update bereits Datenbankmigrationen angewendet, vorher die Sicherung von Schritt 2 in eine neu angelegte Datenbank zurückspielen. So bleibt nichts vom fehlgeschlagenen Stand übrig.

Exitcodes:

| Code | Bedeutung |
|---|---|
| 0 | Update erfolgreich |
| 3 | Prüflauf fehlgeschlagen (nichts geändert) |
| 6 | Rollback erfolgreich |
| 7 | Rollback gestartet, alter Release meldet sich nicht |

**Hier ausgeführt** mit echten Releases, einer eigenen Datenbank und einem gespeicherten Charakter:

- **Kaputte Migration:** Rollback ohne Wiederherstellung (die Migration lief in ihrer Transaktion zurück).
- **Migration angewendet, nächste kaputt:** Rollback mit Wiederherstellung. Die Tabelle aus Migration 008 ist danach verschwunden, das Konto erhalten.
- **Gültiges Update mit neuer Migration und umbenanntem Gegenstand:** Erfolg nach 5 s. Der Gegenstand des Charakters trägt beim nächsten Einloggen die neue ID.

## Manueller Rollback

- **Schutz vor einer neueren Datenbank:** Ein Server startet nicht auf einer Datenbank, die Migrationen enthält, die er nicht kennt. Wer nur das Programm zurücksetzt, bekommt eine klare Meldung statt beschädigter Daten.
- **Zurück auf den alten Stand:** Sicherung zurückspielen (`ER_RESTORE_RECREATE=true tools/restore.sh <datei> <datenbank>`) und den alten Release starten.
- **Nur Content zurücksetzen** (ohne neue Datenbankmigrationen): Den alten Release zu starten genügt. Verwahrte Gegenstände kehren automatisch zurück; bereits umbenannte bleiben unter der neuen ID. Deshalb im alten Stand eine passende Rück-Migration ergänzen oder die Sicherung verwenden.

## Empfehlungen

- Vor jedem Update `.wartung an Update in 10 Minuten` ansagen. Nach dem Update kurz mit einem Spielleiter-Konto prüfen (`.version`, `.pruefen`), dann `.wartung aus`.
- Entfernte Gegenstände immer mit Erstattung in `removeItems` eintragen. Nie einfach aus dem Content löschen: Sie wären sonst verwahrt, nicht erstattet.
- Content-Migrationen nie löschen. Spieler, die lange nicht online waren, brauchen sie noch.
