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
