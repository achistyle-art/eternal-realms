# Datenmodell

PostgreSQL-Schema (Kapitel 53), erzeugt durch `db/migrations/001–007`. Die Migrationen laufen beim Start automatisch, nur vorwärts und unter einer Datenbanksperre. Ohne `ER_DATABASE` nutzt der Server einen Speicher im Arbeitsspeicher mit derselben Schnittstelle (Tests, Ausprobieren).

## Grundsätze

- **Serverautorität:** Die Datenbank kennt nur Zustände, die der Server erzeugt hat. Clients schreiben nie direkt.
- **Atomare Wirtschaft:**
  - Handel, Post, Auktionen und Gutschriften schreiben alle beteiligten Charaktere, Briefe, Auktionen und Geldbuch-Einträge in einer Transaktion (`EconomyCommit`).
  - Speichern geschieht in Auftragsreihenfolge: Ein älterer Stand überschreibt nie einen neueren.
- **Abfragbar, wo nötig:** Inventar, Fähigkeiten, Talente, Quests und Ruf liegen in eigenen Tabellen, etwa für Auswertungen und Auktionshaus-Statistiken. Seltener gebrauchte Felder liegen als JSON in `characters.world_state`.
- **Wertebereiche in der Datenbank:**
  - Stufe 1–60, Geld, Mengen und Gebote nicht negativ, Spielleiter-Stufe 0–2, TOTP-Geheimnis in Base32.
  - Ein Programmfehler kann so keine unmöglichen Werte speichern.
- **Fremdschlüssel** mit `ON DELETE CASCADE` bzw. `SET NULL` und je einem Index.

## Tabellen

| Tabelle | Inhalt | Wichtige Spalten |
|---|---|---|
| `accounts` | Konten | `username` (eindeutig, ohne Groß/Klein), `password_hash` (PBKDF2), `failed_logins`, `locked_until`, `totp_secret`, `totp_last_step`, `gm_level`, `is_banned`, `ban_until`, `ban_reason`, `muted_until` |
| `session_tokens` | Wiederverbindung | `token_hash` (SHA-256, nie das Token), `account_id`, `expires_at` |
| `characters` | Spieler und KI-Mitspieler | `account_id` (NULL = KI), `name` (eindeutig), `race`, `class`, `level`, `xp`, `rested_xp`, `money`, `zone_id`, `pos_*`, `is_bot`, `bot_data` (JSON), `world_state` (JSON, siehe unten), `discovered_areas`, `travel_nodes`, `completed_quests` |
| `character_items` | Taschen, Bank, Ausrüstung, Taschenplätze | `container`, `slot`, `item_id`, `count`, `durability`, `enchant` |
| `character_abilities` / `character_talents` | gelernte Fähigkeiten und Talente | `ability_id` + `rank` / `talent_id` + `rank` |
| `character_quests` | laufende Quests | `quest_id`, `state`, `counts` |
| `character_reputation` | Ruf | `faction_id`, `value` |
| `guilds` / `guild_members` | Gilden | Name eindeutig, `leader_id`, `motd`, `rank_names`; ein Charakter in höchstens einer Gilde |
| `friends` / `character_ignores` | Freundes- und Ignorierliste | `character_id`, `friend_id` bzw. `ignored_id` |
| `mail` | Post | `recipient_id`, `sender_id`, `money`, `cod`, `items` (JSON), `deliver_at`, `expires_at`, `returned` |
| `auctions` | Auktionshaus (je Fraktion) | `house`, `seller_id`, `item_id`, `count`, `start_bid`, `buyout`, `current_bid`, `bidder_id`, `expires_at` |
| `economy_log` | Geldbuch: jede Goldbewegung | `character_id`, `delta`, `balance`, `reason` (z. B. `loot`, `trade`, `gm`, `content_refund`), `reference` |
| `player_reports` | Spielermeldungen | Melder, Ziel, Grund, Text, `evidence` (letzte Chatzeilen), `handled` |
| `audit_log` | Prüfprotokoll | `account_id`, `actor`, `action` (`login.*`, `account.*`, `gm.*`, `anticheat.*`), `target`, `detail`, `ip` |
| `spawn_timers` | Wiederkehr von Weltbossen | `key` (Zone:Spawn:Kreatur), `respawn_at` |
| `realm_info` | Betriebsdaten | `content_version`, `server_version`, `mode`, `started_at` |
| `schema_migrations` | angewendete Migrationen | `version`, `applied_at` |

## `characters.world_state` (JSON)

Seltener gebrauchte oder verschachtelte Felder:

- **Orte:** Leiche, Bindepunkt, Rückkehrpunkt (Instanzen).
- **Charakterwerte:** Talent-Resets, Reitfertigkeit und Reittiere, Berufe mit Rezepten.
- **Instanzen:** Instanzbindungen, Instanz-Sperrzeiten.
- **Einstellungen:** Clienteinstellungen (Tasten, Fenster, Barrierefreiheit), verlassene Chatkanäle, Standortfreigabe.
- **Fortschritt:** Erfolge mit Fortschritt, gewählter Titel, Tutorialstand.
- **Patches:**
  - `contentMigrations`: angewendete Content-Migrationen.
  - `quarantine`: verwahrte Gegenstände ohne Definition, siehe [Updates](UPDATES.md).

## Konsistenz

- **Probelauf:** `--check` bzw. `.pruefen` vergleicht alle gespeicherten Charaktere, Briefe und Auktionen mit dem Content, ohne etwas zu ändern. Es meldet ungültige Werte, unbekannte Regionen, Gegenstände, Quests und Fähigkeiten sowie ausstehende Content-Migrationen.
- **Beim Laden** wendet der Server dieselben Regeln wirklich an (`ContentMigrator`).
- **Sicherungen:** `tools/backup.sh` / `restore.sh`, siehe [Betrieb](BETRIEB.md#datensicherung).
