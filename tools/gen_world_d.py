#!/usr/bin/env python3
"""
Block D (Kapitel 35, 36, 37, 47): Hauptstädte, Weltereignisse und Spawnregeln.

Erzeugt deterministisch:
  content/zones/capitals.json         vier spielbare Hauptstädte (je Fraktion zwei)
  content/creatures/capitals.json     Stadtbewohner: Lehrer, Händler, Bank, Auktionshaus, Wachen …
  content/creatures/events.json       Gegner der Weltereignisse und Nachtwesen
  content/objects/portals.json        Stadtportale (nur für die eigene Fraktion)
  content/travel/nodes/capitals.json, content/travel/paths/capitals.json   Fernflüge
  content/events.json                 Weltereignisse
und passt an:
  content/zones/weltkarte.json        Platzhalter Sonnenfeste/Kharzul entfernt (jetzt echte Zonen)
  content/zones/morgentauwald.json, aschensteppe.json   Portale, Spawnregeln, Nachtwesen
  content/reputations.json            Ruf der zweiten Hauptstädte

Aufruf: python3 tools/gen_world_d.py   (mehrfach ausführbar, Ergebnis immer gleich)
"""
import json, math, os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "content")


def load(rel):
    with open(os.path.join(ROOT, rel), encoding="utf-8") as f:
        return json.load(f)


def save(rel, data):
    path = os.path.join(ROOT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        f.write(json.dumps(data, ensure_ascii=False, indent=1) + "\n")


npcs_base = {x["id"]: x for x in load("creatures/npcs.json")}
professions = [p["id"] for p in load("professions.json")]
classes = {c["id"]: c["name"] for c in load("classes.json")}
races = load("races.json")
faction_classes = {}
for r in races:
    faction_classes.setdefault(r["faction"], set()).update(r["classes"])

SB, SP = "Sonnenbuendnis", "Schattenpakt"
PROF_NAMES = {"mining": "Bergbau", "herbalism": "Kräuterkunde", "skinning": "Kürschnerei", "blacksmithing": "Schmiedekunst",
              "leatherworking": "Lederverarbeitung", "tailoring": "Schneiderei", "alchemy": "Alchemie", "engineering": "Ingenieurskunst",
              "enchanting": "Verzauberkunst", "fishing": "Angeln", "cooking": "Kochkunst", "first_aid": "Erste Hilfe"}

CAPITALS = [
    dict(id="zone_sonnenfeste", key="sf", name="Sonnenfeste", continent="Aurelion", faction=SB,
         desc="Weiße Mauern, goldene Dächer: die Hauptstadt der Aldorer und Fennlinge mit Königspalast, Auktionshaus, Bank und allen Lehrern.",
         weather=["Clear", "Clear", "Rain"], rect=[430, 820, 120, 90], home="zone_morgentauwald", node="tn_mt_dorf",
         names=["Aldwin", "Bertha", "Cedric", "Dorothea", "Edmund", "Frieda", "Gunther", "Hilde", "Ingram", "Jolanda", "Konrad", "Liesel",
                "Magnus", "Notburga", "Oswald", "Petra", "Quirin", "Rosalind", "Siegbert", "Theda", "Ulrich", "Veronika", "Walram", "Yvette",
                "Arnulf", "Brunhild", "Dietmar", "Elfriede", "Folkmar", "Gisela", "Hartmut", "Irmgard", "Kunibert", "Luitgard", "Meinrad", "Odila",
                "Reinhold", "Sieglinde", "Tankred", "Walburga"],
         title="Königsgarde der Sonnenfeste", music="music_city_sonnenfeste"),
    dict(id="zone_silbermond", key="sm", name="Silbermond", continent="Aurelion", faction=SB,
         desc="Unter uralten Mondbäumen und Felsbögen: die Stadt der Elarii und Felsgeborenen, berühmt für Magie, Schmiedekunst und Druidenhaine.",
         weather=["Clear", "Fog", "Rain"], rect=[570, 850, 110, 80], home="zone_morgentauwald", node="tn_mt_dorf",
         names=["Aelthas", "Brynja", "Caelith", "Dorin", "Elowen", "Faelar", "Grimbald", "Hyldra", "Isilwen", "Jorund", "Kaelis", "Lunara",
                "Mithrandel", "Nimue", "Orrin", "Pyria", "Quelin", "Rhovan", "Sylvaine", "Thrain", "Ulva", "Vaelor", "Wyndra", "Ysolde",
                "Arwen", "Balin", "Celebrin", "Durlin", "Eldarion", "Fjola", "Galadhor", "Hrafna", "Ithil", "Keldor", "Lothiel", "Morwyn",
                "Nalfein", "Ondra", "Rhiannon", "Starkad"],
         title="Mondhüter von Silbermond", music="music_city_silbermond"),
    dict(id="zone_kharzul", key="kz", name="Kharzul", continent="Kharvath", faction=SP,
         desc="Die Festungsstadt der Grakar und Skarr in einer erloschenen Caldera: Kriegsrat, Arenen, Basare und alle Lehrer.",
         weather=["Clear", "Clear", "Storm"], rect=[460, 800, 130, 100], home="zone_aschensteppe", node="tn_as_lager",
         names=["Azgul", "Brakka", "Drogg", "Gharza", "Hruk", "Izzik", "Jarnok", "Krasha", "Lugdar", "Mogra", "Nazgrak", "Orzha",
                "Rakhul", "Sszeth", "Tharga", "Ugrok", "Vrasha", "Xelok", "Yazgul", "Zharra", "Karvesh", "Mazrok", "Thessa", "Gorbag",
                "Agra", "Bolgar", "Durza", "Ghorak", "Hessa", "Ishka", "Krogg", "Lurtz", "Mukka", "Nargul", "Oshka", "Razgar",
                "Shagra", "Torka", "Uzgash", "Vorka"],
         title="Kriegswache von Kharzul", music="music_city_kharzul"),
    dict(id="zone_nachtwacht", key="nw", name="Nachtwacht", continent="Kharvath", faction=SP,
         desc="Eine Stadt aus schwarzem Stein zwischen Grabhügeln und Steinkreisen: Heimat der Nachtgeborenen und des Hornvolks.",
         weather=["Fog", "Clear", "Rain"], rect=[620, 830, 120, 80], home="zone_aschensteppe", node="tn_as_lager",
         names=["Morwen", "Asgaroth", "Belisande", "Corvin", "Drusilla", "Erebos", "Fenrik", "Grimhild", "Hadrian", "Isolde", "Kaspar", "Lenore",
                "Malvina", "Nocturn", "Ophira", "Raven", "Selvara", "Tamsin", "Umbra", "Vesper", "Wolfram", "Xanthe", "Ymir", "Zelda",
                "Aldaric", "Brannoc", "Cyrene", "Dagmar", "Elvira", "Gideon", "Hekate", "Ignatius", "Lilith", "Mordecai", "Nerys", "Oberon",
                "Persephone", "Rowena", "Severin", "Thorne"],
         title="Totenwache von Nachtwacht", music="music_city_nachtwacht"),
]

SIZE = 512
C = 256  # Mittelpunkt der Stadt


def pos(x, z):
    return [round(x, 1), 0, round(z, 1)]


def ring(cx, cz, r, n):
    return [(cx + r * math.cos(i * 2 * math.pi / n), cz + r * math.sin(i * 2 * math.pi / n)) for i in range(n)]


def capital(cap, creatures):
    k = cap["key"]
    names = list(cap["names"])
    placed = []

    def person(suffix, title, roles, level=30, **extra):
        nid = f"npc_{k}_{suffix}"
        d = {"id": nid, "name": names.pop(0), "title": title, "faction": cap["faction"], "roles": roles,
             "minLevel": level, "maxLevel": level, "model": "npc"}
        d.update(extra)
        creatures.append(d)
        return nid

    def place(nid, x, z, facing=0.0, patrol=None):
        e = {"npc": nid, "position": pos(x, z), "facing": round(facing, 2)}
        if patrol:
            e["patrol"] = patrol
        placed.append(e)

    m = cap["music"]
    areas = [
        {"id": f"area_{k}_markt", "name": f"Marktplatz von {cap['name']}", "center": pos(C, C), "radius": 45, "isCity": True, "discoveryXp": 40, "music": m},
        {"id": f"area_{k}_handel", "name": "Handelsviertel", "center": pos(C + 90, C), "radius": 40, "isCity": True, "discoveryXp": 20, "music": m},
        {"id": f"area_{k}_lehrer", "name": "Hof der Lehrmeister", "center": pos(C - 90, C), "radius": 40, "isCity": True, "discoveryXp": 20, "music": m},
        {"id": f"area_{k}_handwerk", "name": "Handwerkergasse", "center": pos(C, C - 90), "radius": 40, "isCity": True, "discoveryXp": 20, "music": m},
        {"id": f"area_{k}_gasthaus", "name": f"Gasthaus von {cap['name']}", "center": pos(C, C + 90), "radius": 30, "isCity": True, "isInn": True, "discoveryXp": 20},
        {"id": f"area_{k}_tor", "name": "Stadttor", "center": pos(C, C + 175), "radius": 35, "isCity": True, "discoveryXp": 10},
        {"id": f"area_{k}_palast", "name": "Palast" if cap["faction"] == SB else "Kriegsfeste", "center": pos(C, C - 170), "radius": 35, "isCity": True, "discoveryXp": 30, "music": m},
    ]
    # Herrscher
    place(person("herrscher", f"Herrschaft von {cap['name']}", "QuestGiver", 63, elite=True, healthMultiplier=20,
                 gossip=f"Willkommen in {cap['name']}. Solange diese Mauern stehen, steht auch unser Volk."), C, C - 175, 1.57)
    # Handelsviertel
    place(person("bank1", f"Bank von {cap['name']}", "Banker", gossip="Euer Hab und Gut ist hier sicher."), C + 100, C - 12, 3.14)
    place(person("bank2", f"Bank von {cap['name']}", "Banker", gossip="Einzahlen, abheben – ganz wie Ihr wünscht."), C + 100, C + 12, 3.14)
    place(person("auktion1", "Auktionshaus", "Auctioneer", gossip="Das größte Auktionshaus der Fraktion – kaufen, verkaufen, bieten."), C + 80, C - 25, 1.57)
    place(person("auktion2", "Auktionshaus", "Auctioneer", gossip="Heute schon geboten?"), C + 80, C + 25, 4.71)
    place(person("ruestung", "Waffen und Rüstungen", "Vendor, Repair", 25, vendorItems=npcs_base["npc_mt_smith"]["vendorItems"],
                 gossip="Gute Klingen, feste Rüstungen – und Reparaturen."), C + 60, C - 40, 2.0)
    place(person("wirt", f"Gasthaus von {cap['name']}", "Innkeeper, Vendor", 25, vendorItems=npcs_base["npc_mt_innkeeper"]["vendorItems"],
                 gossip="Ein Bett für die Nacht? Bei uns wacht Ihr ausgeruht auf.", barks=["Frisches Brot!", "Noch ein Krug?"]), C + 6, C + 92, 4.71)
    place(person("werkzeug", "Werkzeug und Zubehör", "Vendor", 25, vendorItems=npcs_base["npc_mt_craftvendor"]["vendorItems"],
                 gossip="Alles, was Handwerker brauchen."), C + 14, C - 70, 1.57)
    stable = (npcs_base["npc_mt_stablemaster"] if cap["faction"] == SB else npcs_base["npc_as_stablemaster"])["vendorItems"]
    place(person("stall", "Stallmeisterei", "RidingTrainer, Vendor", 40, vendorItems=stable, gossip="Ab Stufe 40 lernt Ihr bei mir das Reiten."), C - 30, C + 160, 4.71)
    place(person("flug", "Flugmeisterei", "FlightMaster", 30, gossip="Von hier aus erreicht Ihr die Siedlungen Eurer Fraktion."), C + 30, C + 165, 4.71)
    # Lehrer aller Klassen der Fraktion
    cls = sorted(faction_classes[cap["faction"]])
    for cid, (x, z) in zip(cls, ring(C - 90, C, 22, len(cls))):
        place(person("lehrer_" + cid.lower(), f"{classes[cid]}-Lehrmeister", "ClassTrainer", 50, trainerClass=cid,
                     gossip=f"Ich lehre die Künste der {classes[cid]} bis zur höchsten Meisterschaft."), x, z, round(math.atan2(C - z, C - 90 - x), 2))
    # Berufslehrer
    for i, prof in enumerate(professions):
        place(person("beruf_" + prof, f"Meister: {PROF_NAMES[prof]}", "ProfessionTrainer", 50, trainerProfession=prof,
                     gossip=f"{PROF_NAMES[prof]} – bei mir lernt Ihr bis zum Meistergrad."), C - 66 + (i % 6) * 26, C - 100 + (i // 6) * 22, 1.57)
    # Wachen: Posten an Tor, Palast und Vierteln, Streifen um den Markt
    guard = f"npc_{k}_wache"
    creatures.append({"id": guard, "name": f"Wache von {cap['name']}", "title": cap["title"], "faction": cap["faction"], "roles": "Guard",
                      "minLevel": 60, "maxLevel": 60, "model": "npc", "elite": True, "healthMultiplier": 6, "damageMultiplier": 3, "aggroRadius": 22,
                      "barks": ["Für die Stadt!", "Alles ruhig.", "Haltet Ordnung, Bürger."]})
    for x, z in [(C - 12, C + 190), (C + 12, C + 190), (C - 15, C - 150), (C + 15, C - 150), (C + 125, C), (C - 125, C)]:
        place(guard, x, z, 4.71)
    loop = [pos(C, C - 60), pos(C + 60, C), pos(C, C + 60), pos(C - 60, C)]
    for i in range(4):
        place(guard, loop[i][0], loop[i][2], 0, loop[i:] + loop[:i])
    # Stadtleben: Bürger mit Tagesablauf
    for i in range(6):
        cid = person(f"buerger{i + 1}", "Bürger" if i % 2 == 0 else "Bürgerin", "None", 10,
                     gossip="Habt Ihr schon die Neuigkeiten gehört?",
                     barks=["Schöner Tag heute.", "Die Preise steigen schon wieder.", "Habt Ihr den neuen Händler gesehen?"],
                     schedule=[{"hour": 7, "position": pos(C - 30 + i * 12, C + 20), "activity": "Markt"},
                               {"hour": 19, "position": pos(C - 10 + i * 4, C + 88), "activity": "Gasthaus"},
                               {"hour": 23, "position": pos(C - 100 + i * 40, C + 130), "activity": "Zuhause"}])
        place(cid, C - 30 + i * 12, C + 20, 0)
    healer = f"npc_{k}_geisterheiler"
    creatures.append({"id": healer, "name": "Geisterheiler", "title": "", "faction": "Friendly", "roles": "SpiritHealer", "minLevel": 60, "maxLevel": 60,
                      "model": "npc", "gossip": "Kehrt zurück, Wanderer – die Stadt braucht Euch."})
    place(healer, C, C + 230, 4.71)
    objects = [{"object": "obj_mailbox", "position": pos(C + 70, C + 8), "count": 1, "radius": 0, "respawnSeconds": 0},
               {"object": "obj_mailbox", "position": pos(C + 10, C + 75), "count": 1, "radius": 0, "respawnSeconds": 0},
               {"object": f"obj_portal_{k}_heim", "position": pos(C - 20, C + 200), "count": 1, "radius": 0, "respawnSeconds": 0}]
    return {
        "id": cap["id"], "name": cap["name"], "continent": cap["continent"], "minLevel": 1, "maxLevel": 60, "faction": cap["faction"],
        "isCapital": True, "biome": "Hauptstadt", "description": cap["desc"], "music": m, "ambience": "amb_city",
        "weather": cap["weather"], "size": SIZE, "playable": True, "ambientBots": 8, "areas": areas, "graveyards": [pos(C, C + 225)],
        "spawns": [], "npcs": placed, "objects": objects, "mapRect": cap["rect"],
    }


def main():
    creatures = []
    zones = [capital(c, creatures) for c in CAPITALS]
    save("zones/capitals.json", zones)
    save("creatures/capitals.json", creatures)

    world = [z for z in load("zones/weltkarte.json") if z["id"] not in {c["id"] for c in CAPITALS}]
    save("zones/weltkarte.json", world)

    reps = [r for r in load("reputations.json") if r["id"] not in ("rep_silbermond", "rep_nachtwacht")]
    reps.append({"id": "rep_silbermond", "name": "Silbermond", "faction": SB, "description": "Der Mondrat der Elarii und die Steinfürsten der Felsgeborenen."})
    reps.append({"id": "rep_nachtwacht", "name": "Nachtwacht", "faction": SP, "description": "Der Totenrat der Nachtgeborenen und die Ältesten des Hornvolks."})
    save("reputations.json", reps)

    home_names = {"zone_morgentauwald": "Morgentauwald", "zone_aschensteppe": "Aschensteppe"}
    home_spot = {"zone_morgentauwald": (395, 440), "zone_aschensteppe": (415, 460)}
    portals = []
    for cap in CAPITALS:
        hx, hz = home_spot[cap["home"]]
        portals.append({"id": f"obj_portal_{cap['key']}_heim", "name": f"Portal: {home_names[cap['home']]}", "model": "portal_city",
                        "teleportZone": cap["home"], "teleportPosition": pos(hx, hz), "requiredFaction": cap["faction"], "interactTime": 0.5})
        portals.append({"id": f"obj_portal_nach_{cap['key']}", "name": f"Portal: {cap['name']}", "model": "portal_city",
                        "teleportZone": cap["id"], "teleportPosition": pos(C, C + 195), "requiredFaction": cap["faction"], "interactTime": 0.5})
    save("objects/portals.json", portals)

    nodes, paths = [], []
    for cap in CAPITALS:
        nid = f"tn_{cap['key']}"
        nodes.append({"id": nid, "name": cap["name"], "zone": cap["id"], "position": pos(C + 30, C + 165), "faction": cap["faction"]})
        paths.append({"from": cap["node"], "to": nid, "cost": 200, "durationSeconds": 40})
        paths.append({"from": nid, "to": cap["node"], "cost": 200, "durationSeconds": 40})
    for a, b in (("sf", "sm"), ("kz", "nw")):
        paths.append({"from": f"tn_{a}", "to": f"tn_{b}", "cost": 150, "durationSeconds": 35})
        paths.append({"from": f"tn_{b}", "to": f"tn_{a}", "cost": 150, "durationSeconds": 35})
    save("travel/nodes/capitals.json", nodes)
    save("travel/paths/capitals.json", paths)

    save("creatures/events.json", [
        {"id": "mob_irrlicht", "name": "Irrlicht", "minLevel": 5, "maxLevel": 7, "family": "Spirit", "faction": "Hostile", "model": "wisp",
         "meleeSchool": "Arcane", "aggroRadius": 14, "moneyMin": 10, "moneyMax": 30, "loot": []},
        {"id": "mob_aschegeist", "name": "Aschegeist", "minLevel": 5, "maxLevel": 7, "family": "Spirit", "faction": "Hostile", "model": "wisp",
         "meleeSchool": "Fire", "aggroRadius": 14, "moneyMin": 10, "moneyMax": 30, "loot": []},
        {"id": "mob_ev_hungerwolf", "name": "Ausgehungerter Wolf", "minLevel": 5, "maxLevel": 7, "family": "Wolf", "faction": "Hostile", "model": "wolf",
         "aggroRadius": 18, "loot": [{"item": "item_wolf_fang", "chance": 0.5, "min": 1, "max": 1}]},
        {"id": "mob_ev_rudelfuehrerin", "name": "Rudelführerin Silberfang", "minLevel": 9, "maxLevel": 9, "family": "Wolf", "faction": "Hostile",
         "model": "wolf", "elite": True, "healthMultiplier": 3, "damageMultiplier": 1.3, "scale": 1.4, "moneyMin": 300, "moneyMax": 600,
         "loot": [{"item": "item_wolf_fang", "chance": 1.0, "min": 3, "max": 5}]},
        {"id": "mob_ev_raeuber", "name": "Rotbach-Plünderer", "minLevel": 6, "maxLevel": 8, "family": "Humanoid", "faction": "Hostile", "model": "bandit",
         "aggroRadius": 18, "loot": []},
        {"id": "mob_ev_kultist", "name": "Ascheprediger der Hohlen Flut", "minLevel": 6, "maxLevel": 8, "family": "Humanoid", "faction": "Hostile",
         "model": "cultist", "meleeSchool": "Shadow", "aggroRadius": 18, "loot": []},
        {"id": "mob_ev_flutbote", "name": "Bote der Hohlen Flut", "minLevel": 10, "maxLevel": 10, "family": "Demon", "faction": "Hostile", "model": "demon",
         "elite": True, "healthMultiplier": 3.5, "damageMultiplier": 1.3, "scale": 1.6, "moneyMin": 400, "moneyMax": 800, "meleeSchool": "Shadow", "loot": []},
        {"id": "npc_ev_karren", "name": "Händlerkarren der Familie Lenz", "title": "Zu beschützen", "minLevel": 8, "maxLevel": 8, "faction": SB,
         "roles": "None", "model": "cart", "healthMultiplier": 8},
        {"id": "npc_ev_brunnen", "name": "Wasserstein der Oase", "title": "Zu beschützen", "minLevel": 8, "maxLevel": 8, "faction": SP,
         "roles": "None", "model": "wellstone", "healthMultiplier": 8},
    ])

    save("events.json", [
        {"id": "ev_mt_wolfsnacht", "name": "Die Nacht der hungrigen Wölfe", "zone": "zone_morgentauwald", "position": pos(560, 320), "radius": 55,
         "minLevel": 4, "maxLevel": 9, "intervalMinutes": [25, 50], "timeOfDay": "night", "warmup": 45, "duration": 600,
         "announce": "Im Wolfshain heulen hungrige Wölfe – gleich fallen sie über die Höfe her!",
         "successText": "Das Rudel ist zerschlagen. Die Bauern von Morgentau danken Euch!",
         "failureText": "Die Wölfe ziehen sich mit ihrer Beute zurück …",
         "phases": [{"type": "Kill", "text": "Besiegt die ausgehungerten Wölfe", "creature": "mob_ev_hungerwolf", "count": 12, "perWave": 4, "waveSeconds": 25},
                    {"type": "Boss", "text": "Die Rudelführerin greift selbst an!", "creature": "mob_ev_rudelfuehrerin", "yell": "Aaaauuuuuu!"}],
         "rewards": {"xp": 900, "money": 350, "reputation": [{"faction": "rep_morgentau", "amount": 150}],
                     "items": [{"item": "item_minor_healing_potion", "chance": 0.8, "min": 2, "max": 3}]}},
        {"id": "ev_mt_karren", "name": "Der Karren der Familie Lenz", "zone": "zone_morgentauwald", "position": pos(700, 470), "radius": 45,
         "minLevel": 5, "maxLevel": 10, "intervalMinutes": [35, 70], "timeOfDay": "day", "warmup": 40, "duration": 400,
         "announce": "Rotbach-Räuber haben den Händlerkarren der Familie Lenz umstellt! Verteidigt ihn!",
         "successText": "Der Karren ist gerettet – die Räuber fliehen in die Wälder.",
         "failureText": "Der Karren ist zerstört. Die Räuber feiern im Rotbach-Lager.",
         "phases": [{"type": "Defend", "text": "Schützt den Händlerkarren", "creature": "mob_ev_raeuber", "defend": "npc_ev_karren",
                     "perWave": 3, "waveSeconds": 30, "seconds": 150}],
         "rewards": {"xp": 800, "money": 400, "reputation": [{"faction": "rep_morgentau", "amount": 200}], "items": []}},
        {"id": "ev_as_flutboten", "name": "Ruf der Hohlen Flut", "zone": "zone_aschensteppe", "position": pos(760, 480), "radius": 55,
         "minLevel": 5, "maxLevel": 10, "intervalMinutes": [30, 60], "timeOfDay": "", "warmup": 45, "duration": 600,
         "announce": "Die Kultisten in der Kultschlucht beschwören einen Boten der Hohlen Flut! Haltet sie auf!",
         "successText": "Der Bote ist vernichtet, das Ritual gebrochen. Kargrak ist Euch dankbar!",
         "failureText": "Das Ritual ist vollendet – der Bote verschwindet in den Schatten …",
         "phases": [{"type": "Kill", "text": "Besiegt die Ascheprediger", "creature": "mob_ev_kultist", "count": 10, "perWave": 3, "waveSeconds": 25},
                    {"type": "Boss", "text": "Der Bote der Hohlen Flut ist erschienen!", "creature": "mob_ev_flutbote", "yell": "Die Flut verschlingt alles!"}],
         "rewards": {"xp": 900, "money": 350, "reputation": [{"faction": "rep_kargrak", "amount": 150}],
                     "items": [{"item": "item_minor_healing_potion", "chance": 0.8, "min": 2, "max": 3}]}},
        {"id": "ev_as_oase", "name": "Die Oase in Gefahr", "zone": "zone_aschensteppe", "position": pos(720, 690), "radius": 45,
         "minLevel": 5, "maxLevel": 10, "intervalMinutes": [35, 70], "timeOfDay": "night", "warmup": 40, "duration": 400,
         "announce": "Aschegeister umkreisen den Wasserstein der Oase Zharim! Ohne ihn verdurstet die Oase!",
         "successText": "Der Wasserstein ist sicher. Die Oase lebt weiter.",
         "failureText": "Der Wasserstein ist zerbrochen – die Oase trocknet aus, bis die Schamanen ihn erneuern.",
         "phases": [{"type": "Defend", "text": "Schützt den Wasserstein", "creature": "mob_aschegeist", "defend": "npc_ev_brunnen",
                     "perWave": 3, "waveSeconds": 30, "seconds": 150}],
         "rewards": {"xp": 800, "money": 400, "reputation": [{"faction": "rep_kargrak", "amount": 200}], "items": []}},
    ])

    starts = (
        ("zones/morgentauwald.json", ("sf", "sm"), "mob_irrlicht", pos(840, 230), "mob_forest_wolf", "mob_graumaehne",
         {"mob_graumaehne": dict(timeOfDay="night", respawnSeconds=1800, respawnMaxSeconds=5400),
          "mob_thornmaw": dict(persistent=True, respawnSeconds=7200, respawnMaxSeconds=14400,
                               announce="Die Erde bebt auf der Dornenhöhe – {name} ist erwacht!")}),
        ("zones/aschensteppe.json", ("kz", "nw"), "mob_aschegeist", pos(850, 230), "mob_ember_lizard", "mob_dunestalker",
         {"mob_dunestalker": dict(timeOfDay="day", respawnSeconds=1800, respawnMaxSeconds=5400),
          "mob_kharox": dict(persistent=True, respawnSeconds=7200, respawnMaxSeconds=14400,
                             announce="Der Aschenkrater speit Glut – {name} ist erwacht!")}),
    )
    for rel, caps, night_mob, night_pos, placeholder, rare, rules in starts:
        zl = load(rel)
        z = zl[0]
        hx, hz = home_spot[z["id"]]
        z["objects"] = [o for o in z["objects"] if not o["object"].startswith("obj_portal_nach_")]
        for i, ck in enumerate(caps):
            z["objects"].append({"object": f"obj_portal_nach_{ck}", "position": pos(hx - 10 + i * 20, hz + 15), "count": 1, "radius": 0, "respawnSeconds": 0})
        # Feste Reihenfolge: gen_professions.py schreibt in dieselben Dateien – so ist das Ergebnis unabhängig von der Aufrufreihenfolge.
        z["objects"].sort(key=lambda o: (o["object"], tuple(o["position"])))
        z["spawns"] = [s for s in z["spawns"] if s["creature"] != night_mob]
        z["spawns"].append({"creature": night_mob, "position": night_pos, "count": 6, "radius": 35, "respawnSeconds": 90, "wanderRadius": 10, "timeOfDay": "night"})
        for s in z["spawns"]:
            for a in ("timeOfDay", "respawnMaxSeconds", "persistent", "announce", "rareCreature", "rareChance"):
                if s["creature"] != night_mob:
                    s.pop(a, None)
            if s["creature"] in rules:
                s.update(rules[s["creature"]])
            # Platzhalter: unter diesen Gegnern versteckt sich manchmal der Seltene (nie zwei zugleich)
            if s["creature"] == placeholder and s["position"][0] > 500:
                s["rareCreature"] = rare
                s["rareChance"] = 0.05
        save(rel, zl)


if __name__ == "__main__":
    main()
