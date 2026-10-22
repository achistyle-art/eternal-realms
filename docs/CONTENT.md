# Inhalte erstellen

Alle Spielinhalte sind Daten in `content/`. Server und Client laden dieselben Dateien. Beim Serverstart prüft `ContentDatabase.Validate()` jeden Querverweis; ein Fehler (Quest verweist auf unbekannten NPC, Kreatur erscheint nirgends, Quest ohne Ziele …) verhindert den Start und lässt `dotnet test` fehlschlagen. **Platzhalterquests ohne spielbaren Inhalt werden so technisch verhindert.**

## Dateien

| Pfad | Inhalt |
|---|---|
| `factions.json`, `races.json`, `classes.json`, `reputations.json` | Fraktionen, Völker (Lore, Startpunkt, Volksfähigkeiten, Aussehensoptionen, Namenssilben), Klassen (Werte, Rüstung, Waffen, Startfähigkeiten) |
| `abilities/*.json`, `auras/*.json`, `talents/*.json`, `bots/profiles.json` | **erzeugt** von `tools/gen_class_content.py` |
| `items.json` | **erzeugt** von `tools/gen_items.py` (Formeln in `tools/itemlib.py`) |
| `zones/westmark.json`, `zones/rotsand.json`, `quests/westmark.json`, `quests/rotsand.json`, `*/stufe12_20.json` | **erzeugt** von `tools/gen_world_i.py` (Regionen für Stufe 12–20) |
| `manifest.json` | Name, Content-Version, `levelCap` (derzeit 20, höchstens 60) |
| `riding.json` | Reitstufen: Fertigkeit, Name, Mindeststufe, Preis, höchstes Tempo (derzeit Lehrling ab Stufe 20, Experte ab 60) |
| `mounts.json` | Reittiere; Rufbelohnungen mit `requiredReputation` und `requiredReputationValue` (die Zügel erben die Anforderung) |
| `zones/wurzelkrypta_heroisch.json`, `creatures/heroisch.json` | **erzeugt** von `tools/gen_world_i.py` aus der normalen Krypta: `heroic`, `requiredLevel`, `lockoutDays` |
| `creatures/*.json` | Monster, NPCs, Begleiter – Stufen, Fähigkeiten mit Intervallen, Bossphasen, Beute, Händlerwaren, Tagesabläufe |
| `objects.json` | Erzadern, Kisten, Portale |
| `zones/*.json` | eine Datei pro Region: Gebiete, Friedhöfe, Spawns (Anzahl, Radius, Patrouille, Respawn), NPCs, Objekte, eigenständige Bots |
| `quests/*.json` | Quests je Zone |
| `travel/*.json` | Flugpunkte und Routen |
| `bots/dialog.json` | Sätze der KI-Mitspieler |

Positionen sind `[x, y, z]` oder kurz `[x, z]` in Metern; die Zone reicht von 0 bis `size`.

## Content-Werkzeug

```bash
dotnet run --project src/EternalRealms.ContentTool -- pruefen                 # Content prüfen (auch in der CI)
dotnet run --project src/EternalRealms.ContentTool -- stats                   # Umfang, Regionen, Stufen
dotnet run --project src/EternalRealms.ContentTool -- verweise item_wolf_fang # Wo wird eine ID benutzt?
dotnet run --project src/EternalRealms.ContentTool -- umbenennen item_wolf_fang item_waldwolf_fang [--probe]
dotnet run --project src/EternalRealms.ContentTool -- neu item item_feder "Glänzende Feder" --price 12
dotnet run --project src/EternalRealms.ContentTool -- neu creature mob_kaefer "Panzerkäfer" --level 4
dotnet run --project src/EternalRealms.ContentTool -- neu quest q_kaefer "Käferplage" --giver npc_as_durgash --target mob_kaefer
```

- **`umbenennen`** ändert die ID in allen Content-Dateien und in den Generatoren (`tools/gen_*.py`).
  - Für gespeicherte Arten (Gegenstand, Quest, Fähigkeit, Erfolg, Region) legt es eine Content-Migration an; Spielstände ziehen dann beim nächsten Start mit (siehe [Updates](UPDATES.md)).
  - Ist der Content danach ungültig, werden alle Dateien zurückgesetzt.
- **`neu`** legt Einträge aus geprüften Vorlagen in eigenen Dateien an (`items/eigene.json`, `creatures/eigene.json`, `quests/eigene.json`), nie in erzeugten Dateien.
- **Versionsnummer** des Contents in `content/manifest.json` bei jedem Patch erhöhen.

## Fähigkeiten mit Rängen

`tools/gen_class_content.py` beschreibt jede Fähigkeit einmal; das Skript erzeugt alle Ränge über eine Skalierungskurve (`dmg(Stufe, Faktor)`, `cost(Stufe, Faktor)`). Beispiel:

```python
ranked(c, A(c, id="mage_fireball", name="Feuerball", ..., castTime=3.0, maxRange=35, school="Fire",
  effects=[eff("SchoolDamage", school="Fire", coefficient=1.0)]),
  LV_DIRECT, [1.6], cost_c=1.5)   # Ränge auf Stufe 1, 6, 12 … 60
```

Danach `python3 tools/gen_class_content.py` und `dotnet test`.

## Questtypen

`objectives[].type`: `Kill`, `Collect` (Beute mit `quest`-Bindung oder Objekte), `Explore` (Gebiet), `Interact` (Objekt), `Talk`, `Deliver` (mit `providedItems`), `Escort` (mit `escort`: Wegpunkte, Hinterhalte, optionale Verteidigungszeit). Weitere Felder: `requires` (Kette), `exclusiveWith` (Entscheidung), `rewardChoices` (Wahlbelohnung), `rewardReputation`, `rewardAbility`, `class`/`race`/`faction`, `category` (`Main`, `Side`, `Class`, `Dungeon`, `Raid`, `Profession`, `Faction`), `suggestedPlayers`, `daily` (Tagesquest: nach der Abgabe bis zum nächsten Reset um 05:00 UTC gesperrt; darf nichts freischalten).

## Bosse

```json
"phases": [
  { "healthBelow": 1.01, "name": "Sporen", "yell": "…", "abilities": [ { "ability": "mob_spore_burst", "interval": 14 } ] },
  { "healthBelow": 0.5,  "name": "Brut",   "summonAdds": [ { "creature": "dmob_sporeling", "count": 3 } ] }
]
```

`targetRule` einer Kreaturenfähigkeit: `Current`, `Self`, `Random`, `Farthest`, `LowestHealthAlly`, `Tank`.

### Mechaniken für 5er-Dungeons (Block G)

```json
"enrageAfter": 240,
"abilities": [
  { "ability": "mob_root_eruption", "interval": 16, "initialDelay": 4, "announce": "Wurzeln brechen unter Euren Füßen hervor!",
    "ground": { "placement": "EachPlayer", "radius": 4, "warning": 3, "duration": 0 } },
  { "ability": "mob_dark_mending", "interval": 16, "targetRule": "Self", "healthBelow": 0.85, "mustInterrupt": true }
],
"summonAdds": [ { "creature": "dmob_root_crawler", "count": 2, "priority": true } ]
```

- **`ground` – Bodeneffekt statt direktem Zauber:**
  - Ein Kreis erscheint mit Vorwarnung (`warning`, mindestens 1 s). Danach wendet er die Fähigkeit auf jeden Gegner des Bosses an, der noch darin steht.
  - `duration` 0 heißt einmalige Explosion. Ein größerer Wert lässt die Fläche liegen; sie trifft dann alle `tickInterval` Sekunden.
  - `placement`: `RandomPlayer` (mit `count` verschiedenen Zielen), `EachPlayer`, `Tank`, `Farthest`, `Self`.
  - Die Nutzlast-Fähigkeiten (`mob_spore_carpet`, `mob_shadow_ring`, `mob_root_eruption`) entstehen in `tools/gen_class_content.py`.
- **`mustInterrupt`:**
  - Der Zauber wird angesagt, und die KI-Mitspieler unterbrechen ihn bevorzugt.
  - Er braucht eine Zauberzeit von mindestens 1,5 s; die Prüfung erzwingt das.
  - Die Auswertung zählt unterbrochene und durchgekommene Zauber.
- **`announce`:** Warnung im Chat und als Einblendung. `{boss}` und `{ability}` werden ersetzt.
- **`enrageAfter`:** Nach so vielen Sekunden Kampf wirkt der Boss `enrageAbility` (Standard `mob_berserk`: +150 % Schaden, +50 % Tempo).
- **`priority`** an einem beschworenen Add: Die KI-Mitspieler wechseln darauf, der Spieler sieht es im Kampfgeschehen.

## Berufe (`content/professions.json`, `content/recipes.json`)

Erzeugt von `tools/gen_professions.py` (aufgerufen aus `tools/gen_items.py`). Neun Hauptberufe – Bergbau, Kräuterkunde,
Kürschnerei, Schmiedekunst, Lederverarbeitung, Schneiderei, Alchemie, Ingenieurskunst, Verzauberkunst –, davon höchstens
zwei je Charakter, dazu die Nebenberufe Angeln, Kochen und Erste Hilfe. Ränge: Lehrling (75), Geselle (150, Stufe 10),
Experte (225, Stufe 20), Meister (300, Stufe 35); der nächste Rang ist ab Fertigkeit „Obergrenze − 25“ lernbar.

| Feld (Rezept) | Bedeutung |
|---|---|
| `skillRequired` / `yellow` / `green` / `gray` | Farbstufen: orange = sicher +1, gelb 75 %, grün 25 %, grau 0 % |
| `reagents`, `creates` | Zutaten und Ergebnis; Verzauberungen haben stattdessen `enchant` (Ausrüstungsplatz + Modifikatoren) |
| `station` | Werkbank in 8 m Umkreis: `anvil` (Amboss), `forge` (Schmelzofen), `fire` (Kochfeuer) |
| `tool` | Werkzeug in der Tasche (Schmiedehammer, Runenverzierte Kupferrute …) |

Rohstoffe gibt es in vier Stufen (Kupfer/Zinn/Eisen/Sternsilber, Sonnenblatt … Sternenlilie, Leinen … Zauberstoff).
Abbaustellen (`content/objects/professions.json`) brauchen Beruf, Fertigkeit und ggf. Werkzeug. Kürschnern geht an
geplünderten Tierleichen (Fertigkeit: Stufe ≤ 10 → 1, sonst (Stufe − 10) × 10). Humanoide lassen Stoff, Tiere Fleisch
fallen. Entzaubern wandelt ungewöhnliche bis epische Ausrüstung in Staub und Essenzen. Verzauberungen werden am
Gegenstand gespeichert (`character_items.enchant`) und wirken wie Gegenstandswerte. Jeder Startort hat ein
Handwerksviertel mit zwölf Lehrern, einem Werkzeughändler, Amboss, Schmelzofen und Kochfeuer.
