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

- [x] Berufe: 9 Hauptberufe (max. 2) + Angeln, Kochen, Erste Hilfe, Ränge bis Meister, 106 Rezepte, Abbau, Kürschnern,
  Werkbänke, Verzaubern/Entzaubern, Stoff- und Fleischbeute, KI-Bewohner sammeln und stellen her

Meilenstein 2 ist damit abgeschlossen. Offen bleiben Inhalte höherer Stufen (Regionen 10–30, Hauptstädte).

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
- [ ] Monitoring (Admin-Werkzeuge und Anti-Cheat-Auswertung: Block F ✅)
- [ ] Steam-Integration

## Spezifikation Kapitel 28–68 (nachgereicht, nach Meilenstein 2)

Die Kapitel 28–68 der Produktspezifikation werden nach Abschluss der Wirtschaft in dieser Reihenfolge eingeplant:

| Block | Kapitel | Inhalt |
|---|---|---|
| A ✅ | 28, 29, 30, 59 | Vollständiges HUD, verschiebbare/skalierbare Fenster, frei belegbare Tasten, Weltkarte und Minimap, Barrierefreiheit – Einstellungen dauerhaft gespeichert |
| B ✅ | 31, 32, 46 | Chatkanäle (Handel, Gebiet, Raid, Gilde), Blockieren/Melden, Gegenstandslinks, Freundesliste inkl. KI-Freunde, direkter Spielerhandel (atomar) |
| C ✅ | 33, 34, 51, 52 | Animationen und Emotes (auch für Bots), Musik/Soundeffekte/Audioeinstellungen, Grafikstufen und Performancemessung |
| D ✅ | 35, 36, 37, 47 | Hauptstädte (je Fraktion zwei), Tag-Nacht-Zyklus und Wetter mit Auswirkungen, dynamische Weltereignisse, Spawnregeln für Seltene und Weltbosse |
| E ✅ | 38, 39 | Erfolge und Titel (ohne Spielvorteile), interaktives Tutorial mit KI-Erklärer – Lokalisierung (Kapitel 58) auf Wunsch gestrichen, das Spiel bleibt deutsch |
| F ✅ | 40, 41, 42, 49, 50, 54 | Accountsicherheit (Brute-Force-Schutz, Tokens, optionale 2FA), Wiederverbindung, Backups, Anti-Cheat-Auswertung, GM-/Admin-Werkzeuge mit Protokoll |
| G ✅ | 43–45, 48 | KI-Ebenen ausbauen – auf Wunsch zunächst nur für 5er-Dungeons: Encounter-Manager, Tankwechsel, Flächen ausweichen, Bot-Balance. Raids (10/20/40) folgen später |
| H | 53, 56, 57, 60, 64, 65 | Datenmodell vervollständigen, Update-/Rollback-Konzept, Serverbetriebsarten (klassisch, KI-unterstützt, Entwicklung), Content-Editoren |
| I | 55, 61, 62, 66, 67, 68 | Steam-Vorbereitung, Inhaltsumfang 1–60, kosmetische Monetarisierung ohne Pay-to-Win, Endgame |

### Block A – Stand

**Umgesetzt und getestet** (Shared/Server, `UiModelTests`):
- `Keybindings`: Standardbelegung nach Kapitel 29, zwei Belegungen je Aktion, Umbelegen nimmt die Taste anderen Aktionen weg, Normalisierung der Schreibweise, Ergänzung neuer Aktionen in alten Einstellungen.
- `ActionBarSet`: vier Leisten à 12 Plätze, Tauschen beim Ziehen, automatisches Belegen neuer Fähigkeiten, Anzeigezustand (Abklingzeit, Ressource, Reichweite, aktive Haltung, Anzahl).
- `GameSettings`: JSON mit Wertebereichen, kaputte oder alte Daten fallen auf Standardwerte zurück; Server speichert sie am Charakter (`C_SaveSettings`, max. 32 KB, `S_Settings` beim Betreten).
- `MapModel`: Minikartenmarkierungen (Sammelpunkte nur mit Beruf), Kontinentkarte mit Kartenrechtecken je Region, Nebel des Krieges über entdeckte Gebiete (`S_Exploration` inkl. Flugpunkte), Regionsmarkierungen.
- `ItemTooltip` (Vergleich mit dem schwächeren passenden Platz, Haltbarkeit, Anforderungen), `ChatFormat` (Reiterfilter, Kampflog), `Reputation`.
- `S_PlayerState` enthält Parieren, Blocken, Treffer, Distanz-AP, Heilkraft, Tempo, Widerstände und Ruf.

**Umgesetzt, nur kompiliergeprüft** (Unity, noch nicht im Editor ausgeführt):
- Fenster an der Titelleiste verschieben, an der Ecke skalieren, Lage je Fenster gespeichert; Oberflächengröße 0,5–2 und Schriftgröße 9–28.
- Aktionsleisten mit Ziehen aus Zauberbuch/Taschen, Wegziehen entfernt, Sperre; Abklingzeit-Overlay, Reichweite/Ressource eingefärbt, Tastenanzeige.
- Alle Tasten über `UiInput` (Bewegung, Fenster, Leisten 1–4); Umbelegen im Optionsfenster mit Konflikthinweis.
- Charakterfenster (Ausrüstung mit Haltbarkeit, Werte, Widerstände, Ruf), Vergleichs-Tooltips, Minikarte mit Zoom, Weltkarte/Regionskarte, Quest-Tracker, Chatreiter mit Zeitstempel und Kampflog, Untertitel für NPC-Sprechtexte.
- Farbenblind-Paletten (Protanopie/Deuteranopie/Tritanopie, Symbole zusätzlich zur Farbe), weniger Blitze/Partikel, Kamera mit Kollision, Empfindlichkeit, Y-Umkehr, maximaler Entfernung.

**Grenzen:** Die Oberfläche ist weiterhin IMGUI (ohne Grafik-Assets); ein eigener Look mit UI Toolkit folgt mit Block C. Musik-, Effekt-, Umgebungs- und Sprachlautstärke werden gespeichert, wirken aber erst mit dem Audiosystem (Block C); die Gesamtlautstärke wirkt sofort.

### Block B – Stand

**Umgesetzt und getestet** (`TradeChatTests`, Ende-zu-Ende mit zwei Konsolenclients gegen PostgreSQL):
- Kanäle: Gebiet (`/1`, eigene Region), Handel (`/2`, nur in Städten), Gruppensuche (`/4`, ganze Fraktion), Gilde, Gruppe, Schlachtzug, Flüstern (`/r` antwortet). Öffentliche Kanäle sind fraktionsgetrennt, lassen sich betreten/verlassen (`/join`, `/leave`, gespeichert am Charakter) und respektieren die Ignorierliste.
- Spamschutz: höchstens 6 öffentliche Nachrichten in 10 s, derselbe Text frühestens nach 30 s erneut; Gruppen- und Flüsterchat sind ausgenommen.
- Links `|item:ID|`, `|quest:ID|`, `|ability:ID|`: der Server entfernt unbekannte IDs und begrenzt auf 5 Links; der Client zeigt farbige [Namen] mit Tooltip (Umschalt+Klick in den Taschen fügt einen Link ein). Bots bekommen den Text ohne Steuerzeichen.
- Direkter Handel: Anfrage, je 6 Plätze plus Geld, jede Änderung hebt beide Bestätigungen auf, eine Bestätigung gilt nur für den gesehenen Stand (Version). Beim Abschluss prüft der Server jeden Stapel per Referenz, Geld, Platz und Einzigartigkeit erneut und tauscht in einem Schritt; beide Charaktere und das Protokoll (`trade`, `trade_item`) gehen in eine Transaktion. Gebundene Gegenstände (jetzt auch der Ruhestein), Fraktionswechsel, Entfernung > 10 m, Kampf, Tod und Abmelden verhindern oder beenden den Handel. Verschwindet ein angebotener Gegenstand aus der Tasche, fällt er aus dem Handel.
- KI-Mitspieler der eigenen Gruppe nehmen Geschenke an, legen Verbesserungen sofort an und bedanken sich; sie geben nichts her. KI-Freunde stehen bereits seit Meilenstein 2 in der Freundesliste; Melden und Blockieren ebenso.

**Nur kompiliergeprüft** (Unity): Handelsfenster, Handelsanfrage, „Handeln“ am Zielrahmen und `/handeln`, Linkdarstellung mit festhaltbarem Tooltip, Kanal-Schalter in den Chatoptionen.

### Block C – Stand

**Umgesetzt und getestet** (`PresentationTests`, Live-Lauf mit Konsolenclients):
- 20 Emotes als Content (`content/emotes.json`: /winken, /tanzen, /verbeugen, /jubeln, /sitzen …, mit und ohne Ziel), Text an alle in Sprechweite, `S_Emote` für Animation und Klang, Ignorierliste gilt, Spamschutz 1 s. Freie Emotes mit `/e`.
- KI-Mitspieler erwidern Gesten, die ihnen gelten, winken beim Beitritt, jubeln bei Stufenaufstieg, nach Siegen und Bosskämpfen und setzen sich oder tanzen im Gasthaus.
- `AnimationController`: Zustände für Stehen, Gehen, Rennen, Rückwärts, Kampfhaltung, Reiten, Betäubung, Tod, Geist, Schleifen-Emotes (enden bei Bewegung oder Kampf) und Oberkörper-Einblendungen (Angriff, Schuss, Zaubern, Kanalisieren, Treffer, Ausweichen, Blocken, Sammeln, Sprung, Emotes). Schrittzyklus an die Strecke gekoppelt.
- `Synth`: alle Effekte, Umgebungsgeräusche und Musik werden zur Laufzeit erzeugt – deterministisch, ohne Übersteuerung, Musik und Umgebung nahtlos schleifbar. Keine Audiodateien, keine fremden Rechte.
- `MusicDirector`: Musik nach Region, Gebiet (Stadt/Gasthaus), Nacht, Kampf (setzt nach 2 s ein, klingt 8 s nach), Boss und Geisterwelt; Überblendung 2,5 s. `SoundEvents` ordnet Servernachrichten Klängen zu, nur bei eigener oder Gruppenbeteiligung.
- Grafikstufen Niedrig/Mittel/Hoch/Ultra/Eigene mit Hardwareempfehlung; Anzeigeeinstellungen bleiben beim Stufenwechsel erhalten.
- Leistung: `FrameStats` (Mittel, Perzentile, 1 % Tiefstwerte). Der Server misst jeden Takt (Ø/99 %/max/Überläufe), meldet ihn im Pong, im Statusprotokoll und mit `#perf`. Lasttest: 150 KI-Mitspieler in einer Zone bleiben deutlich unter dem 50-ms-Budget.

**Nur kompiliergeprüft** (Unity):
- Figuren aus Gelenken (Mensch, Vierbeiner, Klumpen) mit allen Animationen, Fußtritt-Geräusche, Angriffs-/Treffer-/Zauberanimationen aus Kampfereignissen.
- `AudioManager`: zwei Musikquellen mit Überblendung, Umgebungsquelle, Effektpool im Raum, Oberflächenklänge, NPC-Sprechlaute; Lautstärken Gesamt/Musik/Effekte/Umgebung/Sprache wirken jetzt alle.
- `GraphicsApplier`: Schatten, Kantenglättung, Sichtweite, Nebel, Bildrate (auch im Hintergrund), Auflösung/Vollbild; Figuren- und Namensschild-Sichtweite, Partikeldichte.
- Eigener Oberflächen-Look (`UiSkin`, ohne Grafikdateien) und kontrastreiche Variante; Emote-Auswahl (K), Leistungsanzeige (Strg+F), Grafik-Reiter in den Optionen.

**Grenzen:** Die Figuren bleiben stilisierte Grundkörper, bis echte Modelle mit Animator eingebunden werden (die Zustandsnamen in `AnimClip` sind dafür vorgesehen). Die erzeugte Musik ist bewusst schlicht und kein Ersatz für komponierte Stücke.

### Block D – Stand

**Umgesetzt und getestet** (`WorldDynamicsTests`, Live-Lauf gegen PostgreSQL mit Serverneustart):
- Vier spielbare Hauptstädte (`tools/gen_world_d.py`): Sonnenfeste und Silbermond (Sonnenbündnis), Kharzul und Nachtwacht (Schattenpakt). Jede mit sieben Stadtvierteln (Handelskanal überall), Bank, Auktionshaus, Briefkästen, Gasthaus (Ruhestein-Bindung), Lehrern aller Klassen der Fraktion, allen Berufslehrern, Waffen-/Werkzeughändlern, Stallmeisterei, Flugmeister, Geisterheiler, Bürgern mit Tagesablauf, KI-Bewohnern und Elitewachen der Stufe 60. Wachen greifen auch Eindringlinge der anderen Fraktion an.
- Anreise: Portale in den Startregionen und zurück (nur für die eigene Fraktion), Fernflüge zwischen Startregion und Hauptstädten sowie zwischen den beiden Hauptstädten einer Fraktion. Flüge in andere Regionen sind jetzt möglich.
- Tag/Nacht und Wetter mit Wirkung (`EnvironmentRules`): Regen dämpft Feuer und stärkt Natur/Frost, Schnee stärkt Frost, Gewitter stärkt Natur; Nebel, Sturm, Schnee und Nacht verkürzen die Sicht der Kreaturen; Untote und Geister sind nachts stärker. Spawns mit Tageszeit (Irrlichter und Aschegeister nur nachts, Graumähne nachts, Dünenpirscher tagsüber) erscheinen und verschwinden passend. Bewegungstempo bleibt unverändert, weil die Clientvorhersage es nicht kennt.
- Spawnregeln (`SpawnRules`): zufällige Wiederkehrfenster, schnellere Wiederkehr bei vielen Spielern (bis 35 %), Platzhalter-Seltene (nie zwei zugleich), Weltbosse mit 2–4 h Fenster, Ankündigung in der Region und gespeicherter Wiederkehrzeit (Tabelle `spawn_timers`, Migration 005) – im Live-Lauf blieb der getötete Dornenschlund nach einem Neustart verschwunden.
- Weltereignisse (`content/events.json`, `WorldEventSystem`): vier Ereignisse (Wolfsnacht, Händlerkarren verteidigen, Ruf der Hohlen Flut, Oase in Gefahr) mit Abschnitten Töten/Boss/Verteidigen, Ankündigung, Zeitlimit, Fehlschlag, Belohnung für alle Beteiligten (EP, Geld, Ruf, Gegenstände; bei vollen Taschen per Post). KI-Bewohner der Region gehen von sich aus hin und kämpfen mit. GM-Befehle: `#event`, `#time`, `#weather`.

**Nur kompiliergeprüft** (Unity): Ereignis-Tracker über den Quests, Ereignismarker auf Minikarte und Karte, Blitze bei Gewitter (abschaltbar), Stadtgeräusche.

**Unterwegs gefunden:** Die Erstbesetzung beim Serverstart hat gespeicherte Weltboss-Zeiten gelöscht, bevor sie geladen wurden – der Unit-Test nutzte einen frischen Speicher und sah es nicht. Behoben; der Test stellt den Neustart jetzt mit demselben Speicher nach. `#kill` traf gepanzerte Bosse nicht tödlich; behoben.

### Block E – Stand

Auf Wunsch ohne Übersetzung: Kapitel 58 (Lokalisierung) entfällt; alle Texte bleiben deutsch.

**Umgesetzt und getestet** (`ProgressTests`, Live-Lauf mit Konsolenclient gegen PostgreSQL):
- 49 Erfolge in 10 Kategorien (`content/achievements.json`), 21 Titel. Belohnt werden nur Punkte und frei wählbare Titel – die Datenstruktur kennt gar keine Gegenstände, Werte oder Gold. Fraktionsgebundene und verborgene Erfolge.
- Ein Ereignisweg für alles (`GameWorld.ReportProgress`): Stufe, Siege (auch für die Gruppe in der Nähe), Familien, Seltene, Bosse, Quests, Gebiete, Regionen, Dungeons, Berufe (Fertigkeit, Herstellen, Sammeln), Weltereignisse, Emotes, Handel, Gilde, Freunde, KI-Mitspieler, Reiten, Reittiere, Ruf, Tode, Flüge.
- Erfolgsmeldung an Spieler, Gruppe und Gilde; KI-Mitspieler gratulieren und erringen selbst Erfolge. Titel am Namensschild, bei Bots immer zusammen mit „KI-Mitspieler“.
- Tutorial mit 11 Schritten (`content/tutorial.json`) und der KI-Erklärerin Lysa: folgt neuen Charakteren, flüstert jeden Schritt mit der eigenen Tastenbelegung, zeigt auf das nächste Ziel (Questgeber, passender Gegner, Leiche, Lehrer, Wirt), gibt nach 45 s einen Tipp, reagiert auf Tod und Stufenaufstieg. Schritte schließen sich nur durch echte Handlungen ab. Überspringen und Neustarten jederzeit; Veteranen und Bots bekommen kein Tutorial; der Stand wird gespeichert.

**Nur kompiliergeprüft** (Unity): Erfolgsfenster (H) mit Kategorien, Fortschrittsbalken und Titelwahl, Erfolgseinblendung mit Klang, Tutorial-Leiste mit „Überspringen“, schwebender Hinweispfeil über dem Ziel der Erklärerin, Tutorial-Neustart im Spielmenü.

### Block F – Stand

**Umgesetzt und getestet** (`SecurityTests`, `NetworkTests`, Live-Lauf mit Konsolenclient gegen PostgreSQL):
- **Kontoschutz (40):**
  - Nach 5 Fehlversuchen wird das Konto 15 Minuten gesperrt; die Sperre wird gespeichert und übersteht Neustarts. Pro IP-Adresse gelten höchstens 20 Fehlversuche bzw. 5 neue Konten pro Stunde.
  - Unbekannte Kontonamen brauchen gleich lange und erhalten dieselbe Meldung wie falsche Passwörter.
  - Neue Passwörter brauchen mindestens 8 Zeichen, Buchstaben und Ziffern, dürfen den Kontonamen nicht enthalten und nicht auf der Liste verbreiteter Passwörter stehen.
  - Optionale Zwei-Faktor-Anmeldung (TOTP nach RFC 6238, mit den Testvektoren geprüft). Sie wird erst nach der Bestätigung mit einem Code aktiv; jeder Code gilt nur einmal. Abschalten verlangt Passwort und Code.
  - Passwortänderung macht alle Wiederverbindungstokens ungültig.
- **Wiederverbindung (41/54):**
  - Nach einem Verbindungsabbruch bleibt der Charakter 60 s in der Welt (verbindungslos) und wird ohne Neuladen übernommen. Ein Doppelstand ist dadurch ausgeschlossen.
  - Der Client verbindet sich selbst neu (1–15 s Abstand, 8 Versuche) und meldet sich mit einem einmaligen Token an. Der Server speichert nur dessen SHA-256-Hash.
  - Eine zweite Anmeldung übernimmt den Charakter, statt ihn abzumelden.
  - Trennt der Server die Verbindung (Spielleitung, Wartung, Nachrichtenflut, doppelte Anmeldung), schickt er den Grund mit; der Client verbindet sich dann nicht neu.
  - Anmeldeschlange bei vollem Realm (`ER_MAX_PLAYERS`), Spielleiter ausgenommen. Wartungsmodus, in dem nur Spielleiter hineinkommen.
- **Backups (42):** `tools/backup.sh` und `tools/restore.sh` (siehe `docs/BETRIEB.md`):
  - Sicherung mit pg_dump, Prüfsumme und Aufbewahrung der letzten N Sicherungen.
  - Die Wiederherstellung prüft die Prüfsumme und überschreibt nie unbemerkt.
  - Hier ausgeführt: drei Sicherungen bei Aufbewahrung 2, Wiederherstellung in eine neue Datenbank, Abbruch bei einer belegten Zieldatenbank und bei einer beschädigten Datei.
- **Anti-Cheat (49):**
  - Der Server setzt die Regeln weiterhin selbst durch. Zusätzlich vergibt er Punkte für zu schnelle Bewegung, Nachrichtenflut, unmögliche Aktionen und auffälligen Goldzufluss (nur neu entstandenes Gold, nicht Handel oder Post). Die Punkte klingen ab.
  - Ab 50 Punkten gibt es eine Meldung an alle Spielleiter und einen Eintrag im Prüfprotokoll, ab 150 eine Trennung.
  - Gesperrt wird nie automatisch.
- **Spielleitung (50):**
  - Punktbefehle für Spielleiter: wer, info, ansage, kick, stumm/laut, bann/entbann, gehezu/hol/tele, auffaellig, meldungen/erledigt, protokoll, wartung, speichern.
  - Zusätzlich für Admins: gold, gegenstand, rechte.
  - Serverkonsole (`ER_CONSOLE=true`) mit vollen Rechten.
  - Niemand kann gegen Gleich- oder Höhergestellte vorgehen. Jede Handlung landet im Prüfprotokoll (`audit_log`), ebenso Anmeldungen, Sperren und Sicherheitsänderungen.
- Migration `006_security.sql`. Migrationen laufen jetzt unter einer Datenbanksperre, sodass zwei gleichzeitig startende Prozesse sich nicht stören.

**Nur kompiliergeprüft** (Unity):
- Codefeld für die Zwei-Faktor-Anmeldung auf dem Anmeldebildschirm.
- Fenster „Kontoschutz“ (2FA einrichten/abschalten, Passwort ändern).
- Hinweis während der Wiederverbindung und Platz in der Warteschlange.

**Grenzen:**
- Die Verbindung ist nicht verschlüsselt. Passwörter gehen im Klartext über UDP, daher ist das TLS-Anmeldegateway aus Meilenstein 5 vor einem öffentlichen Betrieb nötig.
- Das 2FA-Geheimnis liegt unverschlüsselt in der Datenbank.
- Die IP-Begrenzung gilt nur im Speicher eines Prozesses.
- Die Kontosperre kann ein Angreifer gezielt auslösen (übliche Abwägung).
- Wiederherstellungscodes für verlorene 2FA-Geräte gibt es nicht; das Abschalten erfolgt dann über die Spielleitung oder Datenbank.

### Block G – Stand

Auf Wunsch zunächst nur für Gruppen bis 5 Spieler. Raids für 10, 20 und 40 Spieler folgen später.

**Umgesetzt und getestet** (`EncounterTests`, `BotTests`, Live-Lauf mit Konsolenclient und vier KI-Mitspielern gegen Moderkeim):
- **Kampfsteuerung (43):** Jeder Bosskampf hat einen Start, Sieg oder Niederlage, Phase und Kampfzeit.
  - Nach `enrageAfter` gerät der Boss in ungezügelte Wut.
  - Bei einer Niederlage verschwinden alle Mechaniken, und der Boss setzt sich zurück.
  - Je Kampf gibt es eine Auswertung: Dauer, Tode, Treffer durch Bodeneffekte je Teilnehmer, Unterbrechungen. Sie erscheint im Serverprotokoll.
  - Die Clients bekommen Bossleiste und Bodeneffekte über eigene Nachrichten.
- **Mechaniken (44):**
  - Bodeneffekte mit Vorwarnung: einmalig oder liegend; unter zufälligen Spielern, unter jedem, unter dem Tank, beim Entferntesten oder am Boss.
  - Pflicht-Unterbrechungen mit Ansage.
  - Vorrangige Adds.
  - Alles wird in den Content-Daten beschrieben und beim Laden geprüft.
- **Bosse der Wurzelkrypta:**
  - Moderkeim: Sporenteppiche, die liegen bleiben; vorrangige Sporlinge.
  - Schwester Elvaine: Verderbte Genesung, die unterbrochen werden muss; Schattenkreis am Entferntesten; vorrangige Dryade.
  - Wurzelfürst Morrak: Wurzelbruch unter jedem Spieler; vorrangige Wurzelkriecher.
  - Wut nach 180, 200 bzw. 240 s.
- **KI-Mitspieler (45):**
  - Sie weichen Bodeneffekten aus (sicherer Punkt in Reichweite des Ziels, auch unter Abbruch eines Zaubers) und suchen sich Kampfplätze außerhalb liegender Flächen.
  - Sie unterbrechen Pflicht-Zauber vorrangig und wechseln auf vorrangige Adds.
  - Fällt der Tank, übernimmt genau ein geeigneter Bot und sagt es an.
- **Bot-Balance (48):**
  - Die Schwierigkeitsstufe bestimmt, wie schnell und wie zuverlässig Bots ausweichen und unterbrechen.
  - Abgestimmt mit einer Kampfsimulation über alle Stufen (Tabelle in `docs/KI-MITSPIELER.md`). Ab „Normal“ ist der Dungeon zuverlässig schaffbar, Anfänger machen sichtbar mehr Fehler.
- **Konsolenclient:** Warnung „Ihr steht in …“ mit Countdown, `raus` läuft aus dem Kreis, `boss` zeigt Phase, Wut und Flächen.

**Nur kompiliergeprüft** (Unity):
- Bodeneffekte als Scheiben in der Welt: orange pulsierend in der Vorwarnung, rot, solange sie wirken.
- Bossleiste oben mit Phase, Kampfzeit, Toden und Countdown zur Wut.
- Große Warnung, solange man in einem Bodeneffekt steht.

**Grenzen:**
- Die Simulation nutzt einen idealisierten Spieler und eine feste Gruppe (Paladin, Priester, Magier, Schurke).
- Bodeneffekte sind Kreise ohne Sichtlinie oder Gelände.
- Weitere Mechanikarten (Kegel, Linien, Positionswechsel des Bosses, „zusammenstellen/verteilen“) gibt es noch nicht.
- Der Ersatz-Tank kann nur spotten, wenn seine Klasse eine Spottfähigkeit hat.

