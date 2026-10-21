# KI-Mitspieler

Das Alleinstellungsmerkmal von Eternal Realms: Die Welt fühlt sich wie ein MMORPG an, auch wenn nur ein Mensch online ist.

## Grundprinzip: Bots sind Spieler

Ein KI-Mitspieler ist eine normale Spielfigur (`Player`) mit Volk, Klasse, Stufe, Erfahrung, Fähigkeiten, Talenten, Inventar und Questlog. Er wird wie jeder Charakter in PostgreSQL gespeichert. Statt einer Netzwerkverbindung hat er ein Gehirn (`BotBrain`). Daraus folgt:

* Bots nutzen exakt dieselben Regeln: Zauberzeiten, globale Abklingzeit, Mana, Wut, Energie, Reichweiten, Bedrohung, Tod und Wiederbelebung.
* Bots bekommen Erfahrung wie Spieler, steigen auf, lernen neue Ränge und verteilen ihre Talentpunkte nach ihrem Kampfprofil.
* Bots erhalten Beute, legen bessere Gegenstände an und verkaufen Plunder.

## Persönlichkeit

Jeder Bot hat einen dauerhaften Archetyp und vier Werte. Die Archetypen bestimmen nicht nur, was ein Bot sagt, sondern auch, wie er handelt:

| Archetyp | Verhalten |
|---|---|
| `mutig` (mutiger Krieger) | wartet kürzer auf den Tank, schützt schwache Gruppenmitglieder, lobt Siege |
| `vorsichtig` (vorsichtiger Heiler) | warnt vor starken oder vielen Gegnern, empfiehlt Pausen nach Kämpfen |
| `abenteuerlustig` (abenteuerlustiger Magier) | freut sich über neue Gebiete und seltene Beute, schlägt Routen vor |
| `veteran` (erfahrener Veteran) | gibt Bosstipps (`boss_tip:<id>` in `dialog.json`), kündigt Phasenwechsel an, mindestens „Erfahren“ |
| `freundlich`, `witzig`, `wortkarg` | Gesprächsstil |

Außerdem hat jeder Bot eine **Schwierigkeitsstufe** (Anfänger, Normal, Erfahren, Veteran). Sie bestimmt Reaktionszeit,
Entscheidungsqualität und Fehlerwahrscheinlichkeit (15 % / 7 % / 3 % / 1 % „zögert“ pro Denkschritt) – nie aber
zusätzliche Informationen: Bots sehen nur, was ein Spieler auch sehen würde. In der Suche lässt sich die Stufe filtern.

**Langzeitbegleiter (9.9):** Mit ★ merkt sich ein Spieler einen Bot als Favoriten. Favoriten erscheinen zuerst in der Suche,
entwickeln sich offline weiter (eine Stufe pro Stunde, höchstens bis zur Stufe des Spielers), sammeln Vertrautheit
(gemeinsame Kämpfe, Bosse, Quests → schnellere Reaktion) und eine Chronik, aus der sie beim Wiedersehen erzählen.

Die vier Werte:

| Wert | Wirkung |
|---|---|
| Geschick (Skill) | Reaktionszeit 0,25–0,7 s, Zuverlässigkeit beim Unterbrechen |
| Vorsicht (Caution) | früher heilen, früher rasten, im Dungeon länger auf den Tank warten |
| Gesprächigkeit (Chattiness) | wie oft der Bot etwas sagt |
| Angriffslust (Aggression) | reserviert für eigenständiges Verhalten |

## Kampfprofile (`content/bots/profiles.json`)

Es gibt 18 Profile, eines für jede Rolle, die eine Klasse ausfüllen kann. Jedes Profil legt fest:

* den Talentbaum, in den die Punkte fließen,
* die Haltung oder Gestalt (Verteidigungshaltung, Bärengestalt …),
* die Buffs, die außerhalb des Kampfes auf der Gruppe gehalten werden,
* eine **Prioritätsliste** von Fähigkeiten mit Bedingungen, z. B. „Hinrichten, wenn das Ziel unter 20 % Leben hat“, „Verderbnis, wenn sie fehlt“ oder „Ausweiden ab 5 Kombopunkten“,
* Heilzauber, Wiederbelebung, Spott, Unterbrechung und Begleiter.

Designer können diese Profile ändern, ohne den Code anzufassen.

## Entscheidungen pro Denkschritt

1. **Tot?** Wenn ein Wiederbeleber in der Gruppe lebt, wartet der Bot. Sonst lässt er den Geist frei, läuft zur Leiche und belebt sich wieder.
2. **Bodeneffekt?** Steht der Bot in einer markierten Fläche, läuft er zum nächsten sicheren Punkt in Reichweite seines Ziels und bricht dafür auch einen Zauber ab. Wann er die Gefahr bemerkt, hängt von der Schwierigkeitsstufe ab (siehe „Bosskämpfe“).
3. **Zaubert gerade?** Heilungen, die nicht mehr nötig sind, bricht er ab.
4. **Anschluss an den Anführer:** Der Bot folgt über Zonen und Instanzen und holt auf, wenn er zu weit zurückliegt.
5. **Notfall:** Heiltrank oder Volksfähigkeit.
6. **Heilen:** Er wählt das Gruppenmitglied mit dem niedrigsten Lebensanteil, wobei für den Tank eine höhere Schwelle gilt. Danach wählt er den Zauber:
   * Schild für den Tank,
   * HoT vorbeugend,
   * die schnellste Heilung im Notfall,
   * sonst die passende Heilung, die möglichst wenig überheilt.
7. **Unterbrechen:** Feindliche Zauber werden unterbrochen. Angesagte Pflicht-Zauber eines Bosses haben Vorrang. Ob der Bot rechtzeitig reagiert, wird einmal pro Zauber nach seinem Können entschieden.
8. **Kämpfen:**
   * **Tank:** Lose Gegner, die andere angreifen, holt er per Spott zurück, den Heiler zuerst. Danach greift er das Ziel des Anführers an.
   * **Ersatz-Tank:** Fällt der Tank, übernimmt genau ein Bot.
     * Bevorzugt sind Krieger, Paladin oder Druide, sonst der Schadensverursacher mit dem meisten Leben. Heiler übernehmen nie.
     * Der Bot sagt es in der Gruppe an und spottet, wenn er es kann.
   * **Schadensverursacher:** Sie greifen das Ziel des Tanks an, sonst das des Anführers, sonst Gegner, die den Heiler angreifen. Vorrangige Adds eines Bosses greifen sie zuerst an. Gegner unter Kontrolleffekten (Verwandlung, Solarplexus) lassen sie in Ruhe. Liegt ihr Kampfplatz in einer Fläche, suchen sie sich einen sicheren Platz am Ziel. Im Dungeon warten sie, bis der Tank Bedrohung aufgebaut hat. Nahkämpfer stellen sich hinter das Ziel. Fernkämpfer halten Abstand und gehen auf Distanz, wenn ein Gegner festgehalten ist.
9. **Wiederbeleben** toter Gruppenmitglieder nach dem Kampf.
10. **Pflege:** Haltung oder Gestalt einnehmen, Begleiter rufen, Buffs erneuern.
11. **Rasten:** Essen und Trinken, im Dungeon mit höheren Schwellen. Der Heiler sagt in der Gruppe Bescheid, wenn sein Mana knapp wird.
12. **Folgen** in Formation hinter dem Anführer – oder **eigenständig spielen** (siehe unten).

## Gruppenchat-Befehle

Bots verstehen natürliche Sätze im Gruppenchat. Wird ein Bot mit Namen angesprochen, reagiert nur er.

| Beispiel | Wirkung |
|---|---|
| „Folgt mir“ | folgen |
| „Wartet hier“ | an der Stelle bleiben |
| „Angriff!“ | alle greifen das Ziel des Spielers an |
| „Tank, zieh!“ | der Tank eröffnet den Kampf |
| „Wir gehen in den Dungeon.“ | Dungeonmodus: vorsichtiger, der Tank kündigt an, Heiler und Schadensverursacher antworten ihm |
| „Pause“ | rasten |
| „passiv“ / „aggressiv“ / „normal“ | Haltung der Bots |
| „Status“ | jeder Bot meldet Leben und Mana |
| „Tank, greif den Boss an.“ | nur der Tank greift den nächsten Boss an |
| „Heiler, bitte Mana auffüllen.“ | der Heiler trinkt (im Kampf: „Erst den Kampf beenden!“) |
| „Magier, kontrolliere den linken Gegner.“ | Verwandlung auf den Gegner links vom Blick des Anführers, Mond-Markierung, andere greifen ihn nicht an |
| „Alle zurückziehen.“ | Kampf abbrechen, zum Anführer laufen (8 s) |
| „Fokus auf …“, „Unterbrecht!“, „Heil mich“, „Verteilt euch“, „verteidigen“ | Fokusziel, Unterbrechen mit Vorrang, gezielte Heilung, Abstand bei Flächenschaden, Verteidigungshaltung |

Die Anrede steht vor dem ersten Komma oder Doppelpunkt (Name, Rolle wie „Tank“/„Heiler“ oder Klasse wie „Magier“);
ohne Anrede oder mit „Alle“ gilt der Befehl allen. Dieselben Befehle gibt es als Befehlsleiste im Unity-Client
(`C_BotCommand`, Empfänger „Alle“ oder ein einzelner Bot) und im Konsolen-Client (`cmd <befehl> [botId]`).
Zielmarkierungen (`C_SetMarker`: ☠ Totenkopf = zuerst, ✖ Kreuz = danach, ☾ Mond = kontrollieren) beachten alle Bots.

## Bots reden miteinander

Die Dialoge liegen in `content/bots/dialog.json`, sortiert nach Ereignis, Rolle und Persönlichkeit, mit Platzhaltern wie `{tank}`, `{healer}`, `{leader}`, `{target}` oder `{level}`. Antworten kommen zeitversetzt, damit sich ein Gespräch ergibt:

> **Spieler:** Wir gehen in den Dungeon.
> **Darmund (Tank):** Alles klar, Lysandra. Keiner greift an, bevor ich Bedrohung aufgebaut habe.
> **Vaeeth (Heiler):** Bin bereit. Darmund, ich behalte dich im Auge. Alle anderen: Bleibt aus den Flächen raus.
> **Faendra (Nahkampf):** Ich gehe hinter die Gegner. Vaeeth, vergiss mich nicht.
> **Vaeeth:** Verstanden, Darmund.

Dieser Ausschnitt stammt aus einer echten Testsitzung über das Netzwerk. Weitere Ereignisse: knappes Mana mit Antwort des Tanks, Stufenaufstieg mit Glückwünschen, Bosssieg, Tod, Wiederbelebung, Beute-Verbesserung, Plaudern im Leerlauf und Rufe im Zonenchat.

## „KI-Mitspieler suchen“

`BotDirector.FindCandidates` schlägt Bots vor, die fehlende Rollen der Gruppe füllen. Zuerst kommen **bekannte** Bots, mit denen das Konto schon gespielt hat, dann neue. Ein neuer Bot ist so gebaut:

* Volk und Klasse passen zur Fraktion.
* Seine Stufe entspricht der des Spielers.
* Er kann die Rolle auf dieser Stufe wirklich ausfüllen; ein Druide wird zum Beispiel erst ab Stufe 10 als Tank angeboten (Bärengestalt).
* Sein Name passt zum Volk, dazu kommen eine Persönlichkeit und eine Begrüßung.

Eingeladene Bots kommen angelaufen und grüßen. Beim Entlassen verabschieden sie sich und werden gespeichert.

## Gruppenfinder „Abenteurer suchen“ (9.5)

Spieler (allein oder als Gruppe) melden sich für Dungeons, Schlachtzüge oder „Quests in …“ an und wählen ihre Rollen
sowie einen KI-Modus: **Nur Menschen**, **Menschen bevorzugt** (KI füllt nach 45 s auf) oder **Sofort mit KI**.
Zuerst werden echte Spieler zusammengebracht. Jeder Vorschlag muss von allen Menschen innerhalb von 30 s bestätigt
werden; wer ablehnt, verlässt die Suche, die anderen behalten ihren Platz. Fehlende Rollen besetzt der Finder mit
Lieblingsbegleitern, Bekannten oder neuen KI-Abenteurern passender Stufe; die Gruppe erhält Gruppenbeute und wird
direkt in die Instanz gebracht. Meldet sich später ein Mensch für dieselbe Aktivität, übernimmt er geordnet den Platz
eines KI-Mitspielers seiner Rolle – **nie während eines Bosskampfs**; der Bot verabschiedet sich.

Weitere Gruppenwerkzeuge: Bereitschaftscheck (Bots antworten ehrlich nach Leben/Mana), Abstimmung zum Entfernen
(Mehrheit der anderen Menschen; KI-Mitspieler entlässt der Anführer direkt), Rollenwahl für Menschen und Bots
(nur Rollen, für die die Klasse ein Kampfprofil hat), Schlachtzug mit acht Untergruppen.

## Eigenständige Bewohner

Jede Zone hat eine konfigurierbare Zahl eigenständiger Bots (`ambientBots` in der Zonendefinition). Sie jagen Gegner ihrer Stufe, plündern, rasten, kehren nach einer Weile ins Dorf zurück, verkaufen Plunder und rufen im Zonenchat („Suche Gruppe für die Wurzelkrypta!“). Dabei steigen sie selbst auf. Spieler können sie per `/inv Name` einladen; ob sie zusagen, entscheidet der Bot nach Persönlichkeit und Stufenabstand.

**Tagesablauf (9.4):** In der Stadt arbeitet ein Bewohner eine Besorgungsliste ab – fertige Quests abgeben (mit sinnvoller
Belohnungswahl), beim Händler verkaufen, beim Klassenlehrer lernen, passende Quests annehmen (nur Töten/Sammeln, Stufe
passend, höchstens vier), dazu zufällig Gasthaus, Auktionshaus oder Bank. Bewohner unterhalten sich untereinander
(`town_chat` / `town_chat_reply`). Draußen jagen sie bevorzugt Questziele und Träger von Questgegenständen. Ist ein Bewohner
der Zone entwachsen (Stufe > Zonenmaximum + 2), zieht er in eine passende Zone weiter oder steht künftig als Begleiter
in der Suche bereit; neue Bewohner füllen die Zone auf.

**Simulationsstufen:** Zonen mit menschlichen Spielern laufen voll (20 Ticks/s). Ohne Menschen läuft eine Zone reduziert
(5 Schritte/s, Bots denken zusätzlich seltener) und ruht nach fünf Minuten: Dann wird nichts mehr einzeln simuliert,
sondern alle 30 Sekunden ein Hintergrundschritt ausgeführt – Gegner und Vorkommen erscheinen neu, Bewohner erholen sich,
werden bei Bedarf an der Leiche wiederbelebt und erhalten pauschal Erfahrung und Beutegeld (≈ eine Beute je 45 s).
Betritt ein Mensch die Zone, läuft sie sofort wieder voll. Abschaltbar mit `ER_ZONE_LOD=false`.

## Bosskämpfe in 5er-Dungeons (Block G)

Die Kampfsteuerung (`EncounterSystem`) führt jeden Bosskampf:

- Start, Sieg oder Niederlage, Phase und Kampfzeit, die Wut nach `enrageAfter`.
- Bodeneffekte mit Vorwarnung und Pflicht-Zauber.
- Eine Auswertung je Kampf: Dauer, Tode, Treffer durch Bodeneffekte je Teilnehmer, Unterbrechungen.

Raids mit mehr als 5 Spielern folgen später.

Wie zuverlässig Bots die Mechaniken meistern, hängt von der Schwierigkeitsstufe ab (`BotBrain.Encounter.cs`):

| Stufe | Bemerkt Bodeneffekt nach | übersieht ihn | Pflicht-Unterbrechung gelingt (wenn bereit) |
|---|---|---|---|
| Anfänger | 0,8–2,4 s | 25 % | ca. 46–57 % |
| Normal | 0,35–1,3 s | 8 % | ca. 60–70 % |
| Erfahren | 0,15–0,7 s | 2 % | ca. 74–85 % |
| Veteran | 0–0,3 s | nie | ca. 88–95 % |

### Abstimmung mit der Kampfsimulation

`EncounterTests.BalanceAcrossDifficulties` simuliert jeden Boss der Wurzelkrypta (Stufe des Bosses + 1).

- **Gruppe:** ein aufmerksamer Spieler (Krieger, weicht nach etwa 0,5 s aus) und vier KI-Mitspieler: Paladin als Tank, Priester als Heiler, Magier, Schurke.
- **Mehr Durchläufe:** `ER_BALANCE_RUNS=5 dotnet test --filter BalanceAcrossDifficulties`.
- **Messung:** je 5 Kämpfe. Die letzte Spalte zählt erfolgreiche von allen Pflicht-Unterbrechungen.

| Boss | Bots | Siege | Dauer | Tode | Bodentreffer der Bots | Unterbrechungen |
|---|---|---|---|---|---|---|
| Moderkeim | Anfänger | 5/5 | 79 s | 0,8 | 35,8 | – |
| Moderkeim | Normal | 5/5 | 60 s | 0,0 | 7,4 | – |
| Moderkeim | Veteran | 5/5 | 66 s | 0,2 | 2,0 | – |
| Schwester Elvaine | Anfänger | 5/5 | 53 s | 0,0 | 2,0 | 8/10 |
| Schwester Elvaine | Normal | 5/5 | 53 s | 0,0 | 0,0 | 7/9 |
| Schwester Elvaine | Veteran | 5/5 | 51 s | 0,0 | 0,0 | 8/9 |
| Wurzelfürst Morrak | Anfänger | 3/5 | 83 s | 2,4 | 13,0 | – |
| Wurzelfürst Morrak | Normal | 5/5 | 77 s | 0,2 | 3,4 | – |
| Wurzelfürst Morrak | Veteran | 5/5 | 69 s | 0,0 | 2,0 | – |

**Ergebnis der Abstimmung:**

- Ab „Normal“ ist die Wurzelkrypta mit KI-Mitspielern zuverlässig schaffbar. Die Wut nach 180–240 s trifft nur deutlich zu langsame Gruppen.
- Anfänger stehen vier- bis siebenmal öfter in Bodeneffekten und verlieren gegen Morrak manchmal.
- Moderkeim als erster Boss bleibt nachsichtig.
- Elvaines Heilung wird auch von Anfängern meist unterbrochen, weil Magier und Schurke beide unterbrechen können.

## Grundausrüstung

Bots haben keine tausend handgefertigten Gegenstände. Deshalb tragen sie eine **virtuelle Grundausrüstung**, deren Werte etwa grüner Ausrüstung ihrer Stufe entsprechen (`BotGear`). Jeder echte Gegenstand, den ein Bot anlegt, ersetzt den virtuellen Anteil seines Platzes. Bots verbrauchen beim Rasten keine Nahrungsmittel.

## Getestet

`tests/EternalRealms.Tests/BotTests.cs` prüft unter anderem:

* Alle 18 Profile gewinnen allein gegen einen Gegner ihrer Stufe.
* Eine Gruppe aus Spieler und 4 Bots besiegt den ersten Dungeonboss mit Phasenwechsel.
* `EncounterTests` prüft die Mechaniken (Vorwarnung, liegende Flächen, Wut, Niederlage), dass Veteranen besser ausweichen als Anfänger, Pflicht-Unterbrechungen, vorrangige Adds, den Ersatz-Tank und die Balance über alle Stufen.
* Der Tank hält die Gegner, und der Heiler heilt.
* Bots folgen, warten auf Befehl, reden in der richtigen Reihenfolge, steigen auf und lernen dabei Fähigkeiten.
* Eigenständige Bots sammeln selbst Erfahrung.
