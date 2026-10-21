# Gemeinsame Bausteine für Gegenstände (Rüstung, Waffen, Verbrauchsgüter): Werte und Preise aus Gegenstandsstufe und Qualität.
# Genutzt von gen_items.py (content/items.json) und gen_world_i.py (Inhalte Stufe 12–20) – so folgen alle Gegenstände denselben Formeln.
items = []
TF = {"Cloth": 1, "Leather": 2, "Mail": 4, "Plate": 7}
SF = {"Chest": 1, "Legs": 0.9, "Head": 0.8, "Shoulders": 0.75, "Hands": 0.6, "Feet": 0.65, "Waist": 0.45, "Wrist": 0.4, "Back": 0.5, "Shield": 5}
QV = {"Poor": 0.6, "Common": 1.0, "Uncommon": 1.25, "Rare": 1.45, "Epic": 1.7}
def price(ilvl, q): return int((ilvl * ilvl * 1.5 + 10) * QV[q] * QV[q])
def armor(id, name, slot, atype, ilvl, req, q="Common", stats=None, desc="", classes=None, extra=None):
    a = int((ilvl * 2 + 10) * (TF[atype] if slot != "Back" else 1) * SF[slot] * (0.8 + 0.2 * QV[q]) * 0.5)
    d = {"id": id, "name": name, "description": desc, "quality": q, "category": "Armor", "inventoryType": slot, "armorType": atype if slot not in ("Back",) else "Cloth",
         "itemLevel": ilvl, "requiredLevel": req, "armor": a, "modifiers": [{"type": "StatFlat", "stat": s, "value": v} for s, v in (stats or {}).items()],
         "maxDurability": int(20 + ilvl * 2), "sellPrice": price(ilvl, q) // 4, "buyPrice": price(ilvl, q)}
    if classes: d["classes"] = classes
    if extra: d["modifiers"] += extra
    items.append(d); return d
def shield(id, name, ilvl, req, q="Common", stats=None, desc=""):
    d = armor(id, name, "Shield", "Mail", ilvl, req, q, stats, desc)
    d["armorType"] = "None"; d["category"] = "Armor"; d["weaponType"] = "Shield"; d["inventoryType"] = "Shield"
    d["armor"] = int((ilvl * 6 + 30) * (0.8 + 0.2 * QV[q]))
    d["modifiers"].append({"type": "BlockChance", "value": 5})
    return d
WT_INV = {"Sword1H": "OneHand", "Axe1H": "OneHand", "Mace1H": "OneHand", "Dagger": "OneHand", "FistWeapon": "OneHand",
          "Sword2H": "TwoHand", "Axe2H": "TwoHand", "Mace2H": "TwoHand", "Staff": "TwoHand", "Polearm": "TwoHand",
          "Bow": "Ranged", "Gun": "Ranged", "Crossbow": "Ranged", "Wand": "Ranged", "Thrown": "Ranged", "OffhandFrill": "OffHand"}
def weapon(id, name, wt, ilvl, req, speed, q="Common", stats=None, desc="", extra=None):
    two = WT_INV[wt] == "TwoHand"
    dps = (1.2 + ilvl * 0.55) * QV[q] * (1.3 if two else 1.0)
    if wt == "Wand": dps *= 1.4
    avg = dps * speed
    d = {"id": id, "name": name, "description": desc, "quality": q, "category": "Weapon", "inventoryType": WT_INV[wt], "weaponType": wt,
         "itemLevel": ilvl, "requiredLevel": req, "minDamage": max(1, int(avg * 0.75)), "maxDamage": max(2, int(avg * 1.25)), "speed": speed,
         "modifiers": [{"type": "StatFlat", "stat": s, "value": v} for s, v in (stats or {}).items()],
         "maxDurability": int(25 + ilvl * 2.5), "sellPrice": price(ilvl, q) // 4, "buyPrice": price(ilvl, q)}
    if wt == "Wand": d["damageSchool"] = "Arcane"
    if extra: d["modifiers"] += extra
    items.append(d); return d
def misc(id, name, cat="Misc", q="Common", stack=1, sell=0, buy=0, desc="", use=None, req=0, starts=None, unique=False, ilvl=1):
    d = {"id": id, "name": name, "description": desc, "quality": q, "category": cat, "itemLevel": ilvl, "requiredLevel": req, "maxStack": stack,
         "sellPrice": sell, "buyPrice": buy, "modifiers": []}
    if use: d["useAbility"] = use
    if starts: d["startsQuest"] = starts
    if unique: d["unique"] = True
    items.append(d); return d
def ring(id, name, slot, ilvl, req, q, stats, extra=None, desc=""):
    d = {"id": id, "name": name, "description": desc, "quality": q, "category": "Armor", "inventoryType": slot, "itemLevel": ilvl, "requiredLevel": req,
         "modifiers": [{"type": "StatFlat", "stat": s, "value": v} for s, v in stats.items()] + (extra or []),
         "sellPrice": price(ilvl, q) // 4, "buyPrice": price(ilvl, q)}
    items.append(d); return d
