# Inhalte erstellen

Alle Spielinhalte sind Daten in `content/`. Server und Client laden dieselben Dateien. Beim Serverstart prüft `ContentDatabase.Validate()` jeden Querverweis; ein Fehler (Quest verweist auf unbekannten NPC, Kreatur erscheint nirgends, Quest ohne Ziele …) verhindert den Start und lässt `dotnet test` fehlschlagen. **Platzhalterquests ohne spielbaren Inhalt werden so technisch verhindert.**

## Dateien

| Pfad | Inhalt |
|---|---|
| `factions.json`, `races.json`, `classes.json`, `reputations.json` | Fraktionen, Völker (Lore, Startpunkt, Volksfähigkeiten, Aussehensoptionen, Namenssilben), Klassen (Werte, Rüstung, Waffen, Startfähigkeiten) |
| `abilities/*.json`, `auras/*.json`, `talents/*.json`, `bots/profiles.json` | **erzeugt** von `tools/gen_class_content.py` |
| `items.json` | **erzeugt** von `tools/gen_items.py` |
| `creatures/*.json` | Monster, NPCs, Begleiter – Stufen, Fähigkeiten mit Intervallen, Bossphasen, Beute, Händlerwaren, Tagesabläufe |
| `objects.json` | Erzadern, Kisten, Portale |
| `zones/*.json` | eine Datei pro Region: Gebiete, Friedhöfe, Spawns (Anzahl, Radius, Patrouille, Respawn), NPCs, Objekte, eigenständige Bots |
| `quests/*.json` | Quests je Zone |
| `travel/*.json` | Flugpunkte und Routen |
| `bots/dialog.json` | Sätze der KI-Mitspieler |

Positionen sind `[x, y, z]` oder kurz `[x, z]` in Metern; die Zone reicht von 0 bis `size`.

## Fähigkeiten mit Rängen

`tools/gen_class_content.py` beschreibt jede Fähigkeit einmal; das Skript erzeugt alle Ränge über eine Skalierungskurve (`dmg(Stufe, Faktor)`, `cost(Stufe, Faktor)`). Beispiel:

```python
ranked(c, A(c, id="mage_fireball", name="Feuerball", ..., castTime=3.0, maxRange=35, school="Fire",
  effects=[eff("SchoolDamage", school="Fire", coefficient=1.0)]),
  LV_DIRECT, [1.6], cost_c=1.5)   # Ränge auf Stufe 1, 6, 12 … 60
```

Danach `python3 tools/gen_class_content.py` und `dotnet test`.

## Questtypen

`objectives[].type`: `Kill`, `Collect` (Beute mit `quest`-Bindung oder Objekte), `Explore` (Gebiet), `Interact` (Objekt), `Talk`, `Deliver` (mit `providedItems`), `Escort` (mit `escort`: Wegpunkte, Hinterhalte, optionale Verteidigungszeit). Weitere Felder: `requires` (Kette), `exclusiveWith` (Entscheidung), `rewardChoices` (Wahlbelohnung), `rewardReputation`, `rewardAbility`, `class`/`race`/`faction`, `category` (`Main`, `Side`, `Class`, `Dungeon`, `Raid`, `Profession`, `Faction`), `suggestedPlayers`.

## Bosse

```json
"phases": [
  { "healthBelow": 1.01, "name": "Sporen", "yell": "…", "abilities": [ { "ability": "mob_spore_burst", "interval": 14 } ] },
  { "healthBelow": 0.5,  "name": "Brut",   "summonAdds": [ { "creature": "dmob_sporeling", "count": 3 } ] }
]
```

`targetRule` einer Kreaturenfähigkeit: `Current`, `Self`, `Random`, `Farthest`, `LowestHealthAlly`, `Tank`.

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
