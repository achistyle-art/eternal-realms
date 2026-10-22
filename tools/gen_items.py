# Content-Werkzeug: erzeugt content/items.json (Rüstung/Waffenwerte aus Gegenstandsstufe und Qualität).
# Aufruf: python3 tools/gen_items.py
import json
import os as _os0, sys as _sys0
_sys0.path.insert(0, _os0.path.dirname(_os0.path.abspath(__file__)))
from itemlib import items, TF, SF, QV, price, armor, shield, WT_INV, weapon, misc, ring  # noqa: E402

# ---- Startausrüstung
weapon("item_worn_shortsword", "Abgenutztes Kurzschwert", "Sword1H", 2, 1, 1.9)
weapon("item_worn_mace", "Abgenutzter Streitkolben", "Mace1H", 2, 1, 2.0)
weapon("item_worn_axe", "Abgenutzte Axt", "Axe1H", 2, 1, 2.2)
weapon("item_worn_dagger", "Abgenutzter Dolch", "Dagger", 2, 1, 1.6)
weapon("item_worn_staff", "Abgenutzter Wanderstab", "Staff", 2, 1, 2.9)
weapon("item_crude_bow", "Grober Kurzbogen", "Bow", 2, 1, 2.3)
armor("item_recruit_tunic", "Rekrutenhemd", "Chest", "Cloth", 1, 1)
armor("item_recruit_pants", "Rekrutenhose", "Legs", "Cloth", 1, 1)
armor("item_novice_robe", "Novizenrobe", "Chest", "Cloth", 1, 1)
misc("item_hearthstone", "Ruhestein", desc="Bringt Euch zum Gasthaus zurück, an das Ihr gebunden seid.", use="item_hearthstone", unique=True)["bindOnPickup"] = True
# ---- Verbrauchsgüter
misc("item_tough_jerky", "Zähes Dörrfleisch", "Consumable", stack=20, sell=1, buy=5, desc="Stellt Leben wieder her, wenn man sitzt und isst.", use="item_eat")
misc("item_fresh_bread", "Frisches Brot", "Consumable", stack=20, sell=5, buy=25, desc="Stellt Leben wieder her, wenn man sitzt und isst.", use="item_eat", req=5)
misc("item_refreshing_water", "Erfrischendes Quellwasser", "Consumable", stack=20, sell=1, buy=5, desc="Stellt Mana wieder her, wenn man sitzt und trinkt.", use="item_drink")
misc("item_ice_cold_milk", "Eiskalte Milch", "Consumable", stack=20, sell=5, buy=25, desc="Stellt Mana wieder her.", use="item_drink", req=5)
misc("item_minor_healing_potion", "Schwacher Heiltrank", "Consumable", stack=5, sell=5, buy=20, desc="Stellt sofort 70–90 Leben wieder her.", use="item_minor_healing_potion")
misc("item_lesser_healing_potion", "Geringer Heiltrank", "Consumable", stack=5, sell=20, buy=80, desc="Stellt sofort 140–180 Leben wieder her.", use="item_lesser_healing_potion", req=10)
# ---- Taschen (Kapitel 14): vier Taschenplätze zusätzlich zum Rucksack
for id, n, slots, q, sell, buy, ilvl in [("item_bag_linen", "Leinenbeutel", 6, "Common", 25, 100, 5), ("item_bag_leather", "Lederrucksack", 8, "Common", 125, 500, 15),
                                        ("item_bag_wool", "Wollbeutel", 10, "Common", 500, 2000, 25), ("item_bag_mageweave", "Reisetasche aus Zauberstoff", 12, "Uncommon", 1500, 6000, 35),
                                        ("item_bag_runecloth", "Runenstofftasche", 14, "Uncommon", 4000, 16000, 50)]:
    d = misc(id, n, "Container", q, sell=sell, buy=buy, desc=f"Tasche mit {slots} Plätzen.", ilvl=ilvl)
    d["bagSlots"] = slots
# ---- Plunder (grau)
for id, n, s in [("item_wolf_fang", "Wolfsfang", 4), ("item_broken_tooth", "Abgebrochener Zahn", 3), ("item_spider_leg", "Haariges Spinnenbein", 6),
                 ("item_tattered_cloth", "Zerfetztes Tuch", 5), ("item_rusty_gear", "Rostiges Zahnrad", 7), ("item_cracked_scale", "Gesprungene Schuppe", 6),
                 ("item_hyena_hide", "Räudiges Hyänenfell", 5), ("item_beetle_shell", "Leerer Käferpanzer", 4), ("item_ash_crystal", "Trüber Aschekristall", 9),
                 ("item_moldy_root", "Modrige Wurzel", 12)]:
    misc(id, n, "Misc", "Poor", stack=10, sell=s, desc="Kann bei Händlern verkauft werden.")
# ---- Questgegenstände Morgentauwald
for id, n, d in [("item_q_wolf_pelt", "Waldwolfpelz", "Ein dichter, grauer Pelz."), ("item_q_iron_ore_chunk", "Eisenerzbrocken", "Roh, schwer und voller Möglichkeiten."),
                 ("item_q_gear_part", "Fennlings-Zahnradteil", "Ein feines Messingzahnrad mit Gravur."), ("item_q_sealed_letter", "Versiegelter Brief", "Das Siegel des Vogts von Morgentau."),
                 ("item_q_spider_gland", "Moosspinnendrüse", "Klebrig und übelriechend."), ("item_q_sparkstone", "Funkenstein", "Ein glühender Stein, der leise knistert."),
                 ("item_q_bandit_orders", "Befehle der Rotbach-Räuber", "Mit grobem Siegel: ein G."), ("item_q_grell_insignia", "Grells Hauptmannsabzeichen", "Beweis für Grells Niederlage."),
                 ("item_q_moonpetal", "Mondblüte", "Eine silbrig schimmernde Blume aus dem verborgenen Hain."), ("item_q_morrak_heart", "Herz des Wurzelfürsten", "Pulsiert noch immer schwach."),
                 ("item_q_boar_tusk", "Eberhauer", "Spitz und blutig."), ("item_q_trainee_token", "Prüfungsmarke", "Bestätigt eine bestandene Prüfung."),
                 # Aschensteppe
                 ("item_q_hyena_ear", "Hyänenohr", "Beweis für die Jagd."), ("item_q_beetle_gland", "Aschenkäferdrüse", "Brennt auf der Haut."),
                 ("item_q_ember_salt", "Glutsalz", "Warmes, rötliches Salz."), ("item_q_war_message", "Kriegsbotschaft", "In Paktrunen verfasst."),
                 ("item_q_cult_tome", "Kultfolio der Hohlen Flut", "Die Seiten bewegen sich, wenn man nicht hinsieht."), ("item_q_varga_head", "Vargas Kriegsmaske", "Die Maske des Kriegsfürsten."),
                 ("item_q_bone_charm", "Knochenamulett", "Geschnitzt von Skarr-Geistersehern."), ("item_q_water_skin", "Gefüllter Wasserschlauch", "Kostbares Wasser aus der Oase."),
                 ("item_q_lenz_letter", "Lenz' Brief an seine Mutter", "Zerknittert und tränenfleckig."), ("item_q_elron_letter", "Elrons Abschiedsbrief", "Mit zittriger Hand geschrieben.")]:
    misc(id, n, "Quest", "Common", stack=20, desc=d)
# ---- Questbelohnungen Morgentauwald (Stufe 3-10)
armor("item_wolfshain_leggings", "Wolfshain-Gamaschen", "Legs", "Leather", 6, 3, "Common", {"Agility": 1, "Stamina": 1})
armor("item_wolfshain_robe_pants", "Gewebte Waldhose", "Legs", "Cloth", 6, 3, "Common", {"Intellect": 1, "Spirit": 1})
armor("item_wolfshain_chain_legs", "Beinschienen der Hofwache", "Legs", "Mail", 6, 3, "Common", {"Strength": 1, "Stamina": 1})
armor("item_tanner_gloves", "Handschuhe der Gerberin", "Hands", "Leather", 7, 4, "Common", {"Agility": 2})
armor("item_tanner_wraps", "Gerberwickel", "Wrist", "Cloth", 7, 4, "Common", {"Intellect": 2})
armor("item_watchtower_boots", "Stiefel des Wachturms", "Feet", "Mail", 9, 6, "Uncommon", {"Strength": 2, "Stamina": 2})
armor("item_watchtower_sandals", "Sandalen des Wachturms", "Feet", "Cloth", 9, 6, "Uncommon", {"Intellect": 2, "Spirit": 2})
armor("item_watchtower_treads", "Leichte Pirschstiefel", "Feet", "Leather", 9, 6, "Uncommon", {"Agility": 2, "Stamina": 2})
armor("item_bram_belt", "Gürtel des Holzfällers", "Waist", "Leather", 9, 6, "Uncommon", {"Stamina": 2, "Strength": 1})
armor("item_bram_sash", "Schärpe der Dankbarkeit", "Waist", "Cloth", 9, 6, "Uncommon", {"Spirit": 3})
armor("item_miner_helm", "Grubenhelm", "Head", "Mail", 10, 7, "Uncommon", {"Stamina": 3, "Strength": 2})
armor("item_miner_hood", "Kapuze des Geomanten", "Head", "Cloth", 10, 7, "Uncommon", {"Intellect": 3, "Spirit": 2})
armor("item_miner_cap", "Lederkappe der Grube", "Head", "Leather", 10, 7, "Uncommon", {"Agility": 3, "Stamina": 2})
weapon("item_rotbach_cleaver", "Hackbeil des Rotbachs", "Axe1H", 11, 8, 2.4, "Uncommon", {"Strength": 2, "Stamina": 1})
weapon("item_rotbach_staff", "Stab der Hofmagie", "Staff", 11, 8, 3.0, "Uncommon", {"Intellect": 4, "Spirit": 2})
weapon("item_rotbach_bow", "Eibenbogen der Wacht", "Bow", 11, 8, 2.6, "Uncommon", {"Agility": 3})
weapon("item_rotbach_dagger", "Klinge des Spähers", "Dagger", 11, 8, 1.7, "Uncommon", {"Agility": 2, "Stamina": 1})
weapon("item_grell_greatsword", "Grells Zweihänder", "Sword2H", 13, 9, 3.3, "Uncommon", {"Strength": 5, "Stamina": 3})
shield("item_grell_shield", "Schild der Hofwache", 13, 9, "Uncommon", {"Stamina": 4, "Strength": 1})
ring("item_grell_signet", "Siegelring des Vogts", "Finger", 13, 9, "Uncommon", {"Intellect": 3, "Spirit": 3})
ring("item_mercy_amulet", "Amulett der Gnade", "Neck", 10, 7, "Uncommon", {"Spirit": 3, "Stamina": 2})
ring("item_justice_amulet", "Anhänger der Gerechtigkeit", "Neck", 10, 7, "Uncommon", {"Strength": 3, "Stamina": 2})
armor("item_hermit_cloak", "Umhang des Eremiten", "Back", "Cloth", 10, 7, "Uncommon", {"Agility": 1, "Intellect": 1, "Stamina": 2})
armor("item_trainee_bracers", "Armschienen der Prüfung", "Wrist", "Plate", 6, 4, "Common", {"Strength": 1, "Stamina": 1})
armor("item_trainee_mantle", "Mantel des Lehrlings", "Shoulders", "Cloth", 6, 4, "Common", {"Intellect": 1, "Spirit": 1})
armor("item_trainee_leather_shoulders", "Schulterstücke des Spähers", "Shoulders", "Leather", 6, 4, "Common", {"Agility": 1, "Stamina": 1})
armor("item_trainee_mail_shoulders", "Schulterplatten der Wacht", "Shoulders", "Mail", 6, 4, "Common", {"Strength": 1, "Stamina": 1})
# ---- Questbelohnungen Aschensteppe
armor("item_ash_hide_leggings", "Hyänenleder-Beinlinge", "Legs", "Leather", 6, 3, "Common", {"Agility": 1, "Stamina": 1})
armor("item_ash_wrap_pants", "Aschgewebte Hose", "Legs", "Cloth", 6, 3, "Common", {"Intellect": 1, "Spirit": 1})
armor("item_ash_chain_legs", "Kettenbeinlinge des Lagers", "Legs", "Mail", 6, 3, "Common", {"Strength": 1, "Stamina": 1})
armor("item_salt_gloves", "Salzsieder-Handschuhe", "Hands", "Leather", 7, 4, "Common", {"Agility": 2})
armor("item_salt_wraps", "Glutsalz-Wickel", "Wrist", "Cloth", 7, 4, "Common", {"Intellect": 2})
armor("item_scout_boots", "Stiefel des Kundschafters", "Feet", "Mail", 9, 6, "Uncommon", {"Strength": 2, "Stamina": 2})
armor("item_scout_sandals", "Wüstenwanderer-Sandalen", "Feet", "Cloth", 9, 6, "Uncommon", {"Intellect": 2, "Spirit": 2})
armor("item_scout_treads", "Staubläufer", "Feet", "Leather", 9, 6, "Uncommon", {"Agility": 2, "Stamina": 2})
armor("item_oasis_belt", "Gürtel der Oase", "Waist", "Leather", 9, 6, "Uncommon", {"Stamina": 2, "Agility": 1})
armor("item_oasis_sash", "Schärpe des Wasserträgers", "Waist", "Cloth", 9, 6, "Uncommon", {"Spirit": 3})
armor("item_cult_helm", "Helm des Kultjägers", "Head", "Mail", 10, 7, "Uncommon", {"Stamina": 3, "Strength": 2})
armor("item_cult_hood", "Kapuze des Abtrünnigen", "Head", "Cloth", 10, 7, "Uncommon", {"Intellect": 3, "Spirit": 2})
armor("item_cult_cap", "Maske des Spähers", "Head", "Leather", 10, 7, "Uncommon", {"Agility": 3, "Stamina": 2})
weapon("item_kargrak_axe", "Kargrak-Kriegsaxt", "Axe2H", 11, 8, 3.4, "Uncommon", {"Strength": 4, "Stamina": 2})
weapon("item_kargrak_totemstaff", "Totemstab der Ahnen", "Staff", 11, 8, 3.0, "Uncommon", {"Intellect": 4, "Spirit": 2})
weapon("item_kargrak_bow", "Knochenbogen", "Bow", 11, 8, 2.6, "Uncommon", {"Agility": 3})
weapon("item_kargrak_kris", "Skarr-Kris", "Dagger", 11, 8, 1.7, "Uncommon", {"Agility": 2, "Stamina": 1})
weapon("item_varga_mace", "Vargas Kriegshammer", "Mace1H", 13, 9, 2.5, "Uncommon", {"Strength": 3, "Stamina": 2})
shield("item_varga_shield", "Schild des Kriegsfürsten", 13, 9, "Uncommon", {"Stamina": 4, "Strength": 1})
ring("item_varga_ring", "Ring des Paktes", "Finger", 13, 9, "Uncommon", {"Intellect": 3, "Spirit": 3})
ring("item_honor_amulet", "Amulett der Ehre", "Neck", 10, 7, "Uncommon", {"Strength": 3, "Stamina": 2})
ring("item_cunning_amulet", "Anhänger der List", "Neck", 10, 7, "Uncommon", {"Agility": 3, "Stamina": 2})
armor("item_ancestor_cloak", "Umhang der Ahnen", "Back", "Cloth", 10, 7, "Uncommon", {"Agility": 1, "Intellect": 1, "Stamina": 2})
# ---- Händlerware (weiß)
for wt, n, sp in [("Sword1H", "Langschwert", 2.3), ("Mace1H", "Kriegskeule", 2.4), ("Axe1H", "Handaxt", 2.5), ("Dagger", "Stilett", 1.7),
                  ("Staff", "Eichenstab", 3.0), ("Sword2H", "Bidenhänder", 3.4), ("Bow", "Jagdbogen", 2.5), ("Wand", "Lehrlingszauberstab", 1.6)]:
    weapon("item_vendor_" + wt.lower(), n, wt, 7, 4, sp)
for slot in ["Chest", "Legs", "Hands", "Feet", "Waist", "Wrist", "Shoulders", "Head"]:
    nm = {"Chest": "Wams", "Legs": "Hose", "Hands": "Handschuhe", "Feet": "Stiefel", "Waist": "Gürtel", "Wrist": "Armschienen", "Shoulders": "Schultern", "Head": "Kappe"}[slot]
    for at, pre in [("Cloth", "Leinen"), ("Leather", "Leder"), ("Mail", "Ketten")]:
        armor(f"item_vendor_{at.lower()}_{slot.lower()}", f"{pre}-{nm}" if slot != "Chest" else f"{pre}wams", slot, at, 6, 3)
shield("item_vendor_shield", "Holzschild", 6, 3)
# ---- Zufällige grüne Beute aus Kreaturen
for suffix, stats in [("des Bären", {"Strength": 2, "Stamina": 2}), ("des Affen", {"Agility": 2, "Stamina": 2}), ("der Eule", {"Intellect": 2, "Spirit": 2}), ("des Adlers", {"Intellect": 2, "Stamina": 2})]:
    key = suffix.split()[-1].lower().replace("ä", "ae")
    armor(f"item_green_leather_boots_{key}", "Pirschstiefel " + suffix, "Feet", "Leather", 8, 4, "Uncommon", stats)
    armor(f"item_green_cloth_gloves_{key}", "Seidenhandschuhe " + suffix, "Hands", "Cloth", 8, 4, "Uncommon", stats)
    armor(f"item_green_mail_belt_{key}", "Kettengurt " + suffix, "Waist", "Mail", 8, 4, "Uncommon", stats)
weapon("item_green_dagger_wolf", "Wolfszahn-Messer", "Dagger", 8, 4, 1.6, "Uncommon", {"Agility": 2})
weapon("item_green_sword_dawn", "Morgenrotklinge", "Sword1H", 9, 5, 2.4, "Uncommon", {"Strength": 2, "Stamina": 1})
weapon("item_green_wand_ember", "Glutfunken-Zauberstab", "Wand", 9, 5, 1.6, "Uncommon", {"Intellect": 2})
# Seltene Gegner
weapon("item_rare_graumaehne_fang", "Fang der Alten Graumähne", "Dagger", 12, 7, 1.5, "Rare", {"Agility": 4, "Stamina": 2})
armor("item_rare_dunestalker_cloak", "Umhang des Dünenpirschers", "Back", "Cloth", 12, 7, "Rare", {"Agility": 3, "Stamina": 3})
# ---- Dungeon Wurzelkrypta (Stufe 13-17, blau)
armor("item_wk_moldspore_hood", "Kapuze des Moderkeims", "Head", "Cloth", 17, 13, "Rare", {"Intellect": 6, "Spirit": 4, "Stamina": 3})
armor("item_wk_rootbark_chest", "Brustpanzer aus Wurzelborke", "Chest", "Mail", 17, 13, "Rare", {"Strength": 6, "Stamina": 7})
armor("item_wk_rootbark_tunic", "Wams aus Wurzelborke", "Chest", "Leather", 17, 13, "Rare", {"Agility": 6, "Stamina": 6})
armor("item_wk_elvaine_gloves", "Handschuhe der Verdorbenen", "Hands", "Cloth", 17, 14, "Rare", {"Intellect": 5, "Stamina": 3}, extra=[{"type": "SpellPowerFlat", "value": 6}])
weapon("item_wk_elvaine_staff", "Elvaines verdorbener Stab", "Staff", 18, 14, 3.0, "Rare", {"Intellect": 8, "Stamina": 5}, extra=[{"type": "SpellPowerFlat", "value": 10}])
weapon("item_wk_morrak_maul", "Wurzelfürstenhammer", "Mace2H", 19, 15, 3.5, "Rare", {"Strength": 10, "Stamina": 6})
weapon("item_wk_morrak_blade", "Klinge der Kernwurzel", "Sword1H", 19, 15, 2.6, "Rare", {"Agility": 5, "Stamina": 4})
shield("item_wk_morrak_shield", "Borkenschild des Hüters", 19, 15, "Rare", {"Stamina": 8, "Strength": 3})
ring("item_wk_morrak_ring", "Ring des ewigen Waldes", "Finger", 19, 15, "Rare", {"Intellect": 5, "Spirit": 5, "Stamina": 3})
ring("item_wk_heart_trinket", "Pochendes Wurzelherz", "Trinket", 19, 15, "Rare", {"Stamina": 6}, extra=[{"type": "MaxHealthPercent", "value": 2}])
armor("item_wk_crypt_boots", "Gruftwächterstiefel", "Feet", "Plate", 16, 13, "Uncommon", {"Strength": 4, "Stamina": 4})
armor("item_wk_crypt_leggings", "Beinkleid des Gruftschlurfers", "Legs", "Cloth", 16, 13, "Uncommon", {"Intellect": 4, "Spirit": 3})
# ---- Reittiere: Zügel lehren ein Reittier für die Sammlung (content/mounts.json)
import json as _json, os as _os
for m in _json.load(open(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "content", "mounts.json"), encoding="utf-8")):
    d = misc(m["item"], f"Zügel: {m['name']}", "Misc", m["quality"], desc=f"Lehrt das Reittier „{m['name']}“ (+{m['speed']} % Tempo). {m['description']}",
             req=m["requiredLevel"], buy=m.get("price", 0), ilvl=m["requiredLevel"])
    d["teachesMount"] = m["id"]
    if m.get("bindOnPickup"): d["bindOnPickup"] = True
    if m.get("requiredReputation"):  # Rufbelohnung: nur mit genug Ansehen kauf- und lernbar
        d["requiredReputation"], d["requiredReputationValue"] = m["requiredReputation"], m["requiredReputationValue"]

import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_professions
gen_professions.write(items, armor, weapon, misc)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "content", "items.json")
with open(out, "w", encoding="utf-8") as f:
    json.dump(items, f, ensure_ascii=False, indent=1)
print(len(items), "Gegenstände")
