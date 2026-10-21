#!/usr/bin/env python3
"""
Block I (Kapitel 64–66): Inhalte für Stufe 12–20 – Westmark (Sonnenbündnis) und Rotsandwüste (Schattenpakt).

Erzeugt deterministisch:
  content/zones/westmark.json, content/zones/rotsand.json      spielbare Regionen mit Siedlung, Gegnern, Objekten
  content/quests/westmark.json, content/quests/rotsand.json    Questreihen inkl. Wurzelkrypta-Quests
  content/creatures/stufe12_20.json                            NPCs und Gegner
  content/objects/stufe12_20.json                              Questobjekte, Wege zwischen den Regionen, Krypta-Eingang
  content/items/stufe12_20.json                                Questgegenstände, Belohnungen, Plunder, grüne Beute
  content/travel/nodes/stufe12_20.json, content/travel/paths/stufe12_20.json   Flugpunkte
und passt an:
  content/zones/weltkarte.json        Platzhalter Westmark/Rotsandwüste entfernt (jetzt echte Zonen)
  content/zones/morgentauwald.json, aschensteppe.json   Weg in die neue Region
  content/reputations.json            Ruf der neuen Siedlungen

Aufruf: python3 tools/gen_world_i.py   (mehrfach ausführbar, Ergebnis immer gleich)
"""
import json, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import itemlib  # noqa: E402
from itemlib import armor, weapon, misc, ring  # noqa: E402

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "content")
SB, SP = "Sonnenbuendnis", "Schattenpakt"


def load(rel):
    with open(os.path.join(ROOT, rel), encoding="utf-8") as f:
        return json.load(f)


def save(rel, data):
    path = os.path.join(ROOT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        f.write(json.dumps(data, ensure_ascii=False, indent=1) + "\n")


def P(x, z):
    return [x, 0, z]


# ---- EP: wie Experience.XpToNextLevel (Shared/Rules/Experience.cs) bis Stufe 28
def xp_to_next(level):
    raw = 8.0 * level * (45 + 5 * level)
    factor = 1.0 if level < 10 else 1.05
    return int(round(raw * factor / 100.0) * 100)


QUEST_SHARE = {"normal": 0.19, "group": 0.32, "dungeon": 0.4, "talk": 0.06}


def quest_xp(level, kind):
    return int(round(xp_to_next(level) * QUEST_SHARE[kind] / 50.0) * 50)


def ab(a, interval, delay=0, rule="Current", below=1.01):
    return {"ability": a, "interval": interval, "initialDelay": delay, "chance": 1.0, "targetRule": rule, "healthBelow": below}


# ---- Belohnungen: je Quest drei Gegenstände (Stärke/Kette, Beweglichkeit/Leder, Intelligenz/Stoff)
NOUN = {"Chest": ("Kettenhemd", "Lederwams", "Robe"), "Legs": ("Beinschienen", "Beinlinge", "Hose"),
        "Hands": ("Panzerhandschuhe", "Handschuhe", "Handwickel"), "Feet": ("Schienenstiefel", "Stiefel", "Sandalen"),
        "Waist": ("Kettengurt", "Gürtel", "Schärpe"), "Wrist": ("Armschienen", "Armbänder", "Armwickel"),
        "Shoulders": ("Schulterplatten", "Schulterstücke", "Schulterumhang"), "Head": ("Helm", "Kappe", "Kapuze")}
ARCH = (("str", "Mail", ("Strength", "Stamina")), ("agi", "Leather", ("Agility", "Stamina")), ("int", "Cloth", ("Intellect", "Spirit")))
WEAPONS = {"str": (("Axe2H", "Streitaxt", 3.4), ("Sword1H", "Klinge", 2.4)), "agi": (("Bow", "Bogen", 2.6), ("Dagger", "Dolch", 1.7)),
           "int": (("Staff", "Stab", 3.0), ("Wand", "Zauberstab", 1.6))}


def stat_split(ilvl, q, names):
    budget = max(2, round(ilvl * (0.42 if q == "Uncommon" else 0.55)))
    a = (budget + 1) // 2
    return {names[0]: a, names[1]: budget - a}


def reward_set(prefix, suffix, level, slot, q="Uncommon", weapon_set=None):
    """Gibt die drei Wahlbelohnungen einer Quest zurück; legt die Gegenstände an."""
    ilvl, req = level + 2, max(1, level - 2)
    out = []
    for i, (key, atype, names) in enumerate(ARCH):
        iid = f"{prefix}_{key}"
        stats = stat_split(ilvl, q, names)
        if weapon_set is not None:
            wt, noun, speed = WEAPONS[key][weapon_set]
            weapon(iid, f"{noun} {suffix}", wt, ilvl, req, speed, q, stats)
        else:
            armor(iid, f"{NOUN[slot][i]} {suffix}", slot, atype, ilvl, req, q, stats)
        out.append({"item": iid, "count": 1})
    return out


SLOTS = ["Chest", "Legs", "Hands", "Feet", "Waist", "Wrist", "Shoulders", "Head"]

# ---- Die zwei Regionen
ZONES = [
    dict(
        key="wm", id="zone_westmark", name="Westmark", faction=SB, continent="Aurelion", biome="Ackerland",
        mapRect=[230, 690, 140, 130], music="music_westmark", ambience="amb_fields_wind", weather=["Clear", "Clear", "Rain", "Fog"],
        description="Weite Kornfelder, Windmühlen und die Grenzburg Weidenwacht. Die Sensenbrüder plündern die Höfe, und aus einem Riss im Boden kriecht die Hohle Flut.",
        rep=("rep_weidenwacht", "Burg Weidenwacht", "Die Burgwache und die Bauern der Westmark."),
        home_zone="zones/morgentauwald.json", home_node="tn_mt_dorf", home_giver="npc_mt_vogt", capital_node="tn_sf",
        home_way=P(890, 470), home_name="Morgentauwald",
        areas=[("burg", "Burg Weidenwacht", P(500, 500), 60, dict(isCity=True, isInn=True, discoveryXp=120, music="music_village")),
               ("strasse", "Weidenstraße", P(160, 500), 40, {}),
               ("felder", "Goldfelder", P(300, 340), 80, {}),
               ("muehle", "Mühlenhügel", P(720, 300), 70, {}),
               ("schilf", "Schilfufer", P(260, 720), 70, {}),
               ("grauwald", "Grauwald", P(740, 640), 80, {}),
               ("riss", "Wurzelriss", P(200, 180), 60, {}),
               ("abtei", "Abtei Sankt Elmar", P(820, 860), 60, {})],
        graveyards=[P(470, 570), P(760, 780)],
        npcs=dict(
            main=("Burgvogtin Irmela Weidenstein", "Herrin von Burg Weidenwacht", "QuestGiver", P(500, 490),
                  "Die Westmark ernährt das halbe Bündnis. Solange ich lebe, fällt kein Hof an Räuber oder Schlimmeres."),
            farm=("Bauer Hennig Krummacker", "Goldfelder", "QuestGiver", P(340, 380), "Wenn die Ernte ausfällt, hungert die Sonnenfeste. So einfach ist das."),
            mason=("Mühlenmeister Ortwin Mahlstein", "Mühlenhügel", "QuestGiver", P(640, 330), "Meine Mühlen mahlen nicht, solange die Sensenbrüder auf dem Hügel sitzen."),
            fisher=("Fischerin Wiebke Reuse", "Schilfufer", "QuestGiver", P(320, 690), "Früher war das Schilf voller Fische. Jetzt ist es voller Zähne."),
            ranger=("Waldläuferin Svea Dornbusch", "Grauwald", "QuestGiver", P(680, 600), "Der Grauwald war nie freundlich. Aber er war nie krank – bis jetzt."),
            priest=("Schwester Adelind", "Lichtpriesterin von Sankt Elmar", "QuestGiver", P(515, 515), "Die Abtei war ein Ort des Lichts. Was dort jetzt betet, betet nicht zum Licht."),
            inn=("Wirt Bodo Schankherz", "Gasthaus Zur Weide", "Innkeeper, Vendor, QuestGiver", P(485, 520), "Ein Bett, ein Eintopf und die neuesten Gerüchte – alles im Preis inbegriffen."),
            smith=("Rüstmeister Gerwin", "Waffen und Rüstungen", "Vendor, Repair", P(520, 480), "Was die Sensenbrüder zerbeulen, biege ich wieder gerade."),
            flight=("Greifenwärter Lutz", "Flugmeister", "FlightMaster", P(540, 500), "Meine Greifen fliegen nach Morgentau und zur Sonnenfeste."),
            spirit=("Geisterheilerin", "", "SpiritHealer", P(470, 575), "Noch ist Eure Zeit nicht gekommen."),
            guard=("Wache von Weidenwacht", "", "Guard", None, None)),
        guards=[P(470, 470), P(530, 470), P(470, 530), P(530, 530)],
        mobs=dict(
            wuehler=("Feldwühler", "Boar", "boar", 12, 13, [ab("mob_bite", 7, 3)], "felder", 14, "item_wm_wuehlerborste"),
            brandschatzer=("Sensenbruder-Brandschatzer", "Humanoid", "humanoid", 13, 14, [ab("mob_cleave", 9, 4)], "muehle", 12, "item_wm_sensenlumpen", dict(callsForHelp=True)),
            schuetze=("Sensenbruder-Armbruster", "Humanoid", "humanoid", 13, 15, [ab("mob_throw", 8, 0)], "muehle", 8, "item_wm_sensenlumpen", dict(callsForHelp=True)),
            schnapper=("Schilfschnapper", "Lizard", "lizard", 14, 15, [ab("mob_rend", 10, 4)], "schilf", 14, "item_wm_schnapperschuppe"),
            wolf=("Grauwald-Wolf", "Wolf", "wolf", 15, 16, [ab("mob_bite", 6, 2), ab("mob_rend", 12, 6)], "grauwald", 14, "item_wolf_fang"),
            baumhirt=("Verdorrter Baumhirt", "Treant", "treant", 16, 17, [ab("mob_armor_crush", 12, 5), ab("mob_root_grip", 16, 8, "Random")], "grauwald", 8, "item_moldy_root", dict(offset=(70, 70))),
            kriecher=("Wurzelkriecher", "Treant", "treant", 16, 17, [ab("mob_root_grip", 14, 5), ab("mob_spore_burst", 12, 3)], "riss", 14, "item_moldy_root"),
            geweihter=("Geweihter der Hohlen Flut", "Humanoid", "humanoid", 17, 18, [ab("mob_shadowbolt", 7, 0), ab("mob_heal_ally", 15, 8, "Self", 0.6)], "abtei", 12, "item_wm_kuttenfetzen", dict(callsForHelp=True)),
            hexer=("Abtei-Hexer", "Humanoid", "humanoid", 18, 19, [ab("mob_shadowbolt", 6, 0), ab("mob_terrify", 20, 10)], "abtei", 8, "item_wm_kuttenfetzen", dict(offset=(-50, 40), callsForHelp=True))),
        rare=("knorrhaut", "Knorrhaut", "Lizard", "lizard", 16, "schnapper", [ab("mob_rend", 9, 2), ab("mob_armor_crush", 14, 6)]),
        elite=("rurik", "Rurik Schwarzsense", "Anführer der Sensenbrüder", "humanoid", 16, P(780, 260), [ab("mob_cleave", 8, 4), ab("mob_shield_bash", 12, 6), ab("mob_enrage", 600, 0, "Self", 0.3)]),
        boss=("malachias", "Abt Malachias der Hohle", "Diener der Hohlen Flut", "humanoid", 20, P(860, 900),
              [ab("mob_shadowbolt", 6, 0), ab("mob_shadow_ring", 18, 8), ab("mob_terrify", 24, 12), ab("mob_dark_mending", 30, 15, "Self", 0.5)]),
        objects=dict(
            sack=("Gestohlener Getreidesack", "crate", "muehle", P(760, 280), 8, 40),
            siegel=("Entweihtes Altarsiegel", "rune_stone", "abtei", P(840, 880), 4, 20),
            flutstein=("Flutstein", "crystal", "riss", P(210, 190), 6, 45)),
        crypt_entrance=P(170, 150),
        items=dict(
            saatgut=("Gerettetes Saatgut", "Aus dem Kropf eines Feldwühlers – noch keimfähig.", [("wuehler", 0.6)]),
            getreide=("Sack Westmark-Korn", "Mit dem Brandzeichen der Weidenwacht.", [("obj:sack", 1.0)]),
            befehle=("Befehle der Sensenbrüder", "Unterzeichnet mit einer schwarzen Sense.", [("brandschatzer", 0.2), ("schuetze", 0.2)]),
            sense=("Ruriks schwarze Sense", "Die Waffe des Anführers – Beweis seines Endes.", [("elite", 1.0)]),
            panzer=("Schnapperpanzer", "Hart wie Stein, leicht wie Holz.", [("schnapper", 0.55)]),
            wildbret=("Grauwald-Wildbret", "Zähes, aber nahrhaftes Wolfsfleisch.", [("wolf", 0.6)]),
            harz=("Verdorbenes Harz", "Schwarz und warm – es pulsiert.", [("baumhirt", 0.6)]),
            foliant=("Foliant der Hohlen Flut", "Die Tinte bewegt sich, wenn man nicht hinsieht.", [("geweihter", 0.35), ("hexer", 0.35)]),
            kette=("Gebetskette des Abts", "Jede Perle ist ein Splitter schwarzen Glases.", [("boss", 1.0)]),
            brief=("Brief an Mühlenmeister Ortwin", "Versiegelt von Bauer Hennig.", [])),
        junk=[("item_wm_wuehlerborste", "Struppige Wühlerborste", 14), ("item_wm_sensenlumpen", "Rußiger Lumpen", 16),
              ("item_wm_schnapperschuppe", "Stumpfe Schnapperschuppe", 18), ("item_wm_kuttenfetzen", "Schwarzer Kuttenfetzen", 22)],
        green=("der Westmark", "Feldwacht"),
        quests=[
            ("ankunft", "Hilferuf aus der Westmark", 12, "home", "main", [], [("T", "main", "Burgvogtin Weidenstein gesprochen")], "talk",
             "Ein Bote aus der Westmark ist heute Nacht halb erfroren in Morgentau angekommen. Die Grenzburg Weidenwacht bittet um jede Klinge, die sie bekommen kann – Räuber, Bestien und Schlimmeres. Ihr habt Euch hier bewährt. Folgt der Straße nach Westen und meldet Euch bei Burgvogtin Irmela Weidenstein.",
             "Aldric schickt Euch? Dann seid Ihr mehr wert als zehn Boten. Willkommen auf Weidenwacht – wir haben Arbeit für Euch.", {}),
            ("felder", "Die Goldfelder", 12, "main", "farm", ["ankunft"], [("T", "farm", "Bauer Hennig gesprochen"), ("E", "felder", "Goldfelder erreicht")], "talk",
             "Bevor ich Euch gegen Räuber schicke, müsst Ihr das Land verstehen. Reitet zu den Goldfeldern im Nordwesten. Bauer Hennig Krummacker spricht für alle Höfe dort – und er hat schon dreimal um Hilfe gebeten.",
             "Die Burgvogtin hat also endlich jemanden geschickt. Gut. Seht Euch um – die Felder sind voller Löcher.", {}),
            ("wuehler", "Löcher im Acker", 12, "farm", "farm", ["felder"], [("K", "wuehler", 12, "Feldwühler getötet")], "normal",
             "Feldwühler. Früher ein paar im Jahr, jetzt ganze Rotten. Sie graben die Saat aus und brechen den Ochsen die Beine. Tötet zwölf von ihnen, sonst gibt es dieses Jahr keine Ernte.",
             "Zwölf weniger. Die Ochsen werden es Euch danken – und ich auch.", {"slot": "Legs", "suffix": "des Ackers"}),
            ("saatgut", "Saat für den Frühling", 12, "farm", "farm", ["felder"], [("C", "saatgut", 8, "Gerettetes Saatgut")], "normal",
             "Die Biester fressen nicht nur, sie horten. In ihren Kröpfen steckt das Saatgut, das sie uns gestohlen haben – oft noch keimfähig. Bringt mir acht Beutel davon zurück. Unappetitlich, ich weiß.",
             "Das reicht für zwei Felder. Ihr habt keine Ahnung, wie viel das hier draußen bedeutet.", {}),
            ("sensen", "Ein Brief zum Mühlenhügel", 13, "farm", "mason", ["wuehler"], [("D", "mason", "Brief überbracht"), ("E", "muehle", "Mühlenhügel erreicht")], "talk",
             "Mühlenmeister Ortwin hat seit Tagen nichts von sich hören lassen. Seine Mühlen stehen still, und nachts sehen wir Feuer auf dem Hügel. Bringt ihm diesen Brief. Und passt auf – die Sensenbrüder schießen auf alles, was sich bewegt.",
             "Hennig lebt noch? Den Lichtern sei Dank. Hier oben ist die Hölle los, das seht Ihr ja.", {"provided": "brief"}),
            ("brandschatzer", "Die Sensenbrüder", 13, "mason", "mason", ["sensen"], [("K", "brandschatzer", 10, "Brandschatzer getötet"), ("K", "schuetze", 6, "Armbruster getötet")], "normal",
             "Sie nennen sich Sensenbrüder. Deserteure, Tagelöhner, Diebe – jetzt eine Armee. Sie haben meine Mühlen besetzt und brennen jeden Hof nieder, der nicht zahlt. Zehn Brandschatzer und sechs Armbruster. Dann wissen sie, dass jemand zurückschlägt.",
             "Das wird sie nicht aufhalten, aber es wird sie vorsichtig machen. Vorsichtige Räuber brennen weniger.", {"slot": "Chest", "suffix": "des Mühlenhügels"}),
            ("getreide", "Gestohlenes Korn", 14, "mason", "mason", ["sensen"], [("C", "getreide", 8, "Säcke Westmark-Korn")], "normal",
             "Das Korn, das sie gestohlen haben, liegt in Säcken überall auf dem Hügel – sie haben noch keinen Käufer gefunden. Holt acht Säcke zurück, bevor sie es tun. Jeder Sack ist ein Winter für eine Familie.",
             "Acht Säcke! Ich lasse sie sofort zur Burg bringen. Ihr habt heute mehr Menschen satt gemacht als jeder Bäcker.", {"slot": "Hands", "suffix": "des Müllers"}),
            ("befehle", "Wer führt die Sensen?", 14, "mason", "mason", ["brandschatzer"], [("C", "befehle", 1, "Befehle der Sensenbrüder")], "normal",
             "Gewöhnliche Räuber kämpfen nicht so diszipliniert. Jemand gibt Befehle. Durchsucht die Sensenbrüder – irgendeiner trägt sicher Anweisungen bei sich.",
             "\"Rurik\". Ich kenne den Namen. Ein Hauptmann der Grenzwache – er ist vor einem Jahr verschwunden. Jetzt wissen wir, wohin.", {}),
            ("rurik", "Rurik Schwarzsense", 16, "mason", "main", ["befehle"], [("C", "sense", 1, "Ruriks schwarze Sense")], "group",
             "Rurik hält sich im Lager oben auf dem Hügel, umgeben von seinen besten Leuten. Er war einer der Besten der Grenzwache – allein schafft Ihr das nicht. Nehmt Gefährten mit, bringt mir seine Sense und überbringt der Burgvogtin die Nachricht.",
             "Rurik... Er hat unter mir gedient. Ich hätte wissen müssen, wohin seine Verbitterung führt. Ihr habt der Westmark einen großen Dienst erwiesen.",
             {"slot": "Shoulders", "suffix": "der Weidenwacht", "quality": "Rare", "players": 3, "rep": 500}),
            ("schnapper", "Zähne im Schilf", 14, "fisher", "fisher", ["ankunft"], [("K", "schnapper", 10, "Schilfschnapper getötet")], "normal",
             "Die Schnapper haben sich vermehrt wie nie. Sie zerreißen meine Netze und gestern beinahe meinen Neffen. Tötet zehn von ihnen, damit wir wieder ans Wasser können.",
             "Zehn! Heute Abend gehen wir wieder fischen. Danke, Fremde.", {"slot": "Feet", "suffix": "des Schilfs"}),
            ("panzer", "Panzer für die Reusen", 15, "fisher", "fisher", ["ankunft"], [("C", "panzer", 6, "Schnapperpanzer")], "normal",
             "Aus Schnapperpanzern bauen wir die besten Reusen – sie verrotten nicht. Bringt mir sechs, und ich baue Euch dafür etwas Hübsches aus dem, was übrig bleibt.",
             "Gute, unbeschädigte Panzer. Ihr habt ein Auge dafür.", {"slot": "Waist", "suffix": "der Fischerin"}),
            ("wildbret", "Wildbret für die Burg", 15, "inn", "inn", ["ankunft"], [("C", "wildbret", 8, "Grauwald-Wildbret")], "normal",
             "Die Burg ist voll mit Flüchtlingen von den Höfen, und meine Kammer ist leer. Die Wölfe im Grauwald sind fett geworden – bringt mir acht Stück Wildbret, und niemand geht heute hungrig ins Bett.",
             "Das gibt einen Eintopf für die ganze Burg. Ihr habt bei mir immer ein Bett frei.", {"slot": "Wrist", "suffix": "des Gastwirts"}),
            ("grauwald", "Krankheit im Grauwald", 15, "main", "ranger", ["brandschatzer"], [("T", "ranger", "Waldläuferin Svea gesprochen")], "talk",
             "Die Räuber sind ein Problem, das wir verstehen. Was im Grauwald geschieht, verstehen wir nicht. Waldläuferin Svea Dornbusch hält dort Wache. Geht zu ihr – sie wird Euch erklären, was sie mir nicht in einen Brief schreiben wollte.",
             "Irmela schickt Euch? Gut. Was ich gesehen habe, glaubt mir ohnehin niemand, der es nicht selbst sieht.", {}),
            ("woelfe", "Das Rudel des Grauwalds", 15, "ranger", "ranger", ["grauwald"], [("K", "wolf", 12, "Grauwald-Wölfe getötet")], "normal",
             "Die Wölfe sind aus dem tiefen Wald geflohen, und jetzt jagen sie an den Straßen. Etwas hat sie vertrieben. Bevor ich herausfinde, was, müssen die Wege sicher sein. Zwölf Wölfe.",
             "Ihr seid schnell. Und Ihr habt bemerkt, wie dünn sie waren? Sie fliehen vor etwas.", {"slot": "Head", "suffix": "des Waldläufers"}),
            ("baumhirten", "Verdorrte Hirten", 16, "ranger", "ranger", ["grauwald"], [("K", "baumhirt", 8, "Verdorrte Baumhirten getötet")], "normal",
             "Die Baumhirten haben diesen Wald seit tausend Jahren gehütet. Jetzt sind ihre Herzen schwarz, und sie greifen alles an, was atmet. Es bricht mir das Herz, aber erlöst acht von ihnen.",
             "Ihr habt ihnen Frieden gegeben. Mehr konnten wir nicht für sie tun.", {"slot": "Chest", "suffix": "der Hirten", "weapon": 0}),
            ("harz", "Schwarzes Harz", 16, "ranger", "priest", ["grauwald"], [("C", "harz", 8, "Verdorbenes Harz")], "normal",
             "Aus den Baumhirten tropft ein schwarzes Harz. Schwester Adelind auf der Burg sagt, sie könne erkennen, woher die Verderbnis kommt, wenn sie genug davon hat. Sammelt acht Klumpen und bringt sie ihr.",
             "Dieses Harz... es riecht nach der Wurzelkrypta. Und nach etwas noch Älterem. Es kommt aus dem Norden – aus dem Riss.", {"slot": "Hands", "suffix": "der Läuterung"}),
            ("wurzelriss", "Der Wurzelriss", 17, "priest", "priest", ["harz"], [("E", "riss", "Wurzelriss erkundet")], "talk",
             "Im Nordwesten hat sich der Boden geöffnet – die Bauern nennen es den Wurzelriss. Dort treten die Wurzeln der Krypta an die Oberfläche. Seht nach, was dort wächst, und kommt zurück.",
             "Flutsteine. Kristalle, die die Hohle Flut nähren. Wenn sie dort wachsen, wird der ganze Westen sterben.", {}),
            ("kriecher", "Kriechende Wurzeln", 17, "priest", "priest", ["wurzelriss"], [("K", "kriecher", 12, "Wurzelkriecher getötet")], "normal",
             "Die Wurzelkriecher schützen die Flutsteine wie Ameisen ihre Königin. Lichtet ihre Reihen – zwölf von ihnen –, damit wir an die Steine herankommen.",
             "Der Riss ist ruhiger. Jetzt sind die Steine dran.", {"slot": "Legs", "suffix": "der Wurzeln", "weapon": 1}),
            ("flutsteine", "Flutsteine zerschlagen", 17, "priest", "priest", ["wurzelriss"], [("I", "flutstein", 5, "Flutsteine zerschlagen")], "normal",
             "Die Flutsteine lassen sich nicht wegtragen – sie müssen an Ort und Stelle zerschlagen werden. Ich habe sie gesegnet... hoffentlich reicht das. Zerschlagt fünf von ihnen.",
             "Ich habe es gespürt, als sie zersprungen sind. Aber ich habe noch etwas gespürt – eine Antwort. Aus Sankt Elmar.", {"slot": "Feet", "suffix": "des Lichts"}),
            ("abtei", "Was in Sankt Elmar betet", 18, "priest", "priest", ["flutsteine"], [("E", "abtei", "Abtei Sankt Elmar erreicht"), ("K", "geweihter", 8, "Geweihte getötet")], "normal",
             "Sankt Elmar war meine Abtei. Abt Malachias hat uns vor einem Jahr fortgeschickt – er wollte allein beten, sagte er. Jetzt weiß ich, zu wem. Geht hin und tötet acht der Geweihten, die jetzt dort hausen.",
             "Ich kannte einige von ihnen. Ich bete für sie – ein letztes Mal.", {"slot": "Waist", "suffix": "von Sankt Elmar"}),
            ("hexer", "Die Hexer der Abtei", 18, "priest", "priest", ["abtei"], [("K", "hexer", 8, "Abtei-Hexer getötet")], "normal",
             "Die Hexer sind die gefährlichsten. Sie beten nicht – sie befehlen. Tötet acht von ihnen, bevor sie die Hohle Flut ganz in die Abtei rufen.",
             "Ohne die Hexer ist Malachias verwundbar. Wir kommen ihm näher.", {"slot": "Shoulders", "suffix": "der Wachsamkeit"}),
            ("folianten", "Die schwarzen Folianten", 19, "priest", "priest", ["abtei"], [("C", "foliant", 6, "Folianten der Hohlen Flut")], "normal",
             "Malachias hat seine Gebete aufgeschrieben, und die Geweihten tragen Abschriften bei sich. Bringt mir sechs Folianten. Ich muss wissen, was er vorhat – und wie man es beendet.",
             "Er will die Abtei als Tor benutzen. Für die Flut. Wir haben nicht mehr viel Zeit.", {"slot": "Head", "suffix": "der Erkenntnis"}),
            ("siegel", "Die entweihten Siegel", 19, "priest", "priest", ["abtei"], [("I", "siegel", 4, "Altarsiegel gereinigt")], "normal",
             "Vier Siegel auf dem Altar halten den Abt in seiner Macht. Reinigt sie mit diesem Weihwasser – dann ist er nur noch ein alter Mann mit schwarzer Zunge.",
             "Die Siegel sind gebrochen. Jetzt ist er angreifbar.", {"slot": "Wrist", "suffix": "der Weihe"}),
            ("malachias", "Abt Malachias der Hohle", 20, "priest", "main", ["folianten", "siegel"], [("K", "boss", 1, "Abt Malachias besiegt"), ("C", "kette", 1, "Gebetskette des Abts")], "group",
             "Es ist Zeit. Malachias wartet in der Krypta unter der Abtei. Nehmt Gefährten mit – er ist stärker, als ein Mensch sein sollte. Bringt seine Gebetskette zur Burgvogtin, damit die Westmark erfährt, dass es vorbei ist.",
             "Es ist vorbei. Die Westmark wird sich erholen – dank Euch. Diese Burg steht in Eurer Schuld, solange ihre Mauern stehen.",
             {"suffix": "des Abtbezwingers", "quality": "Rare", "weapon": 0, "players": 3, "rep": 1000, "category": "Main"}),
            ("wk_moderkeim", "Fäulnis an der Wurzel", 15, "ranger", "ranger", ["grauwald"], [("K", "boss_moderkeim", 1, "Moderkeim besiegt")], "dungeon",
             "Unter dem Riss beginnen die Gänge der Wurzelkrypta. Ein Pilzwesen namens Moderkeim verbreitet dort die Sporen, die meine Baumhirten verdorben haben. Sammelt eine Gruppe und vernichtet es – der Eingang liegt am Wurzelriss.",
             "Ich spüre es schon: Die Sporen im Grauwald werden weniger. Ihr habt dem Wald seine Lunge zurückgegeben.", {"slot": "Feet", "suffix": "der Krypta", "quality": "Rare", "players": 5}),
            ("wk_untote", "Die Toten unter den Wurzeln", 16, "ranger", "ranger", ["grauwald"], [("K", "dmob_crypt_shambler", 12, "Gruftschlurfer vernichtet")], "dungeon",
             "In der Krypta schlurfen die Toten – alte Ritter der Grenzwache, deren Gräber die Wurzeln gesprengt haben. Gebt zwölf von ihnen die Ruhe zurück.",
             "Die alten Wächter ruhen wieder. Die Grenzwache dankt Euch, auch wenn keiner von ihnen es mehr sagen kann.", {"slot": "Wrist", "suffix": "der Grenzwache", "players": 5}),
            ("wk_elvaine", "Die gefallene Dryade", 17, "ranger", "ranger", ["grauwald"], [("K", "boss_elvaine", 1, "Elvaine erlöst")], "dungeon",
             "Elvaine war die Hüterin des Grauwalds, bevor die Flut sie holte. Jetzt dient sie Morrak in der Krypta. Erlöst sie. Mehr Gnade bleibt uns nicht.",
             "Elvaine ist frei. Vielleicht wird eines Tages wieder eine Dryade über diesen Wald wachen.", {"slot": "Back", "suffix": "der Dryade", "quality": "Rare", "players": 5}),
        ]),
    dict(
        key="rs", id="zone_rotsand", name="Rotsandwüste", faction=SP, continent="Kharvath", biome="Wüste",
        mapRect=[280, 720, 130, 130], music="music_rotsand", ambience="amb_desert_wind", weather=["Clear", "Clear", "Storm"],
        description="Rote Dünen, Gräber vergessener Könige und die Festungsoase Zharas Rast. Dünenräuber überfallen die Karawanen, und in einem alten Sonnentempel betet man zur Hohlen Flut.",
        rep=("rep_zharas_rast", "Zharas Rast", "Die Karawanen und Krieger der Rotsandwüste."),
        home_zone="zones/aschensteppe.json", home_node="tn_as_oase", home_giver="npc_as_chief", capital_node="tn_kz",
        home_way=P(900, 520), home_name="Aschensteppe",
        areas=[("rast", "Zharas Rast", P(500, 500), 60, dict(isCity=True, isInn=True, discoveryXp=120, music="music_village")),
               ("pfad", "Karawanenpfad", P(160, 520), 40, {}),
               ("duenen", "Dünenmeer", P(300, 320), 80, {}),
               ("klippen", "Räuberklippen", P(720, 300), 70, {}),
               ("senke", "Glassenke", P(260, 720), 70, {}),
               ("graeber", "Königsgräber", P(740, 640), 80, {}),
               ("riss", "Dürrwurzelriss", P(200, 180), 60, {}),
               ("tempel", "Tempel der Versunkenen Sonne", P(820, 860), 60, {})],
        graveyards=[P(470, 570), P(760, 780)],
        npcs=dict(
            main=("Zhara Sandklinge", "Herrin von Zharas Rast", "QuestGiver", P(500, 490),
                  "Diese Oase ist das einzige Wasser auf hundert Meilen. Wer sie hält, hält die Wüste. Und ich halte sie."),
            farm=("Karawanenmeister Orrak", "Dünenmeer", "QuestGiver", P(340, 360), "Jede Karawane, die nicht ankommt, ist ein Dorf, das hungert."),
            mason=("Späherin Ishra", "Räuberklippen", "QuestGiver", P(640, 330), "Von hier oben sehe ich jeden Räuber. Leider sehen sie mich auch."),
            fisher=("Brunnenhüterin Nehal", "Glassenke", "QuestGiver", P(320, 690), "Unter dem Glas der Senke liegt Wasser – und Tote, die es bewachen."),
            ranger=("Totenrufer Vashkar", "Königsgräber", "QuestGiver", P(680, 600), "Die alten Könige schlafen unruhig. Ich höre sie flüstern."),
            priest=("Seher Rakhim", "Hüter des Sonnenwissens", "QuestGiver", P(515, 515), "Der Tempel der Versunkenen Sonne war heilig. Jetzt ist er hohl."),
            inn=("Wirtin Shazra", "Rasthaus Zum Kühlen Krug", "Innkeeper, Vendor, QuestGiver", P(485, 520), "Wasser, Schatten und ein weiches Lager. Mehr braucht niemand in der Wüste."),
            smith=("Waffenschmied Grothak", "Waffen und Rüstungen", "Vendor, Repair", P(520, 480), "Sand frisst jede Klinge. Ich mache sie wieder scharf."),
            flight=("Windreiterin Kesra", "Flugmeisterin", "FlightMaster", P(540, 500), "Meine Windreiter fliegen zur Oase Zharim und nach Kharzul."),
            spirit=("Geisterheiler", "", "SpiritHealer", P(470, 575), "Die Wüste hat dich noch nicht verschluckt. Geh zurück."),
            guard=("Wache von Zharas Rast", "", "Guard", None, None)),
        guards=[P(470, 470), P(530, 470), P(470, 530), P(530, 530)],
        mobs=dict(
            schakal=("Rotsand-Schakal", "Hyena", "hyena", 12, 13, [ab("mob_bite", 7, 3)], "duenen", 14, "item_rs_schakalfell"),
            skorpion=("Glutstachel-Skorpion", "Beetle", "beetle", 13, 14, [ab("mob_poison_bite", 10, 4)], "duenen", 10, "item_rs_chitinsplitter", dict(offset=(60, -50))),
            raeuber=("Dünenräuber", "Humanoid", "humanoid", 13, 14, [ab("mob_cleave", 9, 4)], "klippen", 12, "item_rs_staubtuch", dict(callsForHelp=True)),
            schleuderer=("Dünenräuber-Schleuderer", "Humanoid", "humanoid", 13, 15, [ab("mob_throw", 8, 0)], "klippen", 8, "item_rs_staubtuch", dict(callsForHelp=True)),
            waechter=("Grabwächter", "Undead", "undead", 14, 15, [ab("mob_armor_crush", 11, 4)], "senke", 14, "item_rs_grabbinde"),
            wurm=("Sandwurm", "Wyrm", "wyrm", 15, 16, [ab("mob_bite", 6, 2), ab("mob_ground_slam", 16, 6)], "graeber", 12, "item_rs_wurmzahn"),
            kriecher=("Dürrwurzel-Kriecher", "Treant", "treant", 16, 17, [ab("mob_root_grip", 14, 5), ab("mob_spore_burst", 12, 3)], "riss", 14, "item_moldy_root"),
            geweihter=("Geweihter der Hohlen Flut", "Humanoid", "humanoid", 17, 18, [ab("mob_shadowbolt", 7, 0), ab("mob_heal_ally", 15, 8, "Self", 0.6)], "tempel", 12, "item_rs_kultmaske", dict(callsForHelp=True)),
            seherin=("Seherin der Hohlen Flut", "Humanoid", "humanoid", 18, 19, [ab("mob_shadowbolt", 6, 0), ab("mob_terrify", 20, 10)], "tempel", 8, "item_rs_kultmaske", dict(offset=(-50, 40), callsForHelp=True))),
        rare=("ussar", "Gräberkönig Ussar", "Undead", "undead", 16, "waechter", [ab("mob_armor_crush", 9, 2), ab("mob_terrify", 20, 8)]),
        elite=("mardra", "Mardra Klippenfürstin", "Anführerin der Dünenräuber", "humanoid", 16, P(780, 260), [ab("mob_cleave", 8, 4), ab("mob_net", 14, 6, "Random"), ab("mob_enrage", 600, 0, "Self", 0.3)]),
        boss=("nethra", "Hohepriesterin Nethra", "Stimme der Hohlen Flut", "humanoid", 20, P(860, 900),
              [ab("mob_shadowbolt", 6, 0), ab("mob_shadow_ring", 18, 8), ab("mob_terrify", 24, 12), ab("mob_dark_mending", 30, 15, "Self", 0.5)]),
        objects=dict(
            sack=("Gestohlenes Wasserfass", "crate", "klippen", P(760, 280), 8, 40),
            siegel=("Entweihter Sonnenaltar", "rune_stone", "tempel", P(840, 880), 4, 20),
            flutstein=("Flutstein", "crystal", "riss", P(210, 190), 6, 45)),
        crypt_entrance=P(170, 150),
        items=dict(
            stachel=("Glutstachel", "Brennt noch Stunden nach dem Tod des Skorpions.", [("skorpion", 0.6)]),
            wasser=("Fass mit Oasenwasser", "Mit dem Zeichen von Zharas Rast.", [("obj:sack", 1.0)]),
            karte=("Beuteplan der Dünenräuber", "Eine Karte der Karawanenwege, jede Route mit einem Kreuz.", [("raeuber", 0.2), ("schleuderer", 0.2)]),
            krone=("Mardras Kriegskrone", "Ein Reif aus Karawanengold.", [("elite", 1.0)]),
            skarabaeus=("Grabskarabäus", "Ein Amulett aus blauem Glas, kalt wie Mondlicht.", [("waechter", 0.55)]),
            wurmfleisch=("Sandwurmfleisch", "Zäh, aber nahrhaft – wenn man es lange genug kocht.", [("wurm", 0.6)]),
            harz=("Dürrwurzel-Harz", "Schwarz und warm – es pulsiert.", [("kriecher", 0.6)]),
            schriftrolle=("Schriftrolle der Hohlen Flut", "Die Zeichen kriechen über das Papier.", [("geweihter", 0.35), ("seherin", 0.35)]),
            auge=("Auge der Hohepriesterin", "Ein schwarzer Kristall, der in der Hand pocht.", [("boss", 1.0)]),
            botschaft=("Botschaft für Späherin Ishra", "Versiegelt von Karawanenmeister Orrak.", [])),
        junk=[("item_rs_schakalfell", "Räudiges Schakalfell", 14), ("item_rs_chitinsplitter", "Chitinsplitter", 15), ("item_rs_staubtuch", "Staubiges Kopftuch", 16),
              ("item_rs_grabbinde", "Vergilbte Grabbinde", 18), ("item_rs_wurmzahn", "Abgebrochener Wurmzahn", 20), ("item_rs_kultmaske", "Gesprungene Kultmaske", 22)],
        green=("des Rotsands", "Dünenwacht"),
        quests=[
            ("ankunft", "Ruf aus der Rotsandwüste", 12, "home", "main", [], [("T", "main", "Zhara Sandklinge gesprochen")], "talk",
             "Eine Windreiterin aus dem Westen ist gestern gelandet – mit einem Pfeil in der Schulter. Zharas Rast braucht Krieger. Räuber, Bestien und ein Kult, über den niemand gerne spricht. Du hast dich hier bewiesen. Folge dem Karawanenpfad nach Westen und melde dich bei Zhara Sandklinge.",
             "Der Kriegshäuptling schickt dich? Gut. Wasser haben wir genug – Klingen nicht. Willkommen in Zharas Rast.", {}),
            ("felder", "Die Karawanen des Dünenmeers", 12, "main", "farm", ["ankunft"], [("T", "farm", "Karawanenmeister Orrak gesprochen"), ("E", "duenen", "Dünenmeer erreicht")], "talk",
             "Die Karawanen sind unser Blut. Ohne sie verdurstet Kharzul langsam an Gold- und Getreidemangel. Karawanenmeister Orrak lagert am Rand des Dünenmeers. Hilf ihm.",
             "Zhara schickt Verstärkung? Endlich. Sieh dich um – die Schakale lauern hinter jeder Düne.", {}),
            ("schakale", "Schakale der Dünen", 12, "farm", "farm", ["felder"], [("K", "schakal", 12, "Rotsand-Schakale getötet")], "normal",
             "Die Schakale jagen in Rudeln und reißen die Lasttiere, sobald sie zurückfallen. Zwölf Schakale. Dann können meine Tiere wieder in Ruhe trinken.",
             "Die Rudel sind kleiner geworden. Die Lasttiere spüren es schon.", {"slot": "Legs", "suffix": "der Karawane"}),
            ("stachel", "Gift gegen Gift", 12, "farm", "farm", ["felder"], [("C", "stachel", 8, "Glutstachel")], "normal",
             "Die Glutstachel-Skorpionen haben drei meiner Treiber gestochen. Das einzige Gegengift wird aus ihren eigenen Stacheln gebraut. Bring mir acht.",
             "Damit brauen wir genug Gegengift für die nächsten drei Karawanen. Danke.", {}),
            ("botschaft", "Botschaft zu den Klippen", 13, "farm", "mason", ["schakale"], [("D", "mason", "Botschaft überbracht"), ("E", "klippen", "Räuberklippen erreicht")], "talk",
             "Späherin Ishra beobachtet die Räuberklippen im Nordosten. Seit Tagen keine Nachricht. Bring ihr diese Botschaft und sieh nach, ob sie noch lebt.",
             "Orrak denkt also noch an mich. Ich lebe – aber nicht mehr lange, wenn das so weitergeht.", {"provided": "botschaft"}),
            ("raeuber", "Die Dünenräuber", 13, "mason", "mason", ["botschaft"], [("K", "raeuber", 10, "Dünenräuber getötet"), ("K", "schleuderer", 6, "Schleuderer getötet")], "normal",
             "Die Dünenräuber haben sich in den Klippen eingenistet. Jede Karawane, die vorbeizieht, verliert Wasser, Waren und Leben. Zehn Räuber und sechs Schleuderer. Zeig ihnen, dass der Pakt zurückschlägt.",
             "Sie werden sich jetzt zweimal überlegen, ob sie angreifen. Das ist mehr, als ich allein geschafft habe.", {"slot": "Chest", "suffix": "der Klippen"}),
            ("wasser", "Gestohlenes Wasser", 14, "mason", "mason", ["botschaft"], [("C", "wasser", 8, "Fässer mit Oasenwasser")], "normal",
             "Sie stehlen das Wasser nicht, um es zu trinken – sie verkaufen es zurück. Acht Fässer stehen überall in den Klippen. Hol sie zurück.",
             "Acht Fässer! Ich schicke sie sofort zur Rast. Heute Nacht verdurstet niemand.", {"slot": "Hands", "suffix": "der Wasserträger"}),
            ("karte", "Wer führt die Räuber?", 14, "mason", "mason", ["raeuber"], [("C", "karte", 1, "Beuteplan der Dünenräuber")], "normal",
             "Räuber planen nicht so genau. Jemand kennt jede Karawanenroute. Durchsuche sie – einer trägt sicher einen Plan bei sich.",
             "Mardra. Die Klippenfürstin. Sie war einst Karawanenwache – sie kennt jeden Weg, weil sie ihn selbst bewacht hat.", {}),
            ("mardra", "Mardra Klippenfürstin", 16, "mason", "main", ["karte"], [("C", "krone", 1, "Mardras Kriegskrone")], "group",
             "Mardra hält sich oben in den Klippen, umgeben von ihren besten Leuten. Allein schaffst du das nicht. Nimm Gefährten mit, bring mir ihre Krone und überbringe Zhara die Nachricht.",
             "Mardra... Wir sind zusammen geritten, vor langer Zeit. Die Wüste verändert Menschen. Du hast der Rast einen großen Dienst erwiesen.",
             {"slot": "Shoulders", "suffix": "von Zharas Rast", "quality": "Rare", "players": 3, "rep": 500}),
            ("waechter", "Die Wächter der Senke", 14, "fisher", "fisher", ["ankunft"], [("K", "waechter", 10, "Grabwächter vernichtet")], "normal",
             "Unter dem Glas der Senke liegt eine Quelle. Aber die Grabwächter erheben sich, sobald jemand sich nähert. Vernichte zehn, damit wir graben können.",
             "Endlich Ruhe. Die Grabungen können beginnen.", {"slot": "Feet", "suffix": "der Senke"}),
            ("skarabaeus", "Blaue Skarabäen", 15, "fisher", "fisher", ["ankunft"], [("C", "skarabaeus", 6, "Grabskarabäen")], "normal",
             "Die Wächter tragen Skarabäen aus blauem Glas. Sie binden ihre Seelen an die Gräber. Bring mir sechs – ich zerbreche sie, damit die Toten endlich gehen können.",
             "Ich höre sie seufzen, wenn das Glas bricht. Danke.", {"slot": "Waist", "suffix": "der Brunnenhüterin"}),
            ("wurmfleisch", "Fleisch für die Rast", 15, "inn", "inn", ["ankunft"], [("C", "wurmfleisch", 8, "Sandwurmfleisch")], "normal",
             "Die Rast ist voll mit Flüchtlingen aus den Karawanen, und mein Vorratsraum ist leer. Die Sandwürmer bei den Königsgräbern sind riesig. Bring mir acht Stück Fleisch.",
             "Das gibt Eintopf für alle. Bei mir hast du immer ein Lager frei.", {"slot": "Wrist", "suffix": "der Wirtin"}),
            ("graeber", "Unruhe in den Königsgräbern", 15, "main", "ranger", ["raeuber"], [("T", "ranger", "Totenrufer Vashkar gesprochen")], "talk",
             "Die Räuber sind ein Problem, das ich verstehe. Was in den Königsgräbern geschieht, verstehe ich nicht. Totenrufer Vashkar wacht dort. Geh zu ihm.",
             "Zhara schickt dich? Dann hör gut zu. Die Toten sprechen – und was sie sagen, gefällt mir nicht.", {}),
            ("wuermer", "Würmer unter den Gräbern", 15, "ranger", "ranger", ["graeber"], [("K", "wurm", 12, "Sandwürmer getötet")], "normal",
             "Die Sandwürmer wühlen sich durch die Gräber und fressen, was sie finden. Etwas treibt sie aus der Tiefe herauf. Töte zwölf, bevor sie die Grabkammern ganz einstürzen lassen.",
             "Hast du bemerkt, wie sie geflohen sind? Nicht vor dir. Vor etwas unter ihnen.", {"slot": "Head", "suffix": "des Totenrufers"}),
            ("koenige", "Die rastlosen Könige", 16, "ranger", "ranger", ["graeber"], [("E", "graeber", "Königsgräber erkundet"), ("K", "waechter", 10, "Grabwächter vernichtet")], "normal",
             "Die alten Könige rufen ihre Wächter aus der Glassenke zu sich – sie wollen sich erheben. Geh in die Gräber und sieh nach, dann vernichte zehn Wächter, bevor ein Heer daraus wird.",
             "Die Könige schweigen wieder. Für eine Weile.", {"slot": "Chest", "suffix": "der Könige", "weapon": 0}),
            ("riss_harz", "Schwarzes Harz", 16, "ranger", "priest", ["graeber"], [("C", "harz", 8, "Dürrwurzel-Harz")], "normal",
             "Im Nordwesten brechen schwarze Wurzeln aus dem Sand. Aus den Kriechern dort tropft ein Harz, das nach Tod riecht. Seher Rakhim kann lesen, woher es kommt. Sammle acht Klumpen und bring sie ihm.",
             "Dieses Harz... es kommt aus der Wurzelkrypta. Die Wurzeln reichen bis hierher, unter dem ganzen Kontinent hindurch.", {"slot": "Hands", "suffix": "der Läuterung"}),
            ("riss", "Der Dürrwurzelriss", 17, "priest", "priest", ["riss_harz"], [("E", "riss", "Dürrwurzelriss erkundet")], "talk",
             "Geh zum Dürrwurzelriss und sieh dir an, was dort wächst. Dann komm zurück.",
             "Flutsteine. Kristalle, die die Hohle Flut nähren. Wenn sie hier wachsen, stirbt die Oase.", {}),
            ("kriecher", "Kriechende Wurzeln", 17, "priest", "priest", ["riss"], [("K", "kriecher", 12, "Dürrwurzel-Kriecher getötet")], "normal",
             "Die Kriecher schützen die Flutsteine. Lichte ihre Reihen – zwölf von ihnen.", "Der Riss ist ruhiger. Jetzt die Steine.", {"slot": "Legs", "suffix": "der Dürrwurzel", "weapon": 1}),
            ("flutsteine", "Flutsteine zerschlagen", 17, "priest", "priest", ["riss"], [("I", "flutstein", 5, "Flutsteine zerschlagen")], "normal",
             "Die Flutsteine müssen an Ort und Stelle zerschlagen werden. Ich habe meinen Segen auf deine Waffe gelegt. Zerschlage fünf von ihnen.",
             "Ich habe es gespürt, als sie zersprungen sind – und eine Antwort. Aus dem Tempel der Versunkenen Sonne.", {"slot": "Feet", "suffix": "der Sonne"}),
            ("tempel", "Der hohle Tempel", 18, "priest", "priest", ["flutsteine"], [("E", "tempel", "Tempel der Versunkenen Sonne erreicht"), ("K", "geweihter", 8, "Geweihte getötet")], "normal",
             "Der Tempel war unser heiligster Ort. Hohepriesterin Nethra hat die Seher vertrieben und betet dort nun zur Flut. Geh hin und töte acht ihrer Geweihten.",
             "Einige von ihnen waren meine Schüler. Möge die Sonne ihnen vergeben.", {"slot": "Waist", "suffix": "des Tempels"}),
            ("seherinnen", "Die falschen Seherinnen", 18, "priest", "priest", ["tempel"], [("K", "seherin", 8, "Seherinnen getötet")], "normal",
             "Die Seherinnen der Flut sind Nethras rechte Hand. Sie sehen nicht – sie befehlen. Töte acht von ihnen.",
             "Ohne ihre Seherinnen ist Nethra verwundbar.", {"slot": "Shoulders", "suffix": "der Wachsamkeit"}),
            ("schriftrollen", "Die schwarzen Schriftrollen", 19, "priest", "priest", ["tempel"], [("C", "schriftrolle", 6, "Schriftrollen der Hohlen Flut")], "normal",
             "Nethras Anhänger tragen Abschriften ihrer Gebete. Bring mir sechs Schriftrollen. Ich muss wissen, was sie vorhat.",
             "Sie will den Tempel als Tor nutzen – für die Flut. Wir haben nicht mehr viel Zeit.", {"slot": "Head", "suffix": "der Erkenntnis"}),
            ("altar", "Die entweihten Altäre", 19, "priest", "priest", ["tempel"], [("I", "siegel", 4, "Sonnenaltäre gereinigt")], "normal",
             "Vier Sonnenaltäre nähren Nethras Macht. Reinige sie mit diesem Sonnenöl, dann ist sie verwundbar.",
             "Die Altäre leuchten wieder. Jetzt ist sie angreifbar.", {"slot": "Wrist", "suffix": "der Weihe"}),
            ("nethra", "Hohepriesterin Nethra", 20, "priest", "main", ["schriftrollen", "altar"], [("K", "boss", 1, "Hohepriesterin Nethra besiegt"), ("C", "auge", 1, "Auge der Hohepriesterin")], "group",
             "Es ist Zeit. Nethra wartet im Allerheiligsten des Tempels. Nimm Gefährten mit. Bring ihr Auge zu Zhara, damit die Wüste erfährt, dass es vorbei ist.",
             "Es ist vorbei. Die Rotsandwüste wird sich erholen – dank dir. Zharas Rast steht in deiner Schuld.",
             {"suffix": "der Sonnenbezwinger", "quality": "Rare", "weapon": 0, "players": 3, "rep": 1000, "category": "Main"}),
            ("wk_moderkeim", "Fäulnis an der Wurzel", 15, "ranger", "ranger", ["graeber"], [("K", "boss_moderkeim", 1, "Moderkeim besiegt")], "dungeon",
             "Unter dem Dürrwurzelriss beginnen die Gänge der Wurzelkrypta. Das Pilzwesen Moderkeim verbreitet dort die Sporen, die die Würmer aus der Tiefe treiben. Sammle eine Gruppe und vernichte es.",
             "Die Toten flüstern leiser. Du hast der Wüste einen Atemzug geschenkt.", {"slot": "Feet", "suffix": "der Krypta", "quality": "Rare", "players": 5}),
            ("wk_untote", "Die Toten unter den Wurzeln", 16, "ranger", "ranger", ["graeber"], [("K", "dmob_crypt_shambler", 12, "Gruftschlurfer vernichtet")], "dungeon",
             "In der Krypta schlurfen die Toten, deren Gräber die Wurzeln gesprengt haben. Gib zwölf von ihnen die Ruhe zurück.",
             "Sie ruhen wieder. Die Ahnen danken dir.", {"slot": "Wrist", "suffix": "der Ahnen", "players": 5}),
            ("wk_elvaine", "Die gefallene Dryade", 17, "ranger", "ranger", ["graeber"], [("K", "boss_elvaine", 1, "Elvaine erlöst")], "dungeon",
             "Elvaine war einst eine Hüterin des Lebens. Jetzt dient sie Morrak in der Krypta. Erlöse sie.",
             "Ihr Geist ist frei. Die Toten sagen, sie habe gelächelt.", {"slot": "Back", "suffix": "der Dryade", "quality": "Rare", "players": 5}),
        ]),
]


def junk_items(spec):
    for iid, name, sell in spec["junk"]:
        misc(iid, name, "Misc", "Poor", stack=10, sell=sell, desc="Kann bei Händlern verkauft werden.", ilvl=15)


def green_items(spec):
    """Zufällige grüne Beute der Region (Stufe 15 und 19)."""
    suffix, prefix = spec["green"]
    k = spec["key"]
    ids = []
    for ilvl in (15, 19):
        for slot, atype, noun, stats in [("Feet", "Leather", "Pirschstiefel", ("Agility", "Stamina")), ("Hands", "Cloth", "Seidenhandschuhe", ("Intellect", "Spirit")),
                                         ("Waist", "Mail", "Kettengurt", ("Strength", "Stamina")), ("Wrist", "Leather", "Armbänder", ("Agility", "Stamina"))]:
            iid = f"item_{k}_green_{slot.lower()}_{ilvl}"
            armor(iid, f"{prefix}-{noun} {suffix}", slot, atype, ilvl, ilvl - 4, "Uncommon", stat_split(ilvl, "Uncommon", stats))
            ids.append(iid)
        for wt, noun, speed, stats in [("Sword1H", "Säbel", 2.4, ("Strength", "Stamina")), ("Staff", "Wanderstab", 3.0, ("Intellect", "Spirit"))]:
            iid = f"item_{k}_green_{wt.lower()}_{ilvl}"
            weapon(iid, f"{prefix}-{noun} {suffix}", wt, ilvl, ilvl - 4, speed, "Uncommon", stat_split(ilvl, "Uncommon", stats))
            ids.append(iid)
    return ids


def build_zone(spec, creatures, objects, quests, nodes, paths):
    k, fac = spec["key"], spec["faction"]
    zid = spec["id"]
    area = {a[0]: f"area_{k}_{a[0]}" for a in spec["areas"]}
    center = {a[0]: a[2] for a in spec["areas"]}
    hub = spec["areas"][0][0]
    rep = spec["rep"][0]

    def npc_id(key):
        return spec["home_giver"] if key == "home" else f"npc_{k}_{key}"

    def mob_id(key):
        if key.startswith(("boss_", "dmob_", "mob_")):
            return key
        if key in ("rare", "elite", "boss"):
            return f"mob_{k}_{spec[key][0]}"
        return f"mob_{k}_{key}"

    def qitem(key):
        return f"item_q_{k}_{key}"

    def qid(key):
        return f"q_{k}_{key}"

    # ---- NPCs
    npc_spawns = []
    for key, (name, title, roles, position, gossip) in spec["npcs"].items():
        d = {"id": npc_id(key), "name": name, "title": title, "faction": "Friendly" if key == "spirit" else fac, "roles": roles,
             "minLevel": 60 if key == "spirit" else 30 if key == "guard" else 25, "maxLevel": 60 if key == "spirit" else 30 if key == "guard" else 25, "model": "npc"}
        if gossip:
            d["gossip"] = gossip
        if key == "inn":
            d["vendorItems"] = ["item_tough_jerky", "item_fresh_bread", "item_refreshing_water", "item_ice_cold_milk", "item_lesser_healing_potion", "item_bag_leather"]
            d["barks"] = ["Frisch aus dem Topf!", "Noch einen Krug?"]
        if key == "smith":
            d["vendorItems"] = [n for n in load("creatures/npcs.json") if n["id"] == "npc_mt_smith"][0]["vendorItems"]
        if key == "guard":
            d.update(healthMultiplier=3, damageMultiplier=2, aggroRadius=20, barks=["Alles ruhig.", "Augen offen halten."])
        creatures.append(d)
        if position is not None:
            npc_spawns.append({"npc": d["id"], "position": position, "facing": 0.0})
    for gp in spec["guards"]:
        npc_spawns.append({"npc": npc_id("guard"), "position": gp, "facing": 0.0})

    # ---- Questgegenstände und ihre Quellen
    drops = {}  # Quelle -> [(item, chance, quest)]
    item_quest = {}
    for q in spec["quests"]:
        for o in q[6]:
            if o[0] == "C":
                item_quest.setdefault(o[1], qid(q[0]))
    for key, (name, desc, sources) in spec["items"].items():
        misc(qitem(key), name, "Quest", "Common", stack=20, desc=desc)
        for src, chance in sources:
            drops.setdefault(src, []).append({"item": qitem(key), "chance": chance, "min": 1, "max": 1, "quest": item_quest[key]})
    junk_items(spec)
    greens = green_items(spec)

    # ---- Gegner
    spawns = []
    for key, m in spec["mobs"].items():
        name, family, model, lo, hi, abilities, where, count, junk = m[:9]
        extra = dict(m[9]) if len(m) > 9 else {}
        off = extra.pop("offset", (0, 0))
        loot = [{"item": junk, "chance": 0.45, "min": 1, "max": 1}] + drops.get(key, [])
        loot += [{"item": g, "chance": 0.003, "min": 1, "max": 1} for g in greens]
        d = {"id": mob_id(key), "name": name, "minLevel": lo, "maxLevel": hi, "family": family, "faction": "Hostile", "model": model,
             "moneyMin": lo * 4, "moneyMax": hi * 9, "abilities": abilities, "loot": loot}
        d.update(extra)
        creatures.append(d)
        c = center[where]
        spawns.append({"creature": d["id"], "position": P(c[0] + off[0], c[2] + off[1]), "count": count, "radius": 55, "respawnSeconds": 45, "wanderRadius": 10})

    rkey, rname, rfam, rmodel, rlvl, rhost, rab = spec["rare"]
    creatures.append({"id": mob_id("rare"), "name": rname, "minLevel": rlvl, "maxLevel": rlvl, "family": rfam, "faction": "Hostile", "model": rmodel,
                      "rare": True, "healthMultiplier": 1.8, "damageMultiplier": 1.3, "scale": 1.3, "moneyMin": 400, "moneyMax": 800, "abilities": rab,
                      "loot": [{"item": g, "chance": 0.5, "min": 1, "max": 1} for g in greens if g.endswith("_19")][:3]})
    for s in spawns:
        if s["creature"] == mob_id(rhost):
            s["rareCreature"] = mob_id("rare")
            s["rareChance"] = 0.05

    ekey, ename, etitle, emodel, elvl, epos, eab = spec["elite"]
    creatures.append({"id": mob_id("elite"), "name": ename, "title": etitle, "minLevel": elvl, "maxLevel": elvl, "family": "Humanoid", "faction": "Hostile",
                      "model": emodel, "elite": True, "healthMultiplier": 1.3, "damageMultiplier": 1.0, "callsForHelp": True, "scale": 1.2,
                      "moneyMin": 500, "moneyMax": 1000, "abilities": eab,
                      "loot": drops.get("elite", []) + [{"item": g, "chance": 0.15, "min": 1, "max": 1} for g in greens if g.endswith("_15")]})
    spawns.append({"creature": mob_id("elite"), "position": epos, "count": 1, "radius": 0, "respawnSeconds": 300, "wanderRadius": 0})

    bkey, bname, btitle, bmodel, blvl, bpos, bab = spec["boss"]
    creatures.append({"id": mob_id("boss"), "name": bname, "title": btitle, "minLevel": blvl, "maxLevel": blvl, "family": "Humanoid", "faction": "Hostile",
                      "model": bmodel, "elite": True, "healthMultiplier": 2.5, "damageMultiplier": 1.1, "scale": 1.4, "aggroRadius": 18,
                      "moneyMin": 1500, "moneyMax": 3000, "abilities": bab,
                      "loot": drops.get("boss", []) + [{"item": g, "chance": 0.25, "min": 1, "max": 1} for g in greens if g.endswith("_19")]})
    spawns.append({"creature": mob_id("boss"), "position": bpos, "count": 1, "radius": 0, "respawnSeconds": 600, "wanderRadius": 0})

    # ---- Objekte
    zone_objects = []
    for key, (name, model, where, position, count, radius) in spec["objects"].items():
        oid = f"obj_{k}_{key}"
        quest_for = next(qid(q[0]) for q in spec["quests"] if any((o[0] == "I" and o[1] == key) or (o[0] == "C" and ("obj:" + key) in [s for s, _ in spec["items"][o[1]][2]]) for o in q[6]))
        o = {"id": oid, "name": name, "model": model, "quest": quest_for, "loot": drops.get("obj:" + key, []), "interactTime": 2.0}
        objects.append(o)
        zone_objects.append({"object": oid, "position": position, "count": count, "radius": radius, "respawnSeconds": 30})
    home = load(spec["home_zone"])[0]
    hw = spec["home_way"]
    arrival = P(center["strasse" if k == "wm" else "pfad"][0] - 20, center["strasse" if k == "wm" else "pfad"][2])
    objects.append({"id": f"obj_weg_nach_{k}", "name": f"Weg nach {spec['name']}", "model": "signpost", "teleportZone": zid,
                    "teleportPosition": arrival, "requiredFaction": fac, "interactTime": 0.5})
    objects.append({"id": f"obj_weg_{k}_heim", "name": f"Weg nach {spec['home_name']}", "model": "signpost", "teleportZone": home["id"],
                    "teleportPosition": P(hw[0] - 20, hw[2]), "requiredFaction": fac, "interactTime": 0.5})
    objects.append({"id": f"obj_crypt_entrance_{k}", "name": "Eingang der Wurzelkrypta", "model": "portal_roots", "teleportZone": "dungeon_wurzelkrypta",
                    "teleportPosition": P(50, 50), "interactTime": 0.5})
    zone_objects.append({"object": f"obj_weg_{k}_heim", "position": P(arrival[0] - 30, arrival[2]), "count": 1, "radius": 0, "respawnSeconds": 0})
    zone_objects.append({"object": f"obj_crypt_entrance_{k}", "position": spec["crypt_entrance"], "count": 1, "radius": 0, "respawnSeconds": 45})
    hubc = center[hub]
    zone_objects.append({"object": "obj_mailbox", "position": P(hubc[0] - 6, hubc[2] - 8), "count": 1, "radius": 0, "respawnSeconds": 1})
    zone_objects.sort(key=lambda o: (o["object"], tuple(o["position"])))

    # ---- Flugpunkte
    flight_pos = spec["npcs"]["flight"][3]
    node = f"tn_{k}_{hub}"
    nodes.append({"id": node, "name": spec["areas"][0][1], "zone": zid, "position": flight_pos, "faction": fac})
    for other, cost, secs in ((spec["home_node"], 150, 35), (spec["capital_node"], 300, 45)):
        paths.append({"from": node, "to": other, "cost": cost, "durationSeconds": secs})
        paths.append({"from": other, "to": node, "cost": cost, "durationSeconds": secs})

    # ---- Quests
    slot_i = 0
    for key, title, level, giver, turnin, requires, objs, kind, text, completion, opt in spec["quests"]:
        objectives, otexts = [], []
        for o in objs:
            t = o[0]
            if t == "K":
                tid = mob_id(o[1])
                objectives.append({"type": "Kill", "target": tid, "count": o[2], "description": o[3]})
                otexts.append(f"{o[2]}× {o[3].replace(' getötet', '').replace(' vernichtet', '').replace(' besiegt', '').replace(' erlöst', '')}")
            elif t == "C":
                objectives.append({"type": "Collect", "target": qitem(o[1]), "count": o[2], "description": o[3]})
                otexts.append(f"{o[2]}× {o[3]}")
            elif t == "I":
                objectives.append({"type": "Interact", "target": f"obj_{k}_{o[1]}", "count": o[2], "description": o[3]})
                otexts.append(o[3])
            elif t == "E":
                objectives.append({"type": "Explore", "target": area[o[1]], "count": 1, "description": o[2]})
                otexts.append(o[2])
            elif t in ("T", "D"):
                objectives.append({"type": "Talk" if t == "T" else "Deliver", "target": npc_id(o[1]), "count": 1, "description": o[2]})
                otexts.append(o[2])
        category = opt.get("category", "Dungeon" if kind == "dungeon" else "Main" if giver in ("home", "main") or turnin == "main" else "Side")
        q = {"id": qid(key), "title": title, "category": category, "zone": zid, "level": level, "minLevel": max(1, level - 2), "faction": fac, "class": "None",
             "giver": npc_id(giver), "turnIn": npc_id(turnin), "requires": [qid(r) for r in requires], "text": text,
             "objectivesText": "Ziele: " + ", ".join(otexts) + ".", "progressText": "Habt Ihr es schon erledigt?", "completionText": completion,
             "objectives": objectives, "rewardXp": quest_xp(level, kind), "rewardMoney": int(level * level * (2.5 if kind != "talk" else 0.8)),
             "rewardItems": [], "rewardChoices": [], "providedItems": [{"item": qitem(opt["provided"]), "count": 1}] if "provided" in opt else [],
             "rewardReputation": [{"faction": rep, "amount": opt.get("rep", 75 if kind == "talk" else 250)}],
             "suggestedPlayers": opt.get("players", 1), "exclusiveWith": []}
        if kind == "dungeon":
            q["rewardReputation"].append({"faction": "rep_hueter_der_wurzeln", "amount": 250})
        if "suffix" in opt:
            q_quality = opt.get("quality", "Uncommon")
            prefix = f"item_{k}_{key}"
            slot = opt.get("slot", SLOTS[slot_i % len(SLOTS)])
            if slot == "Back":
                ilvl = level + 2
                for arch, stats in (("str", ("Strength", "Stamina")), ("agi", ("Agility", "Stamina")), ("int", ("Intellect", "Spirit"))):
                    armor(f"{prefix}_{arch}", f"Umhang {opt['suffix']}", "Back", "Cloth", ilvl, level - 2, q_quality, stat_split(ilvl, q_quality, stats))
                q["rewardChoices"] = [{"item": f"{prefix}_{a}", "count": 1} for a in ("str", "agi", "int")]
            else:
                q["rewardChoices"] = reward_set(prefix, opt["suffix"], level, slot, q_quality, opt.get("weapon"))
            slot_i += 1
        quests.append(q)

    zone = {"id": zid, "name": spec["name"], "continent": spec["continent"], "minLevel": 12, "maxLevel": 20, "faction": fac, "biome": spec["biome"],
            "description": spec["description"], "music": spec["music"], "ambience": spec["ambience"], "weather": spec["weather"], "size": 1024,
            "playable": True, "ambientBots": 4,
            "areas": [dict({"id": area[a[0]], "name": a[1], "center": a[2], "radius": a[3], "discoveryXp": 60}, **a[4]) for a in spec["areas"]],
            "graveyards": spec["graveyards"], "spawns": spawns, "npcs": npc_spawns, "objects": zone_objects, "mapRect": spec["mapRect"]}
    return zone


def main():
    itemlib.items.clear()
    creatures, objects, nodes, paths = [], [], [], []
    for spec in ZONES:
        quests = []
        zone = build_zone(spec, creatures, objects, quests, nodes, paths)
        name = "westmark" if spec["key"] == "wm" else "rotsand"
        save(f"zones/{name}.json", [zone])
        save(f"quests/{name}.json", quests)

        # Weg aus der Startregion
        hl = load(spec["home_zone"])
        hz = hl[0]
        oid = f"obj_weg_nach_{spec['key']}"
        hz["objects"] = [o for o in hz["objects"] if o["object"] != oid]
        hz["objects"].append({"object": oid, "position": spec["home_way"], "count": 1, "radius": 0, "respawnSeconds": 0})
        # Feste Reihenfolge wie in gen_world_d.py/gen_professions.py – unabhängig von der Aufrufreihenfolge der Generatoren.
        hz["objects"].sort(key=lambda o: (o["object"], tuple(o["position"])))
        save(spec["home_zone"], hl)

    save("creatures/stufe12_20.json", creatures)
    save("objects/stufe12_20.json", objects)
    save("items/stufe12_20.json", itemlib.items)
    save("travel/nodes/stufe12_20.json", nodes)
    save("travel/paths/stufe12_20.json", paths)

    ids = {z["id"] for z in ZONES}
    save("zones/weltkarte.json", [z for z in load("zones/weltkarte.json") if z["id"] not in ids])

    # Ruf: feste Position hinter "Hüter der Wurzeln" (gen_world_d.py hängt seine Einträge ans Ende).
    new = [{"id": z["rep"][0], "name": z["rep"][1], "faction": z["faction"], "description": z["rep"][2]} for z in ZONES]
    reps = [r for r in load("reputations.json") if r["id"] not in {n["id"] for n in new}]
    at = next(i for i, r in enumerate(reps) if r["id"] == "rep_hueter_der_wurzeln") + 1
    save("reputations.json", reps[:at] + new + reps[at:])


if __name__ == "__main__":
    main()
