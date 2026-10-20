# Roadmap

Ehrlicher Stand: Ein MMORPG im Umfang des klassischen Vorbilds ist die Arbeit eines Teams über Jahre. Dieses Repository legt das **technische und spielerische Fundament** – vollständig lauffähig, getestet und so gebaut, dass Inhalte und Systeme schrittweise wachsen.

## Meilenstein 1 – Fundament ✅ (dieser Stand)

- [x] Architektur: serverautoritärer .NET-8-Realmserver, UDP (LiteNetLib), PostgreSQL, gemeinsame Bibliothek für Unity
- [x] 2 Fraktionen, 8 Völker mit Lore, Volksfähigkeiten, Aussehensoptionen
- [x] 9 Klassen, 27 Talentbäume (192 Talente), 157 Fähigkeiten mit 601 Rängen, Wut/Energie/Mana, Haltungen und Gestalten, Kombopunkte, Begleiter
- [x] Stufen 1–60 mit klassischer Erfahrungskurve, Ruhebonus, Gruppen-EP, graue Gegner, Entdeckungs-EP, Talentpunkte ab 10
- [x] Tab-Target-Kampf: Autoangriff, Angriffstabelle, Zauberzeiten, Zurückwerfen, globale Abklingzeit, Unterbrechen mit Schulsperre, Buffs/Debuffs/DoTs/HoTs/Schilde, Betäubung/Furcht/Wurzeln/Schweigen, Rüstung und Widerstände, Bedrohung mit 110/130-%-Regel, Spott
- [x] Monster-KI: Aggroradius nach Stufe, Hilferufe, Flucht, Leine/Rückkehr, Fähigkeiten mit Abklingzeit, Bossphasen mit Rufen und Adds, Patrouillen, Wandern, Tagesabläufe von NPCs, Wachen
- [x] Tod: Geist, Friedhof, Leichenlauf, Geisterheiler mit Strafe, Haltbarkeit, Wiederbelebung durch Klassen
- [x] Questsystem mit allen Questtypen, Ketten, Entscheidungen, Wahlbelohnungen, Ruf, Eskorten mit Hinterhalten, Klassen- und Dungeonquests
- [x] 2 vollständig spielbare Startgebiete mit 56 Quests, Dungeon „Wurzelkrypta“ (3 Bosse), 2 Weltbosse, 161 Gegenstände, Händler, Lehrer, Gasthäuser, Flugpunkte, Wetter, Tag/Nacht
- [x] **KI-Mitspieler**: Suche nach Rollen, Persönlichkeiten, Kampfprofile für alle 18 Klassenrollen, Heilen/Tanken/Unterbrechen, Gruppenchat-Befehle, Bots reden miteinander, steigen auf, eigenständige Bewohner jeder Zone
- [x] Unity-6-Client (Platzhaltergrafik, vollständige Bedienung), Konsolenclient
- [x] 47 automatische Tests inkl. Simulationen (Dungeonboss mit Bots, Eskorte, alle Kampfprofile) und Netzwerk-/Datenbanktests

## Meilenstein 2 – Gesellschaft und Wirtschaft

Stand: in Arbeit. Erledigt (implementiert und getestet):

- [x] KI-Persönlichkeiten (mutig, vorsichtig, abenteuerlustig, Veteran …), Schwierigkeitsstufen Anfänger–Veteran
- [x] Befehle mit Anrede („Tank, greif den Boss an.“), Kontrolle, Rückzug, Fokus, Markierungen, Befehlsleiste
- [x] Langzeitbegleiter (Favoriten, Offline-Entwicklung, Vertrautheit, Chronik)
- [x] Weltbewohner: Simulationsstufen voll/reduziert/ruhend, Stadtleben, eigenständiges Questen, Weiterziehen
- [x] Geordnete Persistenz-Warteschlange, Geldprotokoll (`economy_log`) für jede Geldbewegung
- [x] Taschen (4 Plätze), Stapel teilen/zusammenlegen, Sortieren, Bank beim Bankier
- [x] Beuteregeln (Jeder für sich, Reihum, Gruppe, Bedarf vor Gier, Plündermeister), faire Bot-Würfe, /würfeln
- [x] Post (atomar mit dem Absender gespeichert), Auktionshaus (12/24/48 h, Gebote, Sofortkauf, Gebühren), begrenztes Bot-Angebot
- [x] Talent-Reset gegen Gold, Rufstufen Hasserfüllt bis Ehrfürchtig
- [x] Unity-Fenster für Bank, Post, Auktionshaus, Würfe, Beuteregel und Talent-Reset (nur kompiliert, nicht im Editor gestartet)

Offen in diesem Meilenstein:

- [x] Reittiere (Stufe 40: +60 %, Stufe 60: +100 %), Sammlung, Bots reiten mit
- [x] Gilden mit Rängen und Gildenchat, Freundesliste, Ignorieren, Melden, KI-Freundesliste
- [x] Gruppenfinder mit KI-Modi, Bereitschaftscheck, Abstimmung, Rollenwahl, Untergruppen, Ersatz ohne Bosskampf-Unterbrechung
- [x] Instanz-IDs, Bindungen, Schlachtzugsperren, Zurücksetzen, Wiederverbindung

Offen in diesem Meilenstein:

- [ ] Berufe

- [ ] Berufe: Kräuterkunde, Bergbau, Kürschnerei, Schmiedekunst, Alchemie, Schneiderei, Lederverarbeitung, Kochen, Erste Hilfe (Schema und Objekt-Felder vorhanden)
- [ ] Regionen 10–30 mit Inhalten: Westmark, Rotsandwüste, Nebelmoor, Splitterküste, Goldene Ebene
- [ ] Hauptstädte Sonnenfeste und Kharzul

## Meilenstein 3 – Präsentation und Welt

- [ ] Stilisierte 3D-Modelle, Animationen pro Volk (Animationsgruppen sind im Content hinterlegt), Terrain und Vegetation je Biom
- [ ] UI Toolkit statt IMGUI, Minikarte, Weltkarte
- [ ] Musik und Umgebungsgeräusche (Felder `music`/`ambience` pro Zone vorhanden)
- [ ] Navmesh (Recast/Detour) aus Unity exportiert, serverseitige Wegfindung und Höhen
- [ ] Schiffe zwischen Kontinenten, Flüge zwischen Regionen
- [ ] Bots: eigenständiges Questen, Gruppenbildung untereinander

## Meilenstein 4 – Endgame

- [ ] Regionen 30–60, weitere Dungeons
- [ ] Raids für 10, 20 und 40 Spieler mit Bot-Unterstützung (Gruppe wird bereits automatisch zum Schlachtzug bis 40)
- [ ] PvP: Ehre, Schlachtfelder, umkämpfte Gebiete (PvP-Markierung `.pvp` ist vorhanden)
- [ ] Rufbelohnungen, seltene Gegenstände, epische Klassenquests

## Meilenstein 5 – Betrieb

- [ ] TLS-Anmeldegateway, Zonen-Sharding über mehrere Prozesse, Layer
- [ ] Monitoring, Admin-Werkzeuge, Anti-Cheat-Auswertung
- [ ] Steam-Integration

## Spezifikation Kapitel 28–68 (nachgereicht, nach Meilenstein 2)

Die Kapitel 28–68 der Produktspezifikation werden nach Abschluss der Wirtschaft in dieser Reihenfolge eingeplant:

| Block | Kapitel | Inhalt |
|---|---|---|
| A | 28, 29, 30, 59 | Vollständiges HUD (UI Toolkit), verschiebbare/skalierbare Fenster, frei belegbare Tasten, Weltkarte und Minimap, Barrierefreiheit – Einstellungen dauerhaft gespeichert |
| B | 31, 32, 46 | Chatkanäle (Handel, Gebiet, Raid, Gilde), Blockieren/Melden, Gegenstandslinks, Freundesliste inkl. KI-Freunde, direkter Spielerhandel (atomar) |
| C | 33, 34, 51, 52 | Animationen und Emotes (auch für Bots), Musik/Soundeffekte/Audioeinstellungen, Grafikstufen und Performancemessung |
| D | 35, 36, 37, 47 | Hauptstädte (je Fraktion zwei), Tag-Nacht-Zyklus und Wetter mit Auswirkungen, dynamische Weltereignisse, Spawnregeln für Seltene und Weltbosse |
| E | 38, 39, 58 | Erfolge und Titel (ohne Spielvorteile), interaktives Tutorial mit KI-Erklärer, Lokalisierung Deutsch/Englisch |
| F | 40, 41, 42, 49, 50, 54 | Accountsicherheit (Brute-Force-Schutz, Tokens, optionale 2FA), Wiederverbindung, Backups, Anti-Cheat-Auswertung, GM-/Admin-Werkzeuge mit Protokoll |
| G | 43–45, 48 | KI-Ebenen ausbauen: Encounter-Manager für Dungeons/Raids, Tankwechsel, Flächen ausweichen, Raidkoordination bis 40, Bot-Balance |
| H | 53, 56, 57, 60, 64, 65 | Datenmodell vervollständigen, Update-/Rollback-Konzept, Serverbetriebsarten (klassisch, KI-unterstützt, Entwicklung), Content-Editoren |
| I | 55, 61, 62, 66, 67, 68 | Steam-Vorbereitung, Inhaltsumfang 1–60, kosmetische Monetarisierung ohne Pay-to-Win, Endgame |
