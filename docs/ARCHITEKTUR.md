# Architektur von Eternal Realms

## Entscheidung: eigener, serverautoritärer .NET-Server statt Unity-Netcode

| Option | Warum nicht (oder warum doch) |
|---|---|
| Unity Netcode for GameObjects / Mirror / Fish-Net als Host | Für Sitzungsspiele mit 2–64 Spielern gebaut. Jeder Server ist ein Unity-Prozess mit Szenen-, Physik- und Rendering-Overhead. Persistente Welten mit Tausenden Einheiten, Datenbank-Anbindung und Sharding müssten wir trotzdem selbst bauen. |
| Photon Fusion / Quantum, PlayFab Multiplayer | Gehostete Sitzungen, Lizenzkosten pro CCU, keine persistente Spielwelt; Spiellogik auf dem Server nur eingeschränkt. |
| SpatialOS / Improbable | Eingestellt. |
| **Eigener headless .NET-8-Server (gewählt)** | Spiellogik als reines C# ohne Engine: läuft auf billigen Linux-Servern, ist vollständig per Unit- und Simulationstest prüfbar, teilt Regeln und Protokoll mit dem Unity-Client über eine gemeinsame `netstandard2.1`-Bibliothek. Diesen Weg gehen auch klassische MMORPG-Server. |

Unity ist damit **nur** Client: Darstellung, Eingabe, Animation, Sound. Der Server entscheidet alles.

## Bausteine

```
┌──────────────── Unity-6-Client (Windows) ────────────────┐
│ Darstellung · Eingabe · UI · Interpolation               │
│        └─ EternalRealms.Shared (GameClient, Protokoll)   │
└──────────────────────────┬───────────────────────────────┘
                           │ UDP (LiteNetLib)
                           │  Kanal 0: zuverlässig, geordnet – Spiellogik
                           │  Kanal 1: unzuverlässig – Positions-Snapshots
┌──────────────────────────┴─────── Realm-Server (.NET 8) ─┐
│ NetworkServer → WorldSession (Prüfung, Rate-Limit)       │
│ GameWorld (ein Spielthread, 20 Ticks/s)                  │
│   ZoneInstance × n  (offene Welt + Dungeon-Instanzen)    │
│     SpatialGrid · Spawnpunkte · Sichtbarkeit · Wetter    │
│   Systeme: Combat · Quests · Progression · Loot · Death  │
│            Interactions · Chat · Groups                  │
│   KI: CreatureAI · BotBrain · BotDirector                │
│ Persistenz (async): PostgresStore / InMemoryStore        │
└──────────────────────────┬───────────────────────────────┘
                           │ Npgsql
                    PostgreSQL 14+
```

### Serverautorität

* Der Client sendet nur **Absichten**: „wirke Fähigkeit X auf Ziel Y“, „ich möchte nach P laufen“.
* Der Server prüft bei jeder Fähigkeit Tod, Kontrolleffekte, Schweigen, Schulsperre, laufende Zauber, globale Abklingzeit, Abklingzeit, Haltung/Gestalt, Kampfstatus, Tarnung, Waffe/Schild, Kombopunkte, Ziel (Freund/Feind/tot), Reichweite und Kosten.
* Schaden, Heilung, Treffer und Krit würfelt nur der Server (`CombatSystem`).
* Bewegung: Der Server begrenzt jede Positionsmeldung auf das erlaubte Tempo (`GameWorld.HandleMove`). Betäubt, festgehalten oder in Furcht wird die Meldung verworfen. Meldet der Server eine Position, die mehr als 3 m von der lokalen abweicht, übernimmt der Client sie.
* Jede Sitzung hat ein Nachrichtenlimit (Standard 80/s); fehlerhafte Pakete trennen die Verbindung.

### Simulation

* **Ein Spielthread pro Realm-Prozess**, feste Schrittweite (Standard 20 Hz). Keine Sperren in der Spiellogik.
* Datenbankzugriffe laufen im Hintergrund (`Task.Run`). Ihre Ergebnisse kehren über `GameWorld.Post` in den Spielthread zurück.
* **Interessensbereich:** Ein Raster mit 32-m-Zellen (`SpatialGrid`) beantwortet Umgebungsabfragen für Aggro, Flächenzauber und Sichtbarkeit (110 m) in O(Einheiten in Reichweite) statt O(Einheiten der Zone).
* **Snapshots** gehen mit 10 Hz nur an Spieler, die die Einheit sehen. Sie werden in Pakete unterhalb der MTU zerlegt (24 Einheiten pro Paket).
* **Reproduzierbarkeit:** Zufall ist injizierbar (`IRandom`), und die Weltgenerierung nutzt einen stabilen Hash. Dadurch sind Simulationstests deterministisch.

### Persistenz

* Das Speichern ist „write-behind“: Alle 60 s sowie beim Ausloggen und Beenden wird eine **Kopie** des Charakters (`CharacterData.Clone`) im Hintergrund geschrieben. Der Spielthread wartet nie auf die Datenbank.
* Alle Datenbankaufträge laufen über die **`PersistenceQueue`** – einen einzigen Hintergrundarbeiter, der Aufträge in genau der Reihenfolge ausführt, in der der Spielthread sie erzeugt. Ein älterer Speicherstand kann so nie einen neueren überschreiben, und ein Charakter wird erst geladen, wenn seine letzte Speicherung fertig ist. Beim Beenden wartet der Server, bis die Warteschlange leer ist.
* Jeder Speichervorgang ist eine Transaktion. Kinderdaten (Inventar, Bank, Taschen, Quests, Talente, Ruf, Fähigkeiten) werden je Tabelle mit einem einzigen `INSERT … SELECT unnest(…)` geschrieben.
* **Wirtschaft ohne Duplikate:** Post, Auktionshaus und Beutewürfe ändern zuerst den Zustand im Speicher und schreiben dann in **einer** Transaktion (`IEconomyRepository.CommitAsync`) die beteiligten Charaktere, die Briefe, die Auktionen und das Geldprotokoll. Ein Gegenstand ist immer an genau einem Ort; stürzt der Server vor dem Commit ab, sind Absender und Brief beide auf dem alten Stand.
* **Geldprotokoll:** Geld ändert sich nur über `EconomySystem.Credit/Debit`. Jede Bewegung landet mit Grund und Kontostand in `economy_log`; `FlowByReason` summiert Quellen und Senken (Beute, Quests, Händler gegen Reparatur, Training, Gebühren, Porto, Talent-Reset) zur Inflationsbeobachtung.
* Schema: `001_initial.sql` (Konten, Charaktere, Gilden, Freunde), `002_economy.sql` (Post, Auktionen, Geldprotokoll). Briefe und Auktionen werden beim Start vollständig geladen und liegen im Speicher.
* Migrationen laufen beim Start automatisch (`schema_migrations`).

## Weg zu großen Spielerzahlen

Die heutige Struktur ist schon darauf ausgelegt. Die Schritte im Einzelnen:

1. **Zonen in eigene Prozesse:** Jede `ZoneInstance` simuliert unabhängig; Querverbindungen gibt es nur über Gruppen, Chat und Zonenwechsel. Ein *World Director* weist Zonen und Instanzen Prozessen zu. Ein Zonenwechsel wird zur Übergabe: Charakter speichern, beim Zielprozess einloggen, den Client umleiten.
2. **Gateway:** Eine vorgeschaltete Anmelde- und Weiterleitungsschicht mit TLS übernimmt Login und Charakterauswahl. Danach verbindet sich der Client mit dem Zonenprozess. Das Gateway nimmt dem Spielprozess die PBKDF2-Last ab.
3. **Gemeinsame Dienste:** Chat-Kanäle, Gruppen über Zonen hinweg, Gilden, Freundeslisten und das Auktionshaus werden eigene Dienste (z. B. mit Redis Pub/Sub). Das Auktionshaus ist rein datenbankgetrieben.
4. **Layer/Phasing:** Wird eine Zone zu voll, startet eine zweite Kopie derselben Zone (Layer), ähnlich wie bei Dungeon-Instanzen.
5. **KI-Mitspieler skalieren mit:** Bots sind normale Spielfiguren in der Zone. Ihre Zahl pro Zone ist konfigurierbar (`AmbientBots`), und ihre Denkfrequenz hängt von der Reaktionszeit ab (0,25–0,7 s).

## Gemeinsame Bibliothek `EternalRealms.Shared`

`netstandard2.1` und C# 9, damit Unity 6 dieselbe DLL lädt:

* **Content-Schema und Lader** (`Content/*`): Server und Client lesen dieselben JSON-Dateien.
* **Regeln** (`Rules/*`): Erfahrungskurve, Kampfformeln, Werte. Der Client nutzt sie nur für Tooltips.
* **Protokoll** (`Net/*`): rund 70 Nachrichtentypen, binär, `[ushort Opcode][Nutzdaten]`.
* **GameClient** (`Client/*`): Verbindung, Weltspiegel und Ereignisse. Unity, der Konsolenclient und die Tests nutzen ihn.

## Bekannte Grenzen dieses Meilensteins

* **Kein Navmesh:** Monster und Bots laufen auf geraden Linien, der Server kennt keine Hindernisse und keine Höhe. Geplant ist ein Recast/Detour-Navmesh, das aus der Unity-Szene exportiert wird.
* **Login über UDP:** Das Passwort wird im Klartext an den Server übertragen und dort mit PBKDF2 gehasht. Für den Betrieb im Internet ist das TLS-Gateway (Schritt 2 oben) Pflicht.
* **Ein Prozess:** Sharding nach Zonen ist vorbereitet, aber nicht umgesetzt.
* **Eine Datenbankverbindung für alle Schreibvorgänge:** Die geordnete Warteschlange ist bewusst einfach. Für Hunderte Spieler ist sie ausreichend; danach wird sie je Charakter gestreut (gleiche Ordnung pro Charakter, parallele Arbeiter).
* **Gebundene Gegenstände:** Gebunden ist bisher, was „beim Aufheben gebunden“ ist oder ein Questgegenstand; „beim Anlegen gebunden“ ist noch nicht modelliert.
