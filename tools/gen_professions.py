# Content-Werkzeug (Kapitel 17): Berufe, Rezepte, Rohstoffe, Abbaustellen, Lehrer und Werkbänke.
# Wird von tools/gen_items.py aufgerufen (Gegenstände landen in content/items.json) und schreibt zusätzlich
# content/professions.json, content/recipes.json, content/objects/professions.json und ergänzt Lehrer/Werkbänke
# in den Startorten. Deterministisch – CI prüft, dass die Ausgabe dem eingecheckten Stand entspricht.
import json, os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "content")
TIERS = [  # (Fertigkeit ab, Gegenstandsstufe, Mindeststufe)
    (1, 5, 1), (75, 15, 10), (150, 25, 20), (225, 35, 30),
]
RANKS = [{"name": "Lehrling", "maxSkill": 75, "requiredLevel": 5, "cost": 1000},
         {"name": "Geselle", "maxSkill": 150, "requiredLevel": 10, "cost": 5000},
         {"name": "Experte", "maxSkill": 225, "requiredLevel": 20, "cost": 50000},
         {"name": "Meister", "maxSkill": 300, "requiredLevel": 35, "cost": 250000}]
PROFESSIONS = [
    ("mining", "Bergbau", "Gathering", "Erz und Stein abbauen und in der Schmelze zu Barren verarbeiten.", "item_tool_mining_pick"),
    ("herbalism", "Kräuterkunde", "Gathering", "Kräuter sammeln – Grundlage aller Tränke.", None),
    ("skinning", "Kürschnerei", "Gathering", "Leder von erlegten Tieren gewinnen.", "item_tool_skinning_knife"),
    ("blacksmithing", "Schmiedekunst", "Crafting", "Waffen und schwere Rüstungen am Amboss schmieden.", "item_tool_blacksmith_hammer"),
    ("leatherworking", "Lederverarbeitung", "Crafting", "Lederrüstungen und Taschen fertigen.", None),
    ("tailoring", "Schneiderei", "Crafting", "Stoffrüstungen, Umhänge und Taschen nähen.", None),
    ("alchemy", "Alchemie", "Crafting", "Tränke und Elixiere brauen.", None),
    ("engineering", "Ingenieurskunst", "Crafting", "Büchsen, Brillen und Bauteile tüfteln.", "item_tool_blacksmith_hammer"),
    ("enchanting", "Verzauberkunst", "Crafting", "Gegenstände entzaubern und Ausrüstung dauerhaft verbessern.", None),
    ("fishing", "Angeln", "Secondary", "In Fischschwärmen angeln.", "item_tool_fishing_pole"),
    ("cooking", "Kochen", "Secondary", "Am Feuer Speisen zubereiten, die satt machen.", None),
    ("first_aid", "Erste Hilfe", "Secondary", "Verbände aus Stoff anfertigen.", None),
]

def build(items, armor, weapon, misc):
    recipes, objects = [], []
    I = {}  # Kurzname → Gegenstands-ID

    def mat(key, name, cat="TradeGoods", tier=0, stack=20, desc=""):
        ilvl = TIERS[tier][1]
        d = misc("item_" + key, name, cat, "Common", stack=stack, sell=2 + tier * tier * 6 + tier * 4, buy=0, desc=desc, ilvl=ilvl)
        I[key] = d["id"]; return d

    # ---- Werkzeuge und Händlerwaren
    for key, name, buy, desc in (("tool_mining_pick", "Bergbauhacke", 100, "Werkzeug für den Bergbau."),
                                 ("tool_skinning_knife", "Kürschnermesser", 100, "Werkzeug für die Kürschnerei."),
                                 ("tool_blacksmith_hammer", "Schmiedehammer", 120, "Werkzeug für Schmiede und Ingenieure."),
                                 ("tool_fishing_pole", "Angelrute", 80, "Ohne Angelrute kein Fang.")):
        d = misc("item_" + key, name, "Misc", "Common", sell=buy // 4, buy=buy, desc=desc); I[key] = d["id"]
    for key, name, buy in (("empty_vial", "Leere Phiole", 4), ("coarse_thread", "Grobes Garn", 10), ("fine_thread", "Feines Garn", 40),
                           ("salt", "Salz", 5), ("mild_spices", "Milde Gewürze", 8), ("flux", "Flussmittel", 15)):
        d = misc("item_" + key, name, "TradeGoods", "Common", stack=20, sell=max(1, buy // 4), buy=buy); I[key] = d["id"]

    # ---- Rohstoffe je Stufe
    ORES = ["Kupfererz", "Zinnerz", "Eisenerz", "Sternsilbererz"]
    BARS = ["Kupferbarren", "Bronzebarren", "Eisenbarren", "Sternsilberbarren"]
    STONES = ["Grober Stein", "Rauer Stein", "Schwerer Stein", "Fester Stein"]
    HERBS = [("Sonnenblatt", "Wurzelkraut"), ("Silbermoos", "Bachminze"), ("Feuerdorn", "Grabmohn"), ("Mondsichelkraut", "Sternenlilie")]
    LEATHER = ["Leichtes Leder", "Mittleres Leder", "Schweres Leder", "Dickes Leder"]
    CLOTH = ["Leinenstoff", "Wollstoff", "Seidenstoff", "Zauberstoff"]
    BOLTS = ["Leinenballen", "Wollballen", "Seidenballen", "Zauberstoffballen"]
    MEAT = ["Zähes Wildfleisch", "Wildbret", "Dunkles Fleisch", "Bärenfleisch"]
    FISH = ["Bachbarsch", "Schlammkarpfen", "Felsenforelle", "Silberlachs"]
    DUST = ["Seltsamer Staub", "Seelenstaub", "Visionenstaub", "Traumstaub"]
    ESSENCE = ["Geringe Magieessenz", "Geringe Astralessenz", "Geringe Mystikessenz", "Geringe Netheressenz"]
    for t in range(4):
        mat(f"ore{t}", ORES[t], tier=t); mat(f"bar{t}", BARS[t], tier=t); mat(f"stone{t}", STONES[t], tier=t)
        mat(f"herb{t}a", HERBS[t][0], "Reagent", t); mat(f"herb{t}b", HERBS[t][1], "Reagent", t)
        mat(f"leather{t}", LEATHER[t], tier=t); mat(f"cloth{t}", CLOTH[t], tier=t); mat(f"bolt{t}", BOLTS[t], tier=t)
        mat(f"meat{t}", MEAT[t], tier=t); mat(f"fish{t}", FISH[t], tier=t)
        mat(f"dust{t}", DUST[t], "Reagent", t); mat(f"essence{t}", ESSENCE[t], "Reagent", t, stack=10)
    mat("tin_bar", "Zinnbarren", tier=1)

    def recipe(prof, rid, name, skill, reagents, creates=None, count=1, station=None, tool=None, cost=None, cast=3.0, enchant=None):
        r = {"id": rid, "profession": prof, "name": name, "skillRequired": skill, "yellow": skill + 25, "green": skill + 40, "gray": skill + 55,
             "reagents": [{"item": I.get(k, k), "count": n} for k, n in reagents], "castTime": cast,
             "trainerCost": cost if cost is not None else max(10, skill * skill // 4)}
        if creates: r["creates"] = {"item": creates, "count": count}
        if station: r["station"] = station
        if tool: r["tool"] = I.get(tool, tool)
        if enchant: r["enchant"] = enchant
        recipes.append(r); return r

    # ---- Bergbau: Schmelzen
    recipe("mining", "smelt_copper", "Kupfer schmelzen", 1, [("ore0", 1)], I["bar0"], station="forge", cast=2)
    recipe("mining", "smelt_tin", "Zinn schmelzen", 65, [("ore1", 1)], I["tin_bar"], station="forge", cast=2)
    recipe("mining", "smelt_bronze", "Bronze schmelzen", 65, [("bar0", 1), ("tin_bar", 1)], I["bar1"], count=2, station="forge", cast=2)
    recipe("mining", "smelt_iron", "Eisen schmelzen", 125, [("ore2", 1)], I["bar2"], station="forge", cast=2)
    recipe("mining", "smelt_starsilver", "Sternsilber schmelzen", 175, [("ore3", 1)], I["bar3"], station="forge", cast=2)

    # ---- Schmiedekunst
    BS = [[("Mace1H", "Kupferstreitkolben", None), ("Chest", "Kupferkettenweste", "Mail"), ("Wrist", "Kupferarmschienen", "Mail")],
          [("Sword1H", "Bronzeschwert", None), ("Legs", "Bronzekettenhose", "Mail"), ("Head", "Bronzehelm", "Mail")],
          [("Axe2H", "Eiserne Streitaxt", None), ("Chest", "Eiserne Brustplatte", "Plate"), ("Feet", "Eisenbeschlagene Stiefel", "Plate")],
          [("Sword1H", "Sternsilberklinge", None), ("Legs", "Sternsilberbeinplatten", "Plate"), ("Head", "Sternsilberhelm", "Plate")]]
    for t, lst in enumerate(BS):
        s0, il, req = TIERS[t]
        for i, (slot, name, at) in enumerate(lst):
            key = f"bs_{t}_{i}"
            q = "Rare" if t == 3 and i == 0 else "Uncommon"
            stats = {"Strength": 2 + t * 2, "Stamina": 1 + t * 2}
            d = weapon("item_" + key, name, slot, il + 2, req, 2.6 if slot.endswith("2H") else 2.0, q, stats) if at is None else armor("item_" + key, name, slot, at, il + 2, req, q, stats)
            recipe("blacksmithing", key, name, s0 + i * 15, [(f"bar{t}", 4 + i * 2), (f"stone{t}", 1 + i)], d["id"], station="anvil", tool="tool_blacksmith_hammer", cast=4)
        recipe("blacksmithing", f"bs_{t}_stone", f"{['Rauer', 'Grober', 'Schwerer', 'Fester'][t]} Wetzstein", s0, [(f"stone{t}", 1)],
               misc(f"item_bs_{t}_whetstone", f"{['Rauer', 'Grober', 'Schwerer', 'Fester'][t]} Wetzstein", "TradeGoods", "Common", stack=20, sell=3 + t * 8, ilvl=il)["id"],
               station="anvil", tool="tool_blacksmith_hammer", cast=2, cost=10 + t * 50)

    # ---- Lederverarbeitung
    LW = [[("Chest", "Leichte Lederweste"), ("Feet", "Leichte Lederstiefel"), ("Hands", "Leichte Lederhandschuhe")],
          [("Waist", "Gerbergürtel"), ("Legs", "Wildlederhose"), ("Shoulders", "Wildleder-Schulterpolster")],
          [("Chest", "Schwere Lederjacke"), ("Head", "Lederkappe des Fährtenlesers"), ("Wrist", "Schwere Lederarmschienen")],
          [("Chest", "Dicke Waldläuferweste"), ("Legs", "Dicke Lederhose"), ("Hands", "Dicke Lederhandschuhe")]]
    for t, lst in enumerate(LW):
        s0, il, req = TIERS[t]
        for i, (slot, name) in enumerate(lst):
            key = f"lw_{t}_{i}"
            q = "Rare" if t == 3 and i == 0 else "Uncommon"
            d = armor("item_" + key, name, slot, "Leather", il + 2, req, q, {"Agility": 2 + t * 2, "Stamina": 1 + t * 2})
            recipe("leatherworking", key, name, s0 + i * 15, [(f"leather{t}", 3 + i * 2), ("coarse_thread" if t < 2 else "fine_thread", 1)], d["id"], cast=3)
    recipe("leatherworking", "lw_bag", "Lederrucksack", 90, [("leather1", 6), ("coarse_thread", 2)], "item_bag_leather", cast=4)

    # ---- Schneiderei
    TL = [[("Chest", "Leinenrobe"), ("Back", "Leinenumhang")], [("Legs", "Wollhose"), ("Hands", "Wollhandschuhe")],
          [("Chest", "Seidenrobe"), ("Head", "Seidenkapuze")], [("Chest", "Zauberstoffrobe"), ("Shoulders", "Zauberstoffschultern")]]
    for t, lst in enumerate(TL):
        s0, il, req = TIERS[t]
        recipe("tailoring", f"tl_{t}_bolt", BOLTS[t], s0, [(f"cloth{t}", 2)], I[f"bolt{t}"], cast=2, cost=10 + t * 40)
        for i, (slot, name) in enumerate(lst):
            key = f"tl_{t}_{i}"
            q = "Rare" if t == 3 and i == 0 else "Uncommon"
            d = armor("item_" + key, name, slot, "Cloth", il + 2, req, q, {"Intellect": 2 + t * 2, "Spirit": 1 + t * 2})
            recipe("tailoring", key, name, s0 + 10 + i * 15, [(f"bolt{t}", 2 + i), ("coarse_thread" if t < 2 else "fine_thread", 1)], d["id"], cast=3)
    for t, bag in ((0, "item_bag_linen"), (1, "item_bag_wool"), (3, "item_bag_mageweave")):
        recipe("tailoring", f"tl_bag_{t}", next(i["name"] for i in items if i["id"] == bag), TIERS[t][0] + 20, [(f"bolt{t}", 3), ("coarse_thread" if t < 2 else "fine_thread", 2)], bag, cast=4)

    # ---- Alchemie
    heal_items = ["item_minor_healing_potion", "item_lesser_healing_potion"]
    d = misc("item_healing_potion", "Heiltrank", "Consumable", stack=5, sell=60, buy=0, desc="Stellt sofort 280–360 Leben wieder her.", use="item_healing_potion", req=20, ilvl=25)
    heal_items.append(d["id"])
    d = misc("item_greater_healing_potion", "Großer Heiltrank", "Consumable", stack=5, sell=150, buy=0, desc="Stellt sofort 455–585 Leben wieder her.", use="item_greater_healing_potion", req=30, ilvl=35)
    heal_items.append(d["id"])
    elixirs = [("elixir_strength", "Elixier der Löwenkraft"), ("elixir_agility", "Elixier der Behändigkeit"), ("elixir_intellect", "Elixier der Weisheit"), ("elixir_stamina", "Elixier der Standhaftigkeit")]
    for t in range(4):
        s0, il, req = TIERS[t]
        recipe("alchemy", f"al_heal_{t}", "Heiltrank brauen", s0, [(f"herb{t}a", 1), (f"herb{t}b", 1), ("empty_vial", 1)], heal_items[t], cast=3)
        mana = misc(f"item_mana_potion_{t}", ["Schwacher", "Geringer", "", "Großer"][t].strip() + (" " if t != 2 else "") + "Manatrank", "Consumable", stack=5,
                    sell=5 + t * t * 20, buy=0, desc="Stellt sofort Mana wieder her.", use=f"item_mana_potion_r{t + 1}", req=req, ilvl=il)
        recipe("alchemy", f"al_mana_{t}", "Manatrank brauen", s0 + 10, [(f"herb{t}b", 2), ("empty_vial", 1)], mana["id"], cast=3)
        eid, ename = elixirs[t]
        el = misc("item_" + eid, ename, "Consumable", "Uncommon", stack=5, sell=20 + t * 30, buy=0, desc="Eine Stunde lang gestärkt.", use="item_" + eid, req=req, ilvl=il)
        recipe("alchemy", f"al_{eid}", ename, s0 + 25, [(f"herb{t}a", 2), (f"herb{t}b", 1), ("empty_vial", 1)], el["id"], cast=3)

    # ---- Ingenieurskunst
    PIPES = ["Kupferrohr", "Bronzerohr", "Eisenrohr", "Sternsilberrohr"]
    POWDER = ["Grobes Schießpulver", "Raues Schießpulver", "Schweres Schießpulver", "Festes Schießpulver"]
    GUNS = ["Handgemachte Kupferbüchse", "Bronzene Donnerbüchse", "Eiserne Langbüchse", "Sternsilber-Präzisionsbüchse"]
    GOGGLES = ["Tüftlerbrille", "Schweißbrille", "Fliegerbrille des Tüftlers", "Sternsilberlinsen"]
    for t in range(4):
        s0, il, req = TIERS[t]
        pipe = mat(f"pipe{t}", PIPES[t], tier=t); powder = mat(f"powder{t}", POWDER[t], tier=t)
        recipe("engineering", f"en_pipe_{t}", PIPES[t], s0, [(f"bar{t}", 1)], pipe["id"], tool="tool_blacksmith_hammer", cast=2, cost=10 + t * 40)
        recipe("engineering", f"en_powder_{t}", POWDER[t], s0, [(f"stone{t}", 1)], powder["id"], cast=2, cost=10 + t * 40)
        g = weapon(f"item_en_gun_{t}", GUNS[t], "Gun", il + 2, req, 2.4, "Rare" if t == 3 else "Uncommon", {"Agility": 2 + t * 2})
        recipe("engineering", f"en_gun_{t}", GUNS[t], s0 + 20, [(f"pipe{t}", 2), (f"powder{t}", 2), (f"bar{t}", 2)], g["id"], tool="tool_blacksmith_hammer", cast=4)
        gg = armor(f"item_en_goggles_{t}", GOGGLES[t], "Head", "Cloth", il + 2, req, "Uncommon", {"Stamina": 2 + t * 2, "Intellect": 1 + t})
        recipe("engineering", f"en_goggles_{t}", GOGGLES[t], s0 + 35, [(f"leather{t}", 2), (f"bar{t}", 2), (f"powder{t}", 1)], gg["id"], tool="tool_blacksmith_hammer", cast=4)

    # ---- Verzauberkunst: Rute, dann dauerhafte Verzauberungen angelegter Ausrüstung
    rod = misc("item_enchanting_rod", "Runenverzierte Kupferrute", "Misc", "Common", sell=20, desc="Werkzeug der Verzauberkunst.", ilvl=5)
    I["enchanting_rod"] = rod["id"]
    recipe("enchanting", "ench_rod", "Runenverzierte Kupferrute", 1, [("bar0", 1), ("dust0", 1)], rod["id"], cast=3, cost=10)
    ENCH = [("Wrist", "Armschienen", "Stamina", "Ausdauer"), ("Chest", "Brust", "Spirit", "Willenskraft"), ("Feet", "Stiefel", "Agility", "Beweglichkeit"),
            ("MainHand", "Waffe", "Strength", "Stärke")]
    for t in range(4):
        s0, il, req = TIERS[t]
        for i, (slot, sname, stat, statname) in enumerate(ENCH):
            val = 1 + t * 2 + (1 if i == 3 else 0)
            recipe("enchanting", f"ench_{slot.lower()}_{t}", f"{sname} verzaubern – {['Geringe', 'Schwache', 'Mittlere', 'Starke'][t]} {statname}", s0 + i * 10,
                   [(f"dust{t}", 1 + i), (f"essence{t}", 1 if i >= 2 else 0)] if i >= 2 else [(f"dust{t}", 1 + i)], None, tool="enchanting_rod", cast=4,
                   enchant={"slot": slot, "modifiers": [{"type": "StatFlat", "stat": stat, "value": val}], "name": f"+{val} {statname}"})

    # ---- Kochen, Erste Hilfe
    FOOD = [("Gebratenes Wildfleisch", "Gegrillter Bachbarsch"), ("Würziger Wildbreteintopf", "Geräucherter Karpfen"),
            ("Dunkler Fleischspieß", "Forelle in Kräuterkruste"), ("Bärenbraten", "Gedünsteter Silberlachs")]
    for t in range(4):
        s0, il, req = TIERS[t]
        for j, (src, name) in enumerate(((f"meat{t}", FOOD[t][0]), (f"fish{t}", FOOD[t][1]))):
            f = misc(f"item_food_{t}_{j}", name, "Consumable", stack=20, sell=3 + t * 10, buy=0, desc="Stellt Leben wieder her und macht satt.", use=f"item_cooked_food_r{t + 1}", req=req, ilvl=il)
            recipe("cooking", f"cook_{t}_{j}", name, s0 + j * 10, [(src, 1), ("mild_spices" if j == 0 else "salt", 1)], f["id"], station="fire", cast=2, cost=10 + t * 40)
        b = misc(f"item_bandage_{t}", ["Leinenverband", "Wollverband", "Seidenverband", "Zauberstoffverband"][t], "Consumable", stack=20, sell=2 + t * 8, buy=0,
                 desc="Heilt das Ziel. Danach eine Minute lang nicht erneut möglich.", use=f"item_bandage_r{t + 1}", req=req, ilvl=il)
        recipe("first_aid", f"fa_bandage_{t}", b["name"], s0, [(f"cloth{t}", 1 if t == 0 else 2)], b["id"], cast=2, cost=10 + t * 40)

    # ---- Abbaustellen und Werkbänke
    for t in range(4):
        s0 = TIERS[t][0]
        objects.append({"id": f"obj_prof_vein{t}", "name": ORES[t].replace("erz", "vorkommen"), "model": "ore_vein", "profession": "mining", "skillRequired": s0 if t else 1,
                        "interactTime": 2.5, "loot": [{"item": I[f"ore{t}"], "chance": 1.0, "min": 1, "max": 3}, {"item": I[f"stone{t}"], "chance": 0.5, "min": 1, "max": 2}]})
        for k in "ab":
            name = HERBS[t][0 if k == "a" else 1]
            objects.append({"id": f"obj_prof_herb{t}{k}", "name": name, "model": "herb", "profession": "herbalism", "skillRequired": s0 if t else 1, "interactTime": 2.0,
                            "loot": [{"item": I[f"herb{t}{k}"], "chance": 1.0, "min": 1, "max": 3}]})
        objects.append({"id": f"obj_prof_fishpool{t}", "name": f"Schwarm: {FISH[t]}", "model": "fish_pool", "profession": "fishing", "skillRequired": s0 if t else 1,
                        "interactTime": 4.0, "loot": [{"item": I[f"fish{t}"], "chance": 1.0, "min": 1, "max": 2}]})
    for sid, name, model in (("anvil", "Amboss", "anvil"), ("forge", "Schmelzofen", "forge"), ("fire", "Kochfeuer", "campfire")):
        objects.append({"id": "obj_station_" + sid, "name": name, "model": model, "station": sid, "interactTime": 0})

    profs = []
    for pid, name, kind, desc, tool in PROFESSIONS:
        d = {"id": pid, "name": name, "kind": kind, "description": desc, "ranks": RANKS}
        if tool: d["tool"] = I.get(tool.replace("item_", ""), tool)
        profs.append(d)
    return profs, recipes, objects, I


def write_world(profs, objects, I):
    """Lehrer, Handwerkswarenhändler, Werkbänke und Abbaustellen in den Startregionen."""
    def load(p):
        with open(p, encoding="utf-8") as f: return json.load(f)
    def dump(p, d):
        with open(p, "w", encoding="utf-8") as f: f.write(json.dumps(d, ensure_ascii=False, indent=1) + "\n")
    npc_path = os.path.join(ROOT, "creatures", "npcs.json")
    npcs = [n for n in load(npc_path) if "_prof_" not in n["id"] and not n["id"].endswith("_craftvendor")]
    FIRST = {"Sonnenbuendnis": ["Albrecht", "Gunda", "Jorin", "Helke", "Tamsin", "Brann", "Mirela", "Odo", "Sefa", "Wendel", "Ilse", "Corvin"],
             "Schattenpakt": ["Grashka", "Murzul", "Thessa", "Vorn", "Zekka", "Drogg", "Ilvar", "Rasha", "Kuun", "Ssiva", "Morak", "Tulra"]}
    TITLES = {"mining": "Bergbaulehrer", "herbalism": "Kräuterkundelehrerin", "skinning": "Kürschnerlehrer", "blacksmithing": "Schmiedemeister",
              "leatherworking": "Lederermeisterin", "tailoring": "Schneidermeister", "alchemy": "Alchemistin", "engineering": "Ingenieur",
              "enchanting": "Verzauberin", "fishing": "Angellehrer", "cooking": "Koch", "first_aid": "Feldscherin"}
    towns = [("mt", "Sonnenbuendnis", os.path.join(ROOT, "zones", "morgentauwald.json"), (400, 400)),
             ("as", "Schattenpakt", os.path.join(ROOT, "zones", "aschensteppe.json"), (420, 420))]
    for pre, fac, zpath, (cx, cz) in towns:
        zones = load(zpath); z = zones[0]
        z["npcs"] = [n for n in z["npcs"] if "_prof_" not in n["npc"] and not n["npc"].endswith("_craftvendor")]
        z["objects"] = [o for o in z["objects"] if not o["object"].startswith(("obj_prof_", "obj_station_"))]
        # Handwerksviertel: Lehrer im Halbkreis südlich des Ortskerns
        for i, (pid, name, kind, desc, tool) in enumerate(PROFESSIONS):
            nid = f"npc_{pre}_prof_{pid}"
            npcs.append({"id": nid, "name": f"{FIRST[fac][i]}", "title": TITLES[pid], "faction": fac, "roles": "ProfessionTrainer", "trainerProfession": pid,
                         "minLevel": 30, "maxLevel": 30, "model": "npc", "gossip": f"{name}: {desc}", "barks": []})
            x = cx - 33 + i * 6; zz = cz - 28 - (abs(i - 5.5) * 0.8)
            z["npcs"].append({"npc": nid, "position": [round(x, 1), 0, round(zz, 1)], "facing": 0.0})
        vid = f"npc_{pre}_craftvendor"
        npcs.append({"id": vid, "name": "Handwerkswaren" if pre == "mt" else "Werkzeughändler", "title": "Werkzeug und Zubehör", "faction": fac, "roles": "Vendor",
                     "minLevel": 25, "maxLevel": 25, "model": "npc", "barks": [], "gossip": "Hacken, Messer, Garn und Phiolen – alles, was Handwerker brauchen.",
                     "vendorItems": [I[k] for k in ("tool_mining_pick", "tool_skinning_knife", "tool_blacksmith_hammer", "tool_fishing_pole", "empty_vial",
                                                     "coarse_thread", "fine_thread", "salt", "mild_spices", "flux")]})
        z["npcs"].append({"npc": vid, "position": [cx + 6, 0, cz - 34], "facing": 0.0})
        for sid, dx in (("anvil", -6), ("forge", -10), ("fire", 10)):
            z["objects"].append({"object": "obj_station_" + sid, "position": [cx + dx, 0, cz - 20], "count": 1, "radius": 0, "respawnSeconds": 1})
        # Abbaustellen der ersten beiden Stufen in den Startregionen
        # Stufe 0 in den leichten Gebieten nahe dem Startpunkt, Stufe 1 in den Gebieten der zweiten Hälfte (Gebiete sind
        # in der Zonendatei grob nach Schwierigkeit geordnet).
        areas = [a for a in z["areas"] if not a.get("isCity") and "rypta" not in a["name"]]
        easy, hard = areas[:4], areas[len(areas) // 2:]
        for t, count, pool in ((0, 8, easy), (1, 4, hard)):
            for j, oid in enumerate((f"obj_prof_vein{t}", f"obj_prof_herb{t}a", f"obj_prof_herb{t}b")):
                a = pool[j % len(pool)]
                z["objects"].append({"object": oid, "position": a["center"], "count": count, "radius": a["radius"] + 20, "respawnSeconds": 180 + t * 120})
        for t, aname in ((0, ("Silberquell", "Oase Zharim")), (1, ("Banditenlager am Rotbach", "Glutsenke"))):
            area = next((a for a in z["areas"] if a["name"] in aname), z["areas"][0])
            z["objects"].append({"object": f"obj_prof_fishpool{t}", "position": area["center"], "count": 3, "radius": area["radius"], "respawnSeconds": 240})
        # Feste Reihenfolge: gen_world_d.py schreibt in dieselben Dateien – so ist das Ergebnis unabhängig von der Aufrufreihenfolge.
        z["objects"].sort(key=lambda o: (o["object"], tuple(o["position"])))
        dump(zpath, zones)
    dump(npc_path, npcs)


def write(items, armor, weapon, misc):
    profs, recipes, objects, I = build(items, armor, weapon, misc)
    def dump(p, d):
        with open(p, "w", encoding="utf-8") as f: f.write(json.dumps(d, ensure_ascii=False, indent=1) + "\n")
    dump(os.path.join(ROOT, "professions.json"), profs)
    dump(os.path.join(ROOT, "recipes.json"), recipes)
    os.makedirs(os.path.join(ROOT, "objects"), exist_ok=True)
    dump(os.path.join(ROOT, "objects", "professions.json"), objects)
    write_world(profs, objects, I)
    print(len(profs), "Berufe,", len(recipes), "Rezepte,", len(objects), "Abbaustellen/Werkbänke")
