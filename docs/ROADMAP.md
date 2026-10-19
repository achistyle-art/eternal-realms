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

- [ ] Berufe: Kräuterkunde, Bergbau, Kürschnerei, Schmiedekunst, Alchemie, Schneiderei, Lederverarbeitung, Kochen, Erste Hilfe (Schema und Objekt-Felder vorhanden)
- [ ] Auktionshaus je Fraktion (Tabelle `auctions` vorhanden), Post, Bank
- [ ] Gilden mit Rängen und Gildenchat, Freundeslisten (Tabellen vorhanden)
- [ ] Bots im Auktionshaus als Marktteilnehmer (Angebot/Nachfrage)
- [ ] Regionen 10–30 mit Inhalten: Westmark, Rotsandwüste, Nebelmoor, Splitterküste, Goldene Ebene
- [ ] Hauptstädte Sonnenfeste und Kharzul

## Meilenstein 3 – Präsentation und Welt

- [ ] Stilisierte 3D-Modelle, Animationen pro Volk (Animationsgruppen sind im Content hinterlegt), Terrain und Vegetation je Biom
- [ ] UI Toolkit statt IMGUI, Minikarte, Weltkarte
- [ ] Musik und Umgebungsgeräusche (Felder `music`/`ambience` pro Zone vorhanden)
- [ ] Navmesh (Recast/Detour) aus Unity exportiert, serverseitige Wegfindung und Höhen
- [ ] Reittiere ab Stufe 40 (Gegenstand angelegt), Schiffe zwischen Kontinenten, Flüge zwischen Regionen
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
