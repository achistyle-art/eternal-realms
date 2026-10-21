# Eternal Realms

Ein Fantasy-MMORPG für Windows-PC im Geist der klassischen Genre-Vorbilder – mit eigener Welt, eigenen Völkern, eigener Geschichte – und einem Alleinstellungsmerkmal: **intelligenten KI-Mitspielern**, die die Welt auch dann lebendig machen, wenn nur ein einziger Mensch online ist.

* **Server:** serverautoritär, .NET 8, UDP (LiteNetLib), PostgreSQL – läuft headless auf Linux oder Windows
* **Client:** Unity 6 (C#), teilt Regeln, Content und Protokoll mit dem Server über eine gemeinsame Bibliothek
* **Content:** vollständig datengetrieben (JSON) mit automatischer Prüfung aller Querverweise

> Stand: **Meilenstein 1 – Fundament.** Zwei Startgebiete, ein Dungeon und zwei Weltbosse sind komplett spielbar. Die übrigen 21 Regionen der Weltkarte sind angelegt, aber noch ohne Inhalte. Die Grafik besteht aus Platzhalterformen. Was fertig ist und was folgt, steht in der [Roadmap](docs/ROADMAP.md).

## Was schon funktioniert

| Bereich | Umfang |
|---|---|
| Fraktionen & Völker | Sonnenbündnis und Schattenpakt, 8 Völker mit Geschichte, Volksfähigkeiten, Aussehen (Haut, Gesicht, Frisur, Haarfarbe, Bart, Augen, Narben, Tattoos, Größe) |
| Klassen | Krieger, Paladin, Jäger, Schurke, Priester, Schamane, Magier, Hexenmeister, Druide · 157 Fähigkeiten mit 601 Rängen · 27 Talentbäume mit 192 Talenten · Wut, Energie, Mana · Haltungen, Bär-/Katzengestalt, Kombopunkte, Tier- und Dämonenbegleiter |
| Fortschritt | Stufe 1–60 mit klassischer Kurve (≈ 4 Mio. EP), Ruhebonus im Gasthaus, Gruppen-EP, graue Gegner, Entdeckungs-EP, Klassenlehrer, Talentpunkte ab Stufe 10 |
| Kampf | Tab-Target, Autoangriff, eine Angriffstabelle (Verfehlen/Ausweichen/Parieren/Streifen/Blocken/Krit), Zauberzeiten mit Zurückwerfen, globale Abklingzeit, Unterbrechen mit Schulsperre, DoTs/HoTs/Schilde, Betäuben/Furcht/Wurzeln/Schweigen/Verwandeln, Rüstung, Widerstände, Bedrohung und Spott |
| Monster-KI | Aggroradius nach Stufe, Hilferufe, Flucht, Rückkehr nach dem Kampf, Fähigkeiten mit Abklingzeit, Bosse mit Phasen, Rufen und Adds, Patrouillen, NPC-Tagesabläufe, Wachen |
| Quests | Töten, Sammeln, Erkunden, Benutzen, Sprechen, Liefern, Eskortieren/Beschützen (mit Hinterhalten), Ketten, Entscheidungen, Wahlbelohnungen, Ruf, Klassen-, Dungeon- und Weltbossquests · **56 Quests** |
| Welt | 2 Kontinente, 26 Regionen, Startgebiete Morgentauwald und Aschensteppe, Dungeon Wurzelkrypta, Tag/Nacht, Wetter, Gasthäuser, Händler, Reparatur, Flugpunkte, Ruhestein, Geist und Friedhof |
| **KI-Mitspieler** | „KI-Mitspieler suchen“ füllt Tank, Heiler und Schaden · Persönlichkeiten · Kampfprofile für alle 18 Klassenrollen · Bots folgen, warten, greifen an, heilen, spotten, unterbrechen, beleben wieder, rasten, reden miteinander, steigen mit auf · eigenständige Bewohner in jeder Zone → [Details](docs/KI-MITSPIELER.md) |

## Schnellstart

Voraussetzung: [.NET 8 SDK](https://dotnet.microsoft.com/download). PostgreSQL ist optional.

```bash
# 1. Server starten (ohne Datenbank: Daten im Arbeitsspeicher)
dotnet run --project src/EternalRealms.ServerHost

# 2. In einem zweiten Terminal: Textclient
dotnet run --project src/EternalRealms.ConsoleClient -- 127.0.0.1 7777
```

Im Textclient:

```text
register heldin geheim123
create Lysandra Aldorer Priest w
play 1
look
talk 240        # NPC mit gelbem ! ansprechen (ID aus 'look')
accept 1
bots            # KI-Mitspieler suchen
bot 1
bot 2
p Wir gehen in den Dungeon
```

Mit PostgreSQL und Docker:

```bash
docker compose up --build        # PostgreSQL 16 und Realmserver auf UDP 7777
```

### Unity-Client

1. Unity 6 (6000.0 LTS) installieren und `client/Unity` als Projekt öffnen.
2. Menü **Eternal Realms → Shared-DLL und Content kopieren** ausführen. Es baut `EternalRealms.Shared` und kopiert die DLLs sowie den Content nach `Assets/Plugins` und `Assets/StreamingAssets`. Alternativ geht das mit `tools/build-unity-plugins.sh`.
3. *Project Settings → Player → Active Input Handling* auf **Both** oder **Input Manager (Old)** stellen.
4. Eine beliebige (auch leere) Szene starten. Der Client baut sich selbst auf: Anmeldung, Charaktereditor, Spielwelt.

Steuerung: **WASD** laufen, **Q/E** seitwärts, **Maus links/rechts** Kamera bzw. Figur drehen, **Linksklick** Ziel wählen, **Rechtsklick** angreifen oder ansprechen, **Tab** nächstes Ziel, **1–0** Aktionsleiste, **F** interagieren, **Enter** Chat (`/p` Gruppe, `/g` Allgemein, `/w Name`, `/inv Name`), **L** Quests, **B** Taschen, **K** Zauberbuch, **N** Talente, **P** Gruppe und KI-Mitspieler.

> Die Unity-Skripte wurden gegen die UnityEngine-Referenzassemblys (2021.3) kompiliert. In einem echten Unity-6-Editor wurde das Projekt in dieser Umgebung noch nicht geöffnet. Rückmeldungen aus dem ersten Start sind deshalb willkommen.

## Konfiguration (Umgebungsvariablen)

| Variable | Standard | Bedeutung |
|---|---|---|
| `ER_PORT` | 7777 | UDP-Port |
| `ER_DATABASE` | – | Npgsql-Verbindungszeichenfolge, z. B. `Host=localhost;Username=realms;Password=…;Database=eternal_realms`. Leer = Arbeitsspeicher |
| `ER_CONTENT` | `content` | Content-Verzeichnis |
| `ER_TICK_RATE` | 20 | Simulationsschritte pro Sekunde |
| `ER_AMBIENT_BOTS` | true | eigenständige KI-Bewohner in den Zonen |
| `ER_ALLOW_REGISTRATION` | true | neue Konten erlauben |
| `ER_DEV_COMMANDS` | false | Entwicklerbefehle im Chat (`#level 15`, `#item id`, `#tele zone x z`, `#gold 10`, `#kill`, `#spawn id`, `#where`) |
| `ER_AUTOSAVE_SECONDS` | 60 | Speicherintervall |
| `ER_MODE` | `ki` | Betriebsart: `klassisch` (nur Menschen, keine KI-Mitspieler), `ki` (Standard), `entwicklung` (Entwicklerbefehle und Konsole an) – einzelne Variablen überschreiben die Voreinstellungen |
| `ER_BOT_COMPANIONS` | true | KI-Mitspieler einladen und von der Gruppensuche ergänzen lassen |
| `ER_STATUS_FILE` | – | Statusdatei (`running`/`stopped`/`failed`) für das Update-Skript |
| `ER_MAX_PLAYERS` | 1000 | Spieler gleichzeitig in der Welt, weitere warten in der Anmeldeschlange |
| `ER_LINKDEAD_SECONDS` | 60 | so lange bleibt ein Charakter nach Verbindungsabbruch in der Welt (0 = sofort abmelden) |
| `ER_SESSION_TOKEN_HOURS` | 12 | Gültigkeit des einmaligen Wiederverbindungstokens |
| `ER_MAINTENANCE` | false | Wartungsmodus beim Start (nur Spielleiter) |
| `ER_CONSOLE` | false | Spielleiter-Befehle über die Serverkonsole (z. B. `rechte meinkonto 2`) |
| `ER_CHEAT_FLAG_SCORE` / `ER_CHEAT_KICK_SCORE` | 50 / 150 | Anti-Cheat: Meldung an Spielleiter / Trennung (nie automatische Sperre) |

Betrieb (Spielleitung, Backups, Kontoschutz): siehe [docs/BETRIEB.md](docs/BETRIEB.md).

Datenbankmigrationen (`db/migrations`) laufen beim Start automatisch.

## Tests

```bash
dotnet test                                           # 210 Tests
ER_TEST_DATABASE="Host=localhost;Username=realms;Password=realms;Database=er_test" dotnet test   # zusätzlich gegen PostgreSQL
```

Die Tests prüfen die Regeln und den gesamten Content, und sie simulieren echtes Spielgeschehen:

* einen Stufe-1-Krieger gegen einen Wolf,
* Zauberzeiten, Unterbrechungen und das Zurücklaufen von Monstern,
* Tod und Leichenlauf,
* komplette Questketten einschließlich Eskorte,
* eine Gruppe aus Spieler und 4 KI-Mitspielern gegen den ersten Dungeonboss,
* jedes Bot-Kampfprofil allein,
* eigenständige Bots,
* einen vollständigen Ablauf über echtes UDP sowie das Speichern und Laden über PostgreSQL.

## Projektaufbau

```
src/EternalRealms.Shared        gemeinsame Bibliothek (netstandard2.1): Content-Schema, Regeln, Protokoll, GameClient
src/EternalRealms.Server        Spielwelt, Systeme, KI, Persistenz, Netzwerk
src/EternalRealms.ServerHost    ausführbarer Realmserver
src/EternalRealms.ConsoleClient Textclient
src/EternalRealms.ContentTool   Content-Werkzeug „er-content“: prüfen, auswerten, Verweise, IDs umbenennen, Vorlagen
client/Unity                    Unity-6-Client
content/                        alle Spielinhalte (JSON)
db/migrations/                  PostgreSQL-Schema (nur vorwärts, siehe docs/DATENMODELL.md)
tools/                          Content-Generatoren, Unity-Plugin-Build, Backup/Restore, Release bauen, Update mit Rollback,
                                UnityCheck (Unity-Skripte ohne Editor kompilieren)
tests/                          xUnit-Tests und Simulationen
docs/                           Architektur, Welt, KI-Mitspieler, Content, Roadmap
```

## Dokumentation

* [Architektur und Netzwerkentscheidung](docs/ARCHITEKTUR.md)
* [KI-Mitspieler](docs/KI-MITSPIELER.md)
* [Welt, Völker und Geschichte](docs/WELT.md)
* [Inhalte erstellen](docs/CONTENT.md)
* [Betrieb: Spielleitung, Kontoschutz, Backups](docs/BETRIEB.md)
* [Updates und Rollback](docs/UPDATES.md)
* [Datenmodell](docs/DATENMODELL.md)
* [Roadmap](docs/ROADMAP.md)

Alle Namen, Völker, Orte, Figuren und Texte sind eigenständig. Es werden keine geschützten Inhalte anderer Spiele verwendet.
