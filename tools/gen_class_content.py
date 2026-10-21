#!/usr/bin/env python3
"""
Content-Werkzeug: erzeugt Fähigkeiten, Auren, Talentbäume und KI-Kampfprofile aller Klassen.

Warum ein Generator? Eine Fähigkeit hat bis zu 12 Ränge (Stufe 1–60). Die Werte jedes Rangs
folgen einer Skalierungskurve. Designer pflegen hier die kompakte Beschreibung (Basiswerte,
Rangstufen, Wirkung), das Skript schreibt die vollständigen JSON-Dateien nach content/.

Aufruf:  python3 tools/gen_class_content.py
Danach:  dotnet test  (prüft u. a., dass alle Querverweise gültig sind)
"""
import json, os, math

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "content")

def dmg(level, k):
    """Basisschaden/-heilung eines Rangs auf Stufe `level` mit Stärkefaktor k."""
    return k * (8 + 2.2 * level + 0.04 * level * level)

def cost(level, c):
    return int(round(c * (10 + 2.5 * level + 0.03 * level * level)))

def train_cost(level):
    return int(10 * level * level) if level > 1 else 0

def spread(v, pct=0.15):
    return [max(1, int(round(v * (1 - pct)))), max(1, int(round(v * (1 + pct))))]

abilities = {}   # klasse -> liste
auras = {}       # klasse -> liste
talents = []
profiles = []

def A(cls, **kw):
    abilities.setdefault(cls, []).append(kw)
    return kw

def AU(cls, **kw):
    auras.setdefault(cls, []).append(kw)
    return kw

def ranked(cls, ability, levels, effects_k, cost_c=None, fixed_cost=None, aura_fn=None):
    """
    Erzeugt Ränge. effects_k: je Effekt ein Faktor k (oder None = Effekt hat keine Zahl).
    aura_fn(rank, level) -> Aura-Dict für rangabhängige Auren (DoTs, Buffs).
    """
    ranks = []
    for i, L in enumerate(levels):
        r = {"level": L, "trainingCost": train_cost(L)}
        if cost_c is not None:
            r["cost"] = cost(L, cost_c)
        elif fixed_cost is not None:
            r["cost"] = fixed_cost
        vals = []
        for k in effects_k:
            vals.append(None if k is None else spread(dmg(L, k)))
        r["values"] = vals
        if aura_fn:
            au = aura_fn(i + 1, L)
            AU(cls, **au)
            r["aura"] = au["id"]
        ranks.append(r)
    ability["ranks"] = ranks
    # Erste Rangwerte auch in die Effekte schreiben, damit die Vorlage für sich lesbar ist.
    for e, k in zip(ability["effects"], effects_k):
        if k is not None:
            e["min"], e["max"] = spread(dmg(levels[0], k))
    if aura_fn:
        for e in ability["effects"]:
            if e["type"] == "ApplyAura":
                e["aura"] = ranks[0]["aura"]
    return ability

def dot(cls, base_id, name, school, k_total, duration, tick, desc, level, debuff=True, extra=None):
    ticks = duration / tick
    per = dmg(level, k_total) / ticks
    au = {"id": f"{base_id}_r{level}", "name": name, "description": desc, "isDebuff": debuff, "duration": duration,
          "school": school, "periodic": "Damage", "tickInterval": tick,
          "tickMin": max(1, int(per * 0.95)), "tickMax": max(1, int(per * 1.05)), "tickCoefficient": 0.1 * tick / 3}
    if extra: au.update(extra)
    return au

def hot(cls, base_id, name, k_total, duration, tick, desc, level):
    per = dmg(level, k_total) / (duration / tick)
    return {"id": f"{base_id}_r{level}", "name": name, "description": desc, "isDebuff": False, "duration": duration,
            "school": "Nature" if cls in ("druid", "shaman") else "Holy", "periodic": "Heal", "tickInterval": tick,
            "tickMin": max(1, int(per * 0.95)), "tickMax": max(1, int(per * 1.05)), "tickCoefficient": 0.12}

def buff(base_id, name, desc, duration, mods_fn, level, **extra):
    au = {"id": f"{base_id}_r{level}", "name": name, "description": desc, "isDebuff": False, "duration": duration,
          "modifiers": mods_fn(level)}
    au.update(extra)
    return au

def stat_mod(stat, v): return {"type": "StatFlat", "stat": stat, "value": int(v)}

def eff(t, **kw):
    d = {"type": t}
    d.update(kw)
    return d

LV_DIRECT = [1, 6, 12, 18, 24, 30, 36, 42, 48, 54, 60]
LV_MID = [4, 10, 18, 26, 34, 42, 50, 58]
LV_LATE = [10, 20, 30, 40, 50, 60]

# =====================================================================================
# KRIEGER (Wut)
# =====================================================================================
c = "warrior"; C = "Warrior"
AU(c, id="warrior_battle_stance", name="Kampfhaltung", description="Ausgewogene Haltung für den Angriff.", permanent=True, form="BattleStance", dispellable=False, modifiers=[])
AU(c, id="warrior_defensive_stance", name="Verteidigungshaltung", description="10 % weniger erlittener Schaden, 10 % weniger verursachter Schaden, 30 % mehr Bedrohung.",
   permanent=True, form="DefensiveStance", dispellable=False,
   modifiers=[{"type": "DamageTakenPercent", "value": -10}, {"type": "DamageDonePercent", "value": -10}, {"type": "ThreatPercent", "value": 30}])
AU(c, id="warrior_berserker_stance", name="Berserkerhaltung", description="3 % mehr kritische Treffer, aber 10 % mehr erlittener Schaden.",
   permanent=True, form="BerserkerStance", dispellable=False,
   modifiers=[{"type": "CritChance", "value": 3}, {"type": "DamageTakenPercent", "value": 10}, {"type": "ThreatPercent", "value": -20}])
AU(c, id="warrior_charge_stun", name="Sturmangriffsbetäubung", description="Betäubt.", isDebuff=True, duration=1.0, stun=True, dispellable=False)
AU(c, id="warrior_thunderclap_slow", name="Donnerknall", description="Angriffstempo um 10 % verringert.", isDebuff=True, duration=10, modifiers=[{"type": "AttackSpeedPercent", "value": -10}])
AU(c, id="warrior_sunder", name="Rüstung zerreißen", description="Rüstung pro Stapel verringert.", isDebuff=True, duration=30, maxStacks=5, dispellable=False,
   modifiers=[{"type": "ArmorFlat", "value": -90}])
AU(c, id="warrior_shield_block", name="Schildblock", description="Blockchance um 75 % erhöht.", duration=5, modifiers=[{"type": "BlockChance", "value": 75}])
AU(c, id="warrior_demo_shout", name="Demoralisierender Ruf", description="Angriffskraft verringert.", isDebuff=True, duration=30, modifiers=[{"type": "AttackPowerFlat", "value": -40}])
AU(c, id="warrior_last_stand", name="Letztes Gefecht", description="Maximales Leben um 30 % erhöht.", duration=20, modifiers=[{"type": "MaxHealthPercent", "value": 30}])
AU(c, id="warrior_mortal_wound", name="Tödliche Wunde", description="Erhaltene Heilung um 50 % verringert.", isDebuff=True, duration=10, modifiers=[{"type": "HealingTakenPercent", "value": -50}])
AU(c, id="warrior_enrage", name="Raserei", description="Verursachter Schaden um 15 % erhöht.", duration=12, modifiers=[{"type": "DamageDonePercent", "value": 15}])

A(c, id="warrior_battle_stance", name="Kampfhaltung", description="Wechselt in die Kampfhaltung.", **{"class": C}, level=1, powerType="Rage", cost=0,
  target="Self", triggersGcd=False, cooldown=1, notTrainable=True, icon="stance_battle", animation="stance",
  effects=[eff("ApplyAura", aura="warrior_battle_stance")])
ranked(c, A(c, id="warrior_heroic_strike", name="Heldenhafter Stoß", description="Ein mächtiger Hieb, der Waffenschaden plus Zusatzschaden verursacht.", **{"class": C},
  powerType="Rage", cost=15, target="Enemy", maxRange=5, icon="heroic_strike", animation="attack_heavy",
  effects=[eff("WeaponDamage", weaponPercent=1.0, threat=20)]), LV_DIRECT, [0.55], fixed_cost=15)
ranked(c, A(c, id="warrior_battle_shout", name="Schlachtruf", description="Erhöht die Angriffskraft aller Gruppenmitglieder für 2 Minuten.", **{"class": C},
  powerType="Rage", cost=10, target="Party", maxRange=30, icon="battle_shout", animation="shout",
  effects=[eff("ApplyAura", threat=10)]), [1, 12, 22, 32, 42, 52], [None], fixed_cost=10,
  aura_fn=lambda r, L: buff("warrior_battle_shout", "Schlachtruf", "Angriffskraft erhöht.", 120, lambda L: [{"type": "AttackPowerFlat", "value": int(15 + L * 3)}], L))
A(c, id="warrior_charge", name="Sturmangriff", description="Stürmt auf den Gegner zu, betäubt ihn kurz und erzeugt 9 Wut. Nur außerhalb des Kampfes.", **{"class": C},
  level=4, trainingCost=train_cost(4), powerType="Rage", cost=0, cooldown=15, minRange=8, maxRange=25, target="Enemy", outOfCombatOnly=True,
  requiredForms=["BattleStance"], triggersGcd=False, icon="charge", animation="charge",
  effects=[eff("Charge"), eff("GainPower", min=9, max=9, onSelf=True), eff("ApplyAura", aura="warrior_charge_stun")])
ranked(c, A(c, id="warrior_rend", name="Verwunden", description="Lässt das Ziel 15 Sekunden lang bluten.", **{"class": C}, powerType="Rage", cost=10,
  target="Enemy", maxRange=5, icon="rend", animation="attack", effects=[eff("ApplyAura")]), LV_MID, [None], fixed_cost=10,
  aura_fn=lambda r, L: dot(c, "warrior_rend", "Verwunden", "Physical", 1.0, 15, 3, "Blutet.", L))
ranked(c, A(c, id="warrior_thunder_clap", name="Donnerknall", description="Erschüttert alle Gegner in der Nähe: Schaden und verlangsamtes Angriffstempo.", **{"class": C},
  level=6, powerType="Rage", cost=20, cooldown=4, target="AreaAroundSelf", radius=8, icon="thunder_clap", animation="stomp",
  effects=[eff("SchoolDamage", school="Physical", threatMultiplier=1.75), eff("ApplyAura", aura="warrior_thunderclap_slow")]),
  [6, 18, 28, 38, 48, 58], [0.45, None], fixed_cost=20)
A(c, id="warrior_defensive_stance", name="Verteidigungshaltung", description="Wechselt in die Verteidigungshaltung für den Einsatz als Tank.", **{"class": C},
  level=10, trainingCost=train_cost(10), powerType="Rage", cost=0, target="Self", triggersGcd=False, cooldown=1, icon="stance_defensive", animation="stance",
  effects=[eff("ApplyAura", aura="warrior_defensive_stance")])
A(c, id="warrior_taunt", name="Spott", description="Zwingt das Ziel, Euch anzugreifen.", **{"class": C}, level=10, trainingCost=train_cost(10),
  powerType="Rage", cost=0, cooldown=10, maxRange=10, target="Enemy", triggersGcd=False, requiredForms=["DefensiveStance"], icon="taunt", animation="taunt",
  effects=[eff("Taunt")])
A(c, id="warrior_sunder_armor", name="Rüstung zerreißen", description="Verringert die Rüstung des Ziels (bis zu 5-mal stapelbar) und erzeugt viel Bedrohung.", **{"class": C},
  level=10, trainingCost=train_cost(10), powerType="Rage", cost=15, target="Enemy", maxRange=5, icon="sunder", animation="attack",
  effects=[eff("ApplyAura", aura="warrior_sunder", threat=100)])
A(c, id="warrior_demoralizing_shout", name="Demoralisierender Ruf", description="Verringert die Angriffskraft aller Gegner in der Nähe.", **{"class": C},
  level=14, trainingCost=train_cost(14), powerType="Rage", cost=10, target="AreaAroundSelf", radius=10, icon="demo_shout", animation="shout",
  effects=[eff("ApplyAura", aura="warrior_demo_shout", threat=40)])
A(c, id="warrior_shield_block", name="Schildblock", description="Erhöht die Blockchance 5 Sekunden lang stark.", **{"class": C},
  level=16, trainingCost=train_cost(16), powerType="Rage", cost=10, cooldown=5, target="Self", requiresShield=True, triggersGcd=False,
  requiredForms=["DefensiveStance"], icon="shield_block", animation="block", effects=[eff("ApplyAura", aura="warrior_shield_block")])
ranked(c, A(c, id="warrior_execute", name="Hinrichten", description="Versucht, einen geschwächten Gegner (unter 20 % Leben) zu erledigen.", **{"class": C},
  powerType="Rage", cost=15, target="Enemy", maxRange=5, targetHealthBelow=0.2, requiredForms=["BattleStance", "BerserkerStance"], icon="execute", animation="attack_heavy",
  effects=[eff("SchoolDamage", school="Physical")]), [24, 32, 40, 48, 56], [1.6], fixed_cost=15)
A(c, id="warrior_berserker_stance", name="Berserkerhaltung", description="Aggressive Haltung mit mehr kritischen Treffern.", **{"class": C},
  level=30, trainingCost=train_cost(30), powerType="Rage", cost=0, target="Self", triggersGcd=False, cooldown=1, icon="stance_berserker", animation="stance",
  effects=[eff("ApplyAura", aura="warrior_berserker_stance")])
ranked(c, A(c, id="warrior_mortal_strike", name="Tödlicher Stoß", description="Starker Hieb, der die erhaltene Heilung des Ziels halbiert. (Talent)", **{"class": C},
  powerType="Rage", cost=30, cooldown=6, target="Enemy", maxRange=5, notTrainable=True, icon="mortal_strike", animation="attack_heavy",
  effects=[eff("WeaponDamage", weaponPercent=1.0), eff("ApplyAura", aura="warrior_mortal_wound")]), [30, 40, 50, 60], [0.9, None], fixed_cost=30)
ranked(c, A(c, id="warrior_shield_slam", name="Schildschlag", description="Schlägt mit dem Schild zu. Erzeugt sehr viel Bedrohung. (Talent)", **{"class": C},
  powerType="Rage", cost=20, cooldown=6, target="Enemy", maxRange=5, requiresShield=True, notTrainable=True, icon="shield_slam", animation="shield_bash",
  effects=[eff("SchoolDamage", school="Physical", threat=150, threatMultiplier=1.5)]), [30, 40, 50, 60], [1.2], fixed_cost=20)
A(c, id="warrior_last_stand", name="Letztes Gefecht", description="Erhöht das maximale Leben 20 Sekunden lang um 30 %. (Talent)", **{"class": C},
  level=20, powerType="Rage", cost=0, cooldown=300, target="Self", triggersGcd=False, notTrainable=True, icon="last_stand", animation="roar",
  effects=[eff("ApplyAura", aura="warrior_last_stand")])
A(c, id="warrior_bloodrage", name="Blutrausch", description="Erzeugt sofort 10 Wut.", **{"class": C}, level=10, trainingCost=train_cost(10),
  powerType="Rage", cost=0, cooldown=60, target="Self", triggersGcd=False, icon="bloodrage", animation="roar",
  effects=[eff("GainPower", min=10, max=10, onSelf=True)])

# =====================================================================================
# PALADIN (Mana)
# =====================================================================================
c = "paladin"; C = "Paladin"
ranked(c, A(c, id="paladin_seal_of_righteousness", name="Siegel der Rechtschaffenheit", description="Jeder Nahkampftreffer verursacht 30 Sekunden lang zusätzlichen Heiligschaden.", **{"class": C},
  powerType="Mana", target="Self", icon="seal_righteousness", animation="cast_self", effects=[eff("ApplyAura")]), [1, 10, 18, 26, 34, 42, 50, 58], [None], cost_c=0.7,
  aura_fn=lambda r, L: {"id": f"paladin_seal_r{L}", "name": "Siegel der Rechtschaffenheit", "description": "Zusätzlicher Heiligschaden pro Treffer.",
                        "duration": 30, "procMin": int(dmg(L, 0.18)), "procMax": int(dmg(L, 0.22)), "procSchool": "Holy", "procCoefficient": 0.1})
ranked(c, A(c, id="paladin_judgement", name="Richturteil", description="Entfesselt heiliges Licht auf den Gegner.", **{"class": C}, level=4,
  powerType="Mana", cooldown=8, target="Enemy", maxRange=10, school="Holy", icon="judgement", animation="cast_point",
  effects=[eff("SchoolDamage", school="Holy", coefficient=0.43)]), [4] + LV_MID[1:], [0.9], cost_c=0.8)
ranked(c, A(c, id="paladin_holy_light", name="Heiliges Licht", description="Heilt ein freundliches Ziel.", **{"class": C}, powerType="Mana", castTime=2.5,
  target="FriendlyOrSelf", maxRange=40, school="Holy", icon="holy_light", animation="cast_heal",
  effects=[eff("Heal", coefficient=0.71)]), LV_DIRECT, [1.6], cost_c=1.3)
ranked(c, A(c, id="paladin_devotion_aura", name="Aura der Hingabe", description="Erhöht die Rüstung aller Gruppenmitglieder in der Nähe.", **{"class": C},
  powerType="Mana", cost=0, target="Self", triggersGcd=False, icon="devotion_aura", animation="cast_self", effects=[eff("ApplyAura")]), [1, 10, 20, 30, 40, 50, 60], [None], fixed_cost=0,
  aura_fn=lambda r, L: {"id": f"paladin_devotion_r{L}", "name": "Aura der Hingabe", "description": "Rüstung erhöht.", "permanent": True, "partyAura": True,
                        "dispellable": False, "modifiers": [{"type": "ArmorFlat", "value": int(25 + L * 12)}]})
ranked(c, A(c, id="paladin_blessing_of_might", name="Segen der Macht", description="Erhöht die Angriffskraft des Ziels für 5 Minuten.", **{"class": C}, level=4,
  powerType="Mana", target="FriendlyOrSelf", maxRange=30, icon="blessing_might", animation="cast_point", effects=[eff("ApplyAura")]), [4, 12, 22, 32, 42, 52], [None], cost_c=0.4,
  aura_fn=lambda r, L: buff("paladin_bom", "Segen der Macht", "Angriffskraft erhöht.", 300, lambda L: [{"type": "AttackPowerFlat", "value": int(20 + L * 3)}], L))
AU(c, id="paladin_divine_protection", name="Göttlicher Schutz", description="Erlittener Schaden um 50 % verringert.", duration=8, modifiers=[{"type": "DamageTakenPercent", "value": -50}])
A(c, id="paladin_divine_protection", name="Göttlicher Schutz", description="Verringert 8 Sekunden lang jeden erlittenen Schaden um 50 %.", **{"class": C},
  level=6, trainingCost=train_cost(6), powerType="Mana", cost=30, cooldown=300, target="Self", icon="divine_protection", animation="cast_self",
  effects=[eff("ApplyAura", aura="paladin_divine_protection")])
AU(c, id="paladin_hammer_stun", name="Hammer der Gerechtigkeit", description="Betäubt.", isDebuff=True, duration=4, stun=True)
A(c, id="paladin_hammer_of_justice", name="Hammer der Gerechtigkeit", description="Betäubt das Ziel 4 Sekunden lang.", **{"class": C}, level=8,
  trainingCost=train_cost(8), powerType="Mana", cost=40, cooldown=60, maxRange=10, target="Enemy", school="Holy", icon="hammer_justice", animation="throw",
  effects=[eff("ApplyAura", aura="paladin_hammer_stun"), eff("Interrupt", duration=0)])
A(c, id="paladin_redemption", name="Erlösung", description="Belebt ein totes Gruppenmitglied mit 35 % Leben wieder.", **{"class": C}, level=12,
  trainingCost=train_cost(12), powerType="Mana", cost=120, castTime=10, maxRange=30, target="DeadFriendly", outOfCombatOnly=True, school="Holy",
  icon="redemption", animation="cast_long", effects=[eff("Resurrect", min=35, max=35)])
AU(c, id="paladin_righteous_fury", name="Zorn der Gerechtigkeit", description="Bedrohung durch Heiligzauber um 60 % erhöht.", duration=1800, dispellable=False,
   modifiers=[{"type": "ThreatPercent", "value": 60, "school": "Holy"}])
A(c, id="paladin_righteous_fury", name="Zorn der Gerechtigkeit", description="Erhöht 30 Minuten lang die Bedrohung durch Heiligschaden um 60 %.", **{"class": C},
  level=16, trainingCost=train_cost(16), powerType="Mana", cost=60, target="Self", school="Holy", icon="righteous_fury", animation="cast_self",
  effects=[eff("ApplyAura", aura="paladin_righteous_fury")])
ranked(c, A(c, id="paladin_flash_of_light", name="Lichtblitz", description="Schnelle, sparsame Heilung.", **{"class": C}, powerType="Mana", castTime=1.5,
  target="FriendlyOrSelf", maxRange=40, school="Holy", icon="flash_of_light", animation="cast_heal",
  effects=[eff("Heal", coefficient=0.43)]), [20, 26, 34, 42, 50, 58], [0.7], cost_c=0.6)
ranked(c, A(c, id="paladin_consecration", name="Weihe", description="Heiligt den Boden: Heiligschaden an allen Gegnern in der Nähe.", **{"class": C},
  powerType="Mana", cooldown=8, target="AreaAroundSelf", radius=8, school="Holy", icon="consecration", animation="cast_ground",
  effects=[eff("SchoolDamage", school="Holy", coefficient=0.33)]), [20, 30, 40, 50, 60], [0.8], cost_c=1.4)
ranked(c, A(c, id="paladin_holy_shock", name="Heiliger Schock", description="Sofortige Heilung eines Verbündeten. (Talent)", **{"class": C}, powerType="Mana",
  cooldown=30, target="FriendlyOrSelf", maxRange=20, school="Holy", notTrainable=True, icon="holy_shock", animation="cast_point",
  effects=[eff("Heal", coefficient=0.43)]), [40, 48, 56], [1.2], cost_c=1.0)
ranked(c, A(c, id="paladin_crusader_strike", name="Kreuzfahrerstoß", description="Heiliger Waffenschlag. (Talent)", **{"class": C}, powerType="Mana",
  cooldown=6, target="Enemy", maxRange=5, notTrainable=True, icon="crusader_strike", animation="attack_heavy",
  effects=[eff("WeaponDamage", weaponPercent=1.1)]), [40, 50, 60], [0.3], cost_c=0.5)

# =====================================================================================
# JÄGER (Mana)
# =====================================================================================
c = "hunter"; C = "Hunter"
ranked(c, A(c, id="hunter_raptor_strike", name="Raptorstoß", description="Ein schneller Nahkampfhieb.", **{"class": C}, powerType="Mana",
  cooldown=6, target="Enemy", maxRange=5, icon="raptor_strike", animation="attack", effects=[eff("WeaponDamage", weaponPercent=1.0)]), LV_DIRECT, [0.35], cost_c=0.3)
ranked(c, A(c, id="hunter_arcane_shot", name="Arkaner Schuss", description="Ein sofortiger Schuss mit arkanem Schaden.", **{"class": C}, level=6,
  powerType="Mana", cooldown=6, minRange=8, maxRange=35, target="Enemy", school="Arcane", requiresRangedWeapon=True, icon="arcane_shot", animation="shoot",
  effects=[eff("SchoolDamage", school="Arcane", coefficient=0.15)]), [6, 12, 20, 28, 36, 44, 52, 60], [0.7], cost_c=0.6)
ranked(c, A(c, id="hunter_serpent_sting", name="Schlangenbiss", description="Vergiftet das Ziel 15 Sekunden lang.", **{"class": C}, level=4, powerType="Mana",
  minRange=8, maxRange=35, target="Enemy", school="Nature", requiresRangedWeapon=True, icon="serpent_sting", animation="shoot", effects=[eff("ApplyAura")]),
  [4, 10, 18, 26, 34, 42, 50, 58], [None], cost_c=0.5,
  aura_fn=lambda r, L: dot(c, "hunter_serpent_sting", "Schlangenbiss", "Nature", 0.9, 15, 3, "Erleidet Naturschaden.", L))
ranked(c, A(c, id="hunter_aspect_of_the_hawk", name="Aspekt des Falken", description="Erhöht die Distanzangriffskraft.", **{"class": C}, level=10,
  powerType="Mana", target="Self", triggersGcd=False, icon="aspect_hawk", animation="cast_self", effects=[eff("ApplyAura")]), LV_LATE, [None], cost_c=0.3,
  aura_fn=lambda r, L: {"id": f"hunter_hawk_r{L}", "name": "Aspekt des Falken", "description": "Angriffskraft erhöht.", "permanent": True, "dispellable": False,
                        "modifiers": [{"type": "AttackPowerFlat", "value": int(L * 2)}]})
AU(c, id="hunter_concussive", name="Erschütternder Schuss", description="Bewegungstempo um 50 % verringert.", isDebuff=True, duration=4, modifiers=[{"type": "MoveSpeedPercent", "value": -50}])
A(c, id="hunter_concussive_shot", name="Erschütternder Schuss", description="Verlangsamt das Ziel 4 Sekunden lang.", **{"class": C}, level=8, trainingCost=train_cost(8),
  powerType="Mana", cost=50, cooldown=12, minRange=8, maxRange=35, target="Enemy", requiresRangedWeapon=True, icon="concussive", animation="shoot",
  effects=[eff("ApplyAura", aura="hunter_concussive")])
A(c, id="hunter_call_pet", name="Begleiter rufen", description="Ruft Euren Wolfsbegleiter, der an Eurer Seite kämpft.", **{"class": C}, level=1,
  powerType="Mana", cost=20, castTime=2, target="Self", outOfCombatOnly=True, icon="call_pet", animation="cast_long", effects=[eff("SummonPet", pet="pet_wolf")])
ranked(c, A(c, id="hunter_mend_pet", name="Tier heilen", description="Heilt Euren Begleiter über 15 Sekunden.", **{"class": C}, level=12,
  powerType="Mana", target="Friendly", maxRange=40, icon="mend_pet", animation="cast_heal", effects=[eff("Heal", coefficient=0.3)]), [12, 20, 28, 36, 44, 52, 60], [1.2], cost_c=0.6)
ranked(c, A(c, id="hunter_multi_shot", name="Mehrfachschuss", description="Trifft bis zu drei Gegner um das Ziel.", **{"class": C}, level=18,
  powerType="Mana", cooldown=10, minRange=8, maxRange=35, target="AreaAroundTarget", radius=8, maxTargets=3, requiresRangedWeapon=True, icon="multi_shot", animation="shoot",
  effects=[eff("WeaponDamage", weaponPercent=1.0)]), [18, 30, 42, 54], [0.3], cost_c=0.9)
A(c, id="hunter_feign_death", name="Totstellen", description="Stellt sich tot – alle Gegner vergessen ihre Bedrohung.", **{"class": C}, level=30,
  trainingCost=train_cost(30), powerType="Mana", cost=80, cooldown=30, target="Self", triggersGcd=False, icon="feign_death", animation="die",
  effects=[eff("ReduceThreat", min=100, max=100)])
ranked(c, A(c, id="hunter_aimed_shot", name="Gezielter Schuss", description="Ein präziser, starker Schuss. (Talent)", **{"class": C}, powerType="Mana",
  castTime=3, cooldown=6, minRange=8, maxRange=35, target="Enemy", requiresRangedWeapon=True, notTrainable=True, icon="aimed_shot", animation="shoot_aim",
  effects=[eff("WeaponDamage", weaponPercent=1.0)]), [20, 28, 36, 44, 52, 60], [1.0], cost_c=1.0)

# =====================================================================================
# SCHURKE (Energie, Kombopunkte)
# =====================================================================================
c = "rogue"; C = "Rogue"
ranked(c, A(c, id="rogue_sinister_strike", name="Finsterer Stoß", description="Ein heimtückischer Stoß. Erzeugt einen Kombopunkt.", **{"class": C},
  powerType="Energy", cost=45, target="Enemy", maxRange=5, icon="sinister_strike", animation="attack",
  effects=[eff("WeaponDamage", weaponPercent=1.0), eff("ComboPoints", min=1, max=1)]), LV_DIRECT, [0.3, None], fixed_cost=45)
ranked(c, A(c, id="rogue_eviscerate", name="Ausweiden", description="Finishing-Move: Schaden pro Kombopunkt.", **{"class": C},
  powerType="Energy", cost=35, target="Enemy", maxRange=5, requiresComboPoints=True, icon="eviscerate", animation="attack_heavy",
  effects=[eff("SchoolDamage", school="Physical", perComboPoint=1)]), [1, 8, 16, 24, 32, 40, 48, 56], [0.35], fixed_cost=35)
AU(c, id="rogue_stealth", name="Verstohlenheit", description="Getarnt. Bewegungstempo verringert.", permanent=True, stealth=True, dispellable=False, modifiers=[{"type": "MoveSpeedPercent", "value": -30}])
A(c, id="rogue_stealth", name="Verstohlenheit", description="Verschwindet in den Schatten. Bricht bei Angriffen ab.", **{"class": C}, level=1,
  powerType="Energy", cost=0, cooldown=10, target="Self", outOfCombatOnly=True, triggersGcd=False, icon="stealth", animation="crouch",
  effects=[eff("ApplyAura", aura="rogue_stealth")])
ranked(c, A(c, id="rogue_backstab", name="Meucheln", description="Ein verheerender Stich mit 150 % Waffenschaden.", **{"class": C}, level=4,
  powerType="Energy", cost=60, target="Enemy", maxRange=5, icon="backstab", animation="attack_stab",
  effects=[eff("WeaponDamage", weaponPercent=1.5), eff("ComboPoints", min=1, max=1)]), [4, 12, 20, 28, 36, 44, 52, 60], [0.35, None], fixed_cost=60)
AU(c, id="rogue_gouge", name="Solarplexus", description="Außer Gefecht gesetzt. Schaden beendet den Effekt.", isDebuff=True, duration=4, stun=True, breaksOnDamage=True)
A(c, id="rogue_gouge", name="Solarplexus", description="Setzt das Ziel 4 Sekunden außer Gefecht.", **{"class": C}, level=6, trainingCost=train_cost(6),
  powerType="Energy", cost=45, cooldown=10, maxRange=5, target="Enemy", icon="gouge", animation="attack",
  effects=[eff("ApplyAura", aura="rogue_gouge"), eff("ComboPoints", min=1, max=1)])
AU(c, id="rogue_evasion", name="Entrinnen", description="Ausweichchance um 50 % erhöht.", duration=15, modifiers=[{"type": "DodgeChance", "value": 50}])
A(c, id="rogue_evasion", name="Entrinnen", description="Erhöht die Ausweichchance 15 Sekunden lang um 50 %.", **{"class": C}, level=8, trainingCost=train_cost(8),
  powerType="Energy", cost=0, cooldown=300, target="Self", triggersGcd=False, icon="evasion", animation="dodge",
  effects=[eff("ApplyAura", aura="rogue_evasion")])
A(c, id="rogue_slice_and_dice", name="Zerhäckseln", description="Finishing-Move: Erhöht das Angriffstempo um 20 %.", **{"class": C}, level=10, trainingCost=train_cost(10),
  powerType="Energy", cost=25, target="Self", requiresComboPoints=True, icon="slice_dice", animation="flourish",
  effects=[eff("ApplyAura", aura="rogue_snd")])
AU(c, id="rogue_snd", name="Zerhäckseln", description="Angriffstempo um 20 % erhöht.", duration=21, modifiers=[{"type": "AttackSpeedPercent", "value": 20}])
AU(c, id="rogue_sprint", name="Sprinten", description="Bewegungstempo um 70 % erhöht.", duration=8, modifiers=[{"type": "MoveSpeedPercent", "value": 70}])
A(c, id="rogue_sprint", name="Sprinten", description="Erhöht das Bewegungstempo 8 Sekunden lang um 70 %.", **{"class": C}, level=10, trainingCost=train_cost(10),
  powerType="Energy", cost=0, cooldown=300, target="Self", triggersGcd=False, icon="sprint", animation="run",
  effects=[eff("ApplyAura", aura="rogue_sprint")])
A(c, id="rogue_kick", name="Tritt", description="Unterbricht einen Zauber und sperrt die Schule 5 Sekunden.", **{"class": C}, level=12, trainingCost=train_cost(12),
  powerType="Energy", cost=25, cooldown=10, maxRange=5, target="Enemy", triggersGcd=False, icon="kick", animation="kick",
  effects=[eff("Interrupt", duration=5), eff("SchoolDamage", school="Physical", min=1, max=3)])
ranked(c, A(c, id="rogue_ambush", name="Hinterhalt", description="Aus der Verstohlenheit: 250 % Waffenschaden.", **{"class": C}, powerType="Energy",
  cost=60, maxRange=5, target="Enemy", requiresStealth=True, icon="ambush", animation="attack_stab",
  effects=[eff("WeaponDamage", weaponPercent=2.5), eff("ComboPoints", min=2, max=2)]), [18, 26, 34, 42, 50, 58], [0.5, None], fixed_cost=60)
ranked(c, A(c, id="rogue_hemorrhage", name="Blutsturz", description="Ein Hieb, der starke Blutungen auslöst. (Talent)", **{"class": C}, powerType="Energy",
  cost=35, maxRange=5, target="Enemy", notTrainable=True, icon="hemorrhage", animation="attack",
  effects=[eff("WeaponDamage", weaponPercent=1.1), eff("ComboPoints", min=1, max=1)]), [30, 46, 58], [0.2, None], fixed_cost=35)
AU(c, id="rogue_adrenaline", name="Adrenalinrausch", description="Energieregeneration stark erhöht.", duration=15, modifiers=[{"type": "PowerRegenPercent", "value": 100}])
A(c, id="rogue_adrenaline_rush", name="Adrenalinrausch", description="Verdoppelt die Energieregeneration 15 Sekunden lang. (Talent)", **{"class": C}, level=40,
  powerType="Energy", cost=0, cooldown=300, target="Self", triggersGcd=False, notTrainable=True, icon="adrenaline", animation="roar",
  effects=[eff("ApplyAura", aura="rogue_adrenaline")])

# =====================================================================================
# PRIESTER (Mana)
# =====================================================================================
c = "priest"; C = "Priest"
ranked(c, A(c, id="priest_smite", name="Göttliche Pein", description="Straft den Gegner mit heiligem Licht.", **{"class": C}, powerType="Mana",
  castTime=2.5, maxRange=30, target="Enemy", school="Holy", icon="smite", animation="cast",
  effects=[eff("SchoolDamage", school="Holy", coefficient=0.71)]), LV_DIRECT, [1.25], cost_c=1.2)
ranked(c, A(c, id="priest_lesser_heal", name="Geringe Heilung", description="Heilt ein Ziel um einen kleinen Betrag.", **{"class": C}, powerType="Mana",
  castTime=2.0, maxRange=40, target="FriendlyOrSelf", school="Holy", icon="lesser_heal", animation="cast_heal",
  effects=[eff("Heal", coefficient=0.6)]), [1, 4, 10], [1.3], cost_c=1.0)
ranked(c, A(c, id="priest_heal", name="Heilung", description="Heilt ein Ziel um einen mittleren Betrag.", **{"class": C}, powerType="Mana",
  castTime=3.0, maxRange=40, target="FriendlyOrSelf", school="Holy", icon="heal", animation="cast_heal",
  effects=[eff("Heal", coefficient=0.86)]), [16, 22, 28, 34], [2.0], cost_c=1.5)
ranked(c, A(c, id="priest_greater_heal", name="Große Heilung", description="Eine große, langsame Heilung.", **{"class": C}, powerType="Mana",
  castTime=3.0, maxRange=40, target="FriendlyOrSelf", school="Holy", icon="greater_heal", animation="cast_heal",
  effects=[eff("Heal", coefficient=0.86)]), [40, 46, 52, 58, 60], [2.6], cost_c=1.8)
ranked(c, A(c, id="priest_renew", name="Erneuerung", description="Heilt das Ziel 15 Sekunden lang.", **{"class": C}, level=8, powerType="Mana",
  maxRange=40, target="FriendlyOrSelf", school="Holy", icon="renew", animation="cast_point", effects=[eff("ApplyAura")]), [8, 14, 20, 26, 32, 38, 44, 50, 56], [None], cost_c=0.8,
  aura_fn=lambda r, L: hot(c, "priest_renew", "Erneuerung", 1.5, 15, 3, "Wird geheilt.", L))
AU(c, id="priest_weakened_soul", name="Geschwächte Seele", description="Kann 15 Sekunden lang nicht erneut geschützt werden.", isDebuff=True, duration=15, dispellable=False)
ranked(c, A(c, id="priest_power_word_shield", name="Machtwort: Schild", description="Ein Schild, der Schaden absorbiert.", **{"class": C}, level=6, powerType="Mana",
  cooldown=4, maxRange=40, target="FriendlyOrSelf", school="Holy", icon="pw_shield", animation="cast_point",
  effects=[eff("ApplyAura"), eff("ApplyAura", aura="priest_weakened_soul")]), [6, 12, 18, 24, 30, 36, 42, 48, 54, 60], [None, None], cost_c=1.0,
  aura_fn=lambda r, L: {"id": f"priest_pws_r{L}", "name": "Machtwort: Schild", "description": "Absorbiert Schaden.", "duration": 30,
                        "absorb": int(dmg(L, 1.4)), "absorbCoefficient": 0.1})
ranked(c, A(c, id="priest_power_word_fortitude", name="Machtwort: Seelenstärke", description="Erhöht die Ausdauer des Ziels für 30 Minuten.", **{"class": C},
  powerType="Mana", maxRange=30, target="FriendlyOrSelf", school="Holy", icon="pw_fortitude", animation="cast_point", effects=[eff("ApplyAura")]),
  [1, 12, 24, 36, 48, 60], [None], cost_c=0.6,
  aura_fn=lambda r, L: buff("priest_fort", "Machtwort: Seelenstärke", "Ausdauer erhöht.", 1800, lambda L: [stat_mod("Stamina", 3 + L * 0.9)], L))
ranked(c, A(c, id="priest_shadow_word_pain", name="Schattenwort: Schmerz", description="Ein Fluch aus Schatten, der 18 Sekunden lang Schaden verursacht.", **{"class": C},
  level=4, powerType="Mana", maxRange=30, target="Enemy", school="Shadow", icon="sw_pain", animation="cast_point", effects=[eff("ApplyAura")]),
  [4, 10, 18, 26, 34, 42, 50, 58], [None], cost_c=0.7,
  aura_fn=lambda r, L: dot(c, "priest_swp", "Schattenwort: Schmerz", "Shadow", 1.2, 18, 3, "Erleidet Schattenschaden.", L))
ranked(c, A(c, id="priest_mind_blast", name="Gedankenschlag", description="Ein Stoß reiner Gedankenkraft.", **{"class": C}, powerType="Mana",
  castTime=1.5, cooldown=8, maxRange=30, target="Enemy", school="Shadow", icon="mind_blast", animation="cast",
  effects=[eff("SchoolDamage", school="Shadow", coefficient=0.43)]), [10, 16, 22, 28, 34, 40, 46, 52, 58], [1.0], cost_c=1.0)
A(c, id="priest_resurrection", name="Auferstehung", description="Belebt ein totes Gruppenmitglied mit 35 % Leben wieder.", **{"class": C}, level=10,
  trainingCost=train_cost(10), powerType="Mana", cost=100, castTime=10, maxRange=30, target="DeadFriendly", outOfCombatOnly=True, school="Holy",
  icon="resurrection", animation="cast_long", effects=[eff("Resurrect", min=35, max=35)])
A(c, id="priest_fade", name="Verblassen", description="Verringert vorübergehend die Bedrohung bei allen Gegnern.", **{"class": C}, level=8, trainingCost=train_cost(8),
  powerType="Mana", cost=40, cooldown=30, target="Self", triggersGcd=False, icon="fade", animation="cast_self", effects=[eff("ReduceThreat", min=50, max=50)])
AU(c, id="priest_scream", name="Psychischer Schrei", description="Flieht voller Furcht.", isDebuff=True, duration=8, fear=True, breaksOnDamage=True, school="Shadow")
A(c, id="priest_psychic_scream", name="Psychischer Schrei", description="Bis zu 2 Gegner in der Nähe fliehen 8 Sekunden lang.", **{"class": C}, level=14,
  trainingCost=train_cost(14), powerType="Mana", cost=80, cooldown=30, target="AreaAroundSelf", radius=8, maxTargets=2, school="Shadow", icon="psychic_scream", animation="shout",
  effects=[eff("ApplyAura", aura="priest_scream")])
ranked(c, A(c, id="priest_mind_flay", name="Gedankenschinden", description="Schattenschaden und Verlangsamung. (Talent)", **{"class": C}, powerType="Mana",
  castTime=3.0, maxRange=20, target="Enemy", school="Shadow", notTrainable=True, icon="mind_flay", animation="cast_channel",
  effects=[eff("SchoolDamage", school="Shadow", coefficient=0.45), eff("ApplyAura", aura="hunter_concussive")]), [20, 28, 36, 44, 52, 60], [1.1, None], cost_c=0.9)
ranked(c, A(c, id="priest_holy_nova", name="Heilige Nova", description="Heilt Verbündete und schadet Gegnern in der Nähe. (Talent)", **{"class": C}, powerType="Mana",
  target="AreaAroundSelf", radius=10, school="Holy", notTrainable=True, icon="holy_nova", animation="cast_ground",
  effects=[eff("SchoolDamage", school="Holy", coefficient=0.1)]), [20, 30, 40, 50, 60], [0.35], cost_c=1.4)

# =====================================================================================
# SCHAMANE (Mana)
# =====================================================================================
c = "shaman"; C = "Shaman"
ranked(c, A(c, id="shaman_lightning_bolt", name="Blitzschlag", description="Schleudert einen Blitz auf den Gegner.", **{"class": C}, powerType="Mana",
  castTime=2.5, maxRange=30, target="Enemy", school="Nature", icon="lightning_bolt", animation="cast",
  effects=[eff("SchoolDamage", school="Nature", coefficient=0.71)]), LV_DIRECT, [1.3], cost_c=1.1)
ranked(c, A(c, id="shaman_rockbiter_weapon", name="Waffe des Felsbeißers", description="Erdkraft in der Waffe: Zusatzschaden pro Treffer für 5 Minuten.", **{"class": C},
  powerType="Mana", target="Self", icon="rockbiter", animation="cast_self", effects=[eff("ApplyAura")]), [1, 8, 16, 24, 34, 44, 54], [None], cost_c=0.5,
  aura_fn=lambda r, L: {"id": f"shaman_rockbiter_r{L}", "name": "Waffe des Felsbeißers", "description": "Zusätzlicher Schaden pro Treffer.", "duration": 300, "dispellable": False,
                        "procMin": int(dmg(L, 0.15)), "procMax": int(dmg(L, 0.2)), "procSchool": "Nature", "procCoefficient": 0.05})
ranked(c, A(c, id="shaman_earth_shock", name="Erdschock", description="Sofortiger Naturschaden, unterbricht Zauber.", **{"class": C}, level=4, powerType="Mana",
  cooldown=6, maxRange=20, target="Enemy", school="Nature", icon="earth_shock", animation="cast_point",
  effects=[eff("SchoolDamage", school="Nature", coefficient=0.39), eff("Interrupt", duration=2)]), [4, 8, 14, 24, 36, 48, 60], [0.9, None], cost_c=0.8)
ranked(c, A(c, id="shaman_healing_wave", name="Welle der Heilung", description="Heilt ein Ziel.", **{"class": C}, powerType="Mana", castTime=3.0,
  maxRange=40, target="FriendlyOrSelf", school="Nature", icon="healing_wave", animation="cast_heal",
  effects=[eff("Heal", coefficient=0.86)]), LV_DIRECT, [1.9], cost_c=1.4)
ranked(c, A(c, id="shaman_flame_shock", name="Flammenschock", description="Feuerschaden und zusätzlicher Brand.", **{"class": C}, powerType="Mana",
  cooldown=6, maxRange=20, target="Enemy", school="Fire", icon="flame_shock", animation="cast_point",
  effects=[eff("SchoolDamage", school="Fire", coefficient=0.15), eff("ApplyAura")]), [10, 18, 28, 40, 52], [0.4, None], cost_c=0.8,
  aura_fn=lambda r, L: dot(c, "shaman_flame_shock", "Flammenschock", "Fire", 0.6, 12, 3, "Brennt.", L))
ranked(c, A(c, id="shaman_strength_of_earth", name="Totem der Erdstärke", description="Erhöht die Stärke der Gruppe für 2 Minuten.", **{"class": C}, level=10,
  powerType="Mana", target="Party", maxRange=30, icon="strength_earth", animation="cast_ground", effects=[eff("ApplyAura")]), [10, 24, 38, 52], [None], cost_c=0.5,
  aura_fn=lambda r, L: buff("shaman_soe", "Erdstärke", "Stärke erhöht.", 120, lambda L: [stat_mod("Strength", 5 + L * 1.1)], L))
A(c, id="shaman_ancestral_spirit", name="Geist der Ahnen", description="Belebt ein totes Gruppenmitglied wieder.", **{"class": C}, level=12, trainingCost=train_cost(12),
  powerType="Mana", cost=110, castTime=10, maxRange=30, target="DeadFriendly", outOfCombatOnly=True, school="Nature", icon="ancestral_spirit", animation="cast_long",
  effects=[eff("Resurrect", min=35, max=35)])
AU(c, id="shaman_frost_slow", name="Frostschock", description="Bewegungstempo um 50 % verringert.", isDebuff=True, duration=8, school="Frost", modifiers=[{"type": "MoveSpeedPercent", "value": -50}])
ranked(c, A(c, id="shaman_frost_shock", name="Frostschock", description="Frostschaden und Verlangsamung.", **{"class": C}, powerType="Mana", cooldown=6,
  maxRange=20, target="Enemy", school="Frost", icon="frost_shock", animation="cast_point",
  effects=[eff("SchoolDamage", school="Frost", coefficient=0.39), eff("ApplyAura", aura="shaman_frost_slow")]), [20, 34, 46, 58], [0.95, None], cost_c=0.9)
ranked(c, A(c, id="shaman_lesser_healing_wave", name="Geringe Welle der Heilung", description="Schnelle Heilung.", **{"class": C}, powerType="Mana", castTime=1.5,
  maxRange=40, target="FriendlyOrSelf", school="Nature", icon="lesser_healing_wave", animation="cast_heal",
  effects=[eff("Heal", coefficient=0.43)]), [20, 28, 36, 44, 52, 60], [1.0], cost_c=0.8)
AU(c, id="shaman_ghost_wolf", name="Geisterwolf", description="Bewegungstempo um 40 % erhöht.", permanent=True, form="Travel", dispellable=False, modifiers=[{"type": "MoveSpeedPercent", "value": 40}])
A(c, id="shaman_ghost_wolf", name="Geisterwolf", description="Verwandelt Euch in einen Geisterwolf (40 % schneller).", **{"class": C}, level=20, trainingCost=train_cost(20),
  powerType="Mana", cost=60, castTime=2, target="Self", icon="ghost_wolf", animation="shapeshift", effects=[eff("ApplyAura", aura="shaman_ghost_wolf")])
ranked(c, A(c, id="shaman_stormstrike", name="Sturmschlag", description="Ein Schlag mit zwei Waffen voller Sturmkraft. (Talent)", **{"class": C}, powerType="Mana",
  cooldown=10, maxRange=5, target="Enemy", notTrainable=True, icon="stormstrike", animation="attack_heavy",
  effects=[eff("WeaponDamage", weaponPercent=1.4)]), [40, 50, 60], [0.2], cost_c=0.6)
ranked(c, A(c, id="shaman_chain_heal", name="Kettenheilung", description="Heilt das Ziel und springt auf weitere Verbündete. (Talent)", **{"class": C}, powerType="Mana",
  castTime=2.5, maxRange=40, target="FriendlyOrSelf", school="Nature", notTrainable=True, icon="chain_heal", animation="cast_heal",
  effects=[eff("Heal", coefficient=0.71)]), [40, 46, 54], [1.6], cost_c=1.3)

# =====================================================================================
# MAGIER (Mana)
# =====================================================================================
c = "mage"; C = "Mage"
ranked(c, A(c, id="mage_fireball", name="Feuerball", description="Schleudert einen flammenden Ball.", **{"class": C}, powerType="Mana", castTime=3.0,
  maxRange=35, target="Enemy", school="Fire", icon="fireball", animation="cast",
  effects=[eff("SchoolDamage", school="Fire", coefficient=1.0)]), LV_DIRECT, [1.6], cost_c=1.5)
AU(c, id="mage_chill", name="Frostblitz", description="Bewegungstempo um 40 % verringert.", isDebuff=True, duration=5, school="Frost", modifiers=[{"type": "MoveSpeedPercent", "value": -40}])
ranked(c, A(c, id="mage_frostbolt", name="Frostblitz", description="Frostschaden und Verlangsamung.", **{"class": C}, level=4, powerType="Mana", castTime=2.5,
  maxRange=30, target="Enemy", school="Frost", icon="frostbolt", animation="cast",
  effects=[eff("SchoolDamage", school="Frost", coefficient=0.81), eff("ApplyAura", aura="mage_chill")]), [4, 8, 14, 20, 26, 32, 38, 44, 50, 56], [1.3, None], cost_c=1.3)
ranked(c, A(c, id="mage_arcane_intellect", name="Arkane Intelligenz", description="Erhöht die Intelligenz des Ziels 30 Minuten lang.", **{"class": C},
  powerType="Mana", maxRange=30, target="FriendlyOrSelf", school="Arcane", icon="arcane_intellect", animation="cast_point", effects=[eff("ApplyAura")]),
  [1, 14, 28, 42, 56], [None], cost_c=0.6,
  aura_fn=lambda r, L: buff("mage_ai", "Arkane Intelligenz", "Intelligenz erhöht.", 1800, lambda L: [stat_mod("Intellect", 2 + L * 0.55)], L))
ranked(c, A(c, id="mage_frost_armor", name="Frostrüstung", description="Erhöht die Rüstung für 30 Minuten.", **{"class": C}, powerType="Mana",
  target="Self", school="Frost", icon="frost_armor", animation="cast_self", effects=[eff("ApplyAura")]), [1, 10, 20, 30, 40, 50], [None], cost_c=0.5,
  aura_fn=lambda r, L: buff("mage_frost_armor", "Frostrüstung", "Rüstung erhöht.", 1800, lambda L: [{"type": "ArmorFlat", "value": int(30 + L * 8)}], L))
ranked(c, A(c, id="mage_fire_blast", name="Feuerschlag", description="Sofortiger Feuerschaden.", **{"class": C}, level=6, powerType="Mana", cooldown=8,
  maxRange=20, target="Enemy", school="Fire", icon="fire_blast", animation="cast_point",
  effects=[eff("SchoolDamage", school="Fire", coefficient=0.43)]), [6, 14, 22, 30, 38, 46, 54], [1.0], cost_c=1.0)
AU(c, id="mage_polymorph", name="Verwandlung", description="In ein Schaf verwandelt. Schaden beendet den Effekt.", isDebuff=True, duration=20, stun=True, breaksOnDamage=True, school="Arcane")
A(c, id="mage_polymorph", name="Verwandlung", description="Verwandelt einen Gegner bis zu 20 Sekunden lang in ein Schaf.", **{"class": C}, level=8, trainingCost=train_cost(8),
  powerType="Mana", cost=60, castTime=1.5, maxRange=30, target="Enemy", school="Arcane", icon="polymorph", animation="cast",
  effects=[eff("ApplyAura", aura="mage_polymorph")])
AU(c, id="mage_frost_nova", name="Frostnova", description="Eingefroren.", isDebuff=True, duration=8, root=True, breaksOnDamage=False, school="Frost")
ranked(c, A(c, id="mage_frost_nova", name="Frostnova", description="Friert Gegner in der Nähe am Boden fest.", **{"class": C}, powerType="Mana", cooldown=25,
  target="AreaAroundSelf", radius=10, school="Frost", icon="frost_nova", animation="cast_ground",
  effects=[eff("SchoolDamage", school="Frost", coefficient=0.13), eff("ApplyAura", aura="mage_frost_nova")]), [10, 26, 40, 54], [0.2, None], cost_c=0.8)
ranked(c, A(c, id="mage_arcane_explosion", name="Arkane Explosion", description="Arkaner Schaden an allen Gegnern in der Nähe.", **{"class": C}, powerType="Mana",
  target="AreaAroundSelf", radius=10, school="Arcane", icon="arcane_explosion", animation="cast_ground",
  effects=[eff("SchoolDamage", school="Arcane", coefficient=0.14)]), [14, 22, 30, 38, 46, 54], [0.45], cost_c=1.2)
ranked(c, A(c, id="mage_arcane_missiles", name="Arkane Geschosse", description="Eine Salve arkaner Geschosse.", **{"class": C}, powerType="Mana", castTime=3.0,
  maxRange=30, target="Enemy", school="Arcane", icon="arcane_missiles", animation="cast_channel",
  effects=[eff("SchoolDamage", school="Arcane", coefficient=1.0)]), [8, 16, 24, 32, 40, 48, 56], [1.55], cost_c=1.6)
A(c, id="mage_counterspell", name="Gegenzauber", description="Unterbricht einen Zauber und sperrt die Schule 8 Sekunden.", **{"class": C}, level=24,
  trainingCost=train_cost(24), powerType="Mana", cost=100, cooldown=24, maxRange=30, target="Enemy", school="Arcane", triggersGcd=False, icon="counterspell",
  animation="cast_point", effects=[eff("Interrupt", duration=8)])
A(c, id="mage_teleport_home", name="Teleportieren: Heimat", description="Teleportiert Euch zur Hauptsiedlung Eurer Fraktion.", **{"class": C}, level=20,
  trainingCost=train_cost(20), powerType="Mana", cost=120, castTime=10, cooldown=60, target="Self", outOfCombatOnly=True, school="Arcane", icon="teleport", animation="cast_long",
  effects=[eff("Teleport", zone="@home")])
ranked(c, A(c, id="mage_pyroblast", name="Pyroschlag", description="Ein gewaltiger Feuerball mit Nachbrennen. (Talent)", **{"class": C}, powerType="Mana", castTime=6,
  maxRange=35, target="Enemy", school="Fire", notTrainable=True, icon="pyroblast", animation="cast_long",
  effects=[eff("SchoolDamage", school="Fire", coefficient=1.0), eff("ApplyAura")]), [20, 30, 40, 50, 60], [2.6, None], cost_c=2.2,
  aura_fn=lambda r, L: dot(c, "mage_pyro_burn", "Pyroschlag", "Fire", 0.8, 12, 3, "Brennt.", L))
AU(c, id="mage_icy_veins", name="Eisige Adern", description="Zaubertempo erhöht.", duration=20, modifiers=[{"type": "CastTimeFlat", "value": -0.5}])
A(c, id="mage_icy_veins", name="Eisige Adern", description="Verkürzt 20 Sekunden lang alle Zauberzeiten. (Talent)", **{"class": C}, level=40, powerType="Mana", cost=100,
  cooldown=180, target="Self", triggersGcd=False, notTrainable=True, icon="icy_veins", animation="cast_self", effects=[eff("ApplyAura", aura="mage_icy_veins")])

# =====================================================================================
# HEXENMEISTER (Mana)
# =====================================================================================
c = "warlock"; C = "Warlock"
ranked(c, A(c, id="warlock_shadow_bolt", name="Schattenblitz", description="Ein Blitz aus purem Schatten.", **{"class": C}, powerType="Mana", castTime=3.0,
  maxRange=30, target="Enemy", school="Shadow", icon="shadow_bolt", animation="cast",
  effects=[eff("SchoolDamage", school="Shadow", coefficient=0.86)]), LV_DIRECT, [1.55], cost_c=1.4)
ranked(c, A(c, id="warlock_immolate", name="Feuerbrand", description="Feuerschaden und zusätzlicher Brand über 15 Sekunden.", **{"class": C}, powerType="Mana",
  castTime=2.0, maxRange=30, target="Enemy", school="Fire", icon="immolate", animation="cast",
  effects=[eff("SchoolDamage", school="Fire", coefficient=0.2), eff("ApplyAura")]), [1, 10, 20, 30, 40, 50, 60], [0.5, None], cost_c=1.0,
  aura_fn=lambda r, L: dot(c, "warlock_immolate", "Feuerbrand", "Fire", 0.9, 15, 3, "Brennt.", L))
ranked(c, A(c, id="warlock_corruption", name="Verderbnis", description="Verdirbt das Ziel und verursacht 18 Sekunden lang Schattenschaden.", **{"class": C}, level=4,
  powerType="Mana", castTime=2.0, maxRange=30, target="Enemy", school="Shadow", icon="corruption", animation="cast", effects=[eff("ApplyAura")]),
  [4, 14, 24, 34, 44, 54], [None], cost_c=0.8,
  aura_fn=lambda r, L: dot(c, "warlock_corruption", "Verderbnis", "Shadow", 1.25, 18, 3, "Erleidet Schattenschaden.", L))
ranked(c, A(c, id="warlock_curse_of_agony", name="Fluch der Pein", description="Fluch: 24 Sekunden lang Schattenschaden.", **{"class": C}, level=8,
  powerType="Mana", maxRange=30, target="Enemy", school="Shadow", icon="curse_agony", animation="cast_point", effects=[eff("ApplyAura")]),
  [8, 18, 28, 38, 48, 58], [None], cost_c=0.6,
  aura_fn=lambda r, L: dot(c, "warlock_coa", "Fluch der Pein", "Shadow", 1.4, 24, 3, "Verflucht.", L))
ranked(c, A(c, id="warlock_life_tap", name="Aderlass", description="Wandelt Leben in Mana um.", **{"class": C}, level=6, powerType="Mana", cost=0,
  target="Self", icon="life_tap", animation="cast_self", effects=[eff("GainPower", onSelf=True), eff("SchoolDamage", school="Shadow", onSelf=True)]), [6, 16, 26, 36, 46, 56], [0.9, 0.9], fixed_cost=0)
A(c, id="warlock_summon_imp", name="Wichtel beschwören", description="Beschwört einen Wichtel, der Feuerblitze schleudert.", **{"class": C}, level=1,
  powerType="Mana", cost=40, castTime=6, target="Self", outOfCombatOnly=True, icon="summon_imp", animation="cast_long", effects=[eff("SummonPet", pet="pet_imp")])
A(c, id="warlock_summon_voidwalker", name="Leerwandler beschwören", description="Beschwört einen Leerwandler, der Gegner bindet.", **{"class": C}, level=10,
  trainingCost=train_cost(10), powerType="Mana", cost=80, castTime=8, target="Self", outOfCombatOnly=True, icon="summon_voidwalker", animation="cast_long",
  effects=[eff("SummonPet", pet="pet_voidwalker")])
AU(c, id="warlock_fear", name="Furcht", description="Flieht voller Furcht.", isDebuff=True, duration=10, fear=True, breaksOnDamage=True, school="Shadow")
A(c, id="warlock_fear", name="Furcht", description="Lässt das Ziel bis zu 10 Sekunden lang fliehen.", **{"class": C}, level=8, trainingCost=train_cost(8),
  powerType="Mana", cost=60, castTime=1.5, maxRange=20, target="Enemy", school="Shadow", icon="fear", animation="cast",
  effects=[eff("ApplyAura", aura="warlock_fear")])
ranked(c, A(c, id="warlock_demon_skin", name="Dämonenhaut", description="Erhöht Rüstung und Lebensregeneration für 30 Minuten.", **{"class": C}, powerType="Mana",
  target="Self", school="Shadow", icon="demon_skin", animation="cast_self", effects=[eff("ApplyAura")]), [1, 10, 20, 30, 40, 50], [None], cost_c=0.5,
  aura_fn=lambda r, L: buff("warlock_demon_skin", "Dämonenhaut", "Rüstung erhöht.", 1800, lambda L: [{"type": "ArmorFlat", "value": int(40 + L * 9)}], L))
ranked(c, A(c, id="warlock_drain_life", name="Blutsauger", description="Entzieht dem Ziel Leben. (Schattenschaden)", **{"class": C}, level=14, powerType="Mana",
  castTime=2.5, maxRange=20, target="Enemy", school="Shadow", icon="drain_life", animation="cast_channel",
  effects=[eff("SchoolDamage", school="Shadow", coefficient=0.5), eff("Heal", onSelf=True, coefficient=0.3)]), [14, 22, 30, 38, 46, 54], [0.9, 0.6], cost_c=1.0)
ranked(c, A(c, id="warlock_shadowburn", name="Schattenbrand", description="Sofortiger Schattenschaden. (Talent)", **{"class": C}, powerType="Mana", cooldown=15,
  maxRange=20, target="Enemy", school="Shadow", notTrainable=True, icon="shadowburn", animation="cast_point",
  effects=[eff("SchoolDamage", school="Shadow", coefficient=0.43)]), [20, 32, 44, 56], [1.1], cost_c=1.0)
ranked(c, A(c, id="warlock_siphon_life", name="Lebensentzug", description="Saugt dem Ziel 30 Sekunden lang Leben aus. (Talent)", **{"class": C}, powerType="Mana",
  maxRange=30, target="Enemy", school="Shadow", notTrainable=True, icon="siphon_life", animation="cast_point", effects=[eff("ApplyAura")]), [30, 40, 50, 60], [None], cost_c=0.8,
  aura_fn=lambda r, L: dot(c, "warlock_siphon", "Lebensentzug", "Shadow", 1.0, 30, 3, "Leben wird entzogen.", L))

# =====================================================================================
# DRUIDE (Mana / Wut in Bärengestalt / Energie in Katzengestalt)
# =====================================================================================
c = "druid"; C = "Druid"
ranked(c, A(c, id="druid_wrath", name="Zorn", description="Schleudert Naturenergie auf den Gegner.", **{"class": C}, powerType="Mana", castTime=2.0,
  maxRange=30, target="Enemy", school="Nature", icon="wrath", animation="cast",
  effects=[eff("SchoolDamage", school="Nature", coefficient=0.57)]), LV_DIRECT, [1.05], cost_c=1.0)
ranked(c, A(c, id="druid_moonfire", name="Mondfeuer", description="Arkaner Sofortschaden und Schaden über Zeit.", **{"class": C}, level=4, powerType="Mana",
  maxRange=30, target="Enemy", school="Arcane", icon="moonfire", animation="cast_point",
  effects=[eff("SchoolDamage", school="Arcane", coefficient=0.15), eff("ApplyAura")]), [4, 10, 16, 22, 28, 34, 40, 46, 52, 58], [0.35, None], cost_c=0.8,
  aura_fn=lambda r, L: dot(c, "druid_moonfire", "Mondfeuer", "Arcane", 0.5, 12, 3, "Mondfeuer.", L))
ranked(c, A(c, id="druid_healing_touch", name="Heilende Berührung", description="Heilt ein Ziel.", **{"class": C}, powerType="Mana", castTime=3.0,
  maxRange=40, target="FriendlyOrSelf", school="Nature", icon="healing_touch", animation="cast_heal",
  effects=[eff("Heal", coefficient=0.86)]), LV_DIRECT, [2.0], cost_c=1.4)
ranked(c, A(c, id="druid_rejuvenation", name="Verjüngung", description="Heilt das Ziel 12 Sekunden lang.", **{"class": C}, level=4, powerType="Mana",
  maxRange=40, target="FriendlyOrSelf", school="Nature", icon="rejuvenation", animation="cast_point", effects=[eff("ApplyAura")]),
  [4, 10, 16, 22, 28, 34, 40, 46, 52, 58], [None], cost_c=0.8,
  aura_fn=lambda r, L: hot(c, "druid_rejuv", "Verjüngung", 1.3, 12, 3, "Wird geheilt.", L))
ranked(c, A(c, id="druid_mark_of_the_wild", name="Mal der Wildnis", description="Erhöht Rüstung und alle Werte des Ziels für 30 Minuten.", **{"class": C},
  powerType="Mana", maxRange=30, target="FriendlyOrSelf", school="Nature", icon="mark_wild", animation="cast_point", effects=[eff("ApplyAura")]),
  [1, 10, 20, 30, 40, 50, 60], [None], cost_c=0.6,
  aura_fn=lambda r, L: buff("druid_motw", "Mal der Wildnis", "Werte erhöht.", 1800,
                            lambda L: [{"type": "ArmorFlat", "value": int(25 + L * 4)}] + [stat_mod(s, 1 + L * 0.2) for s in ("Strength", "Agility", "Stamina", "Intellect", "Spirit")], L))
AU(c, id="druid_roots", name="Wucherwurzeln", description="Von Wurzeln festgehalten.", isDebuff=True, duration=12, root=True, school="Nature")
A(c, id="druid_entangling_roots", name="Wucherwurzeln", description="Hält das Ziel bis zu 12 Sekunden fest.", **{"class": C}, level=8, trainingCost=train_cost(8),
  powerType="Mana", cost=50, castTime=1.5, maxRange=30, target="Enemy", school="Nature", icon="entangling_roots", animation="cast",
  effects=[eff("ApplyAura", aura="druid_roots")])
AU(c, id="druid_bear_form", name="Bärengestalt", description="Bärengestalt: mehr Rüstung, Leben und Bedrohung. Nutzt Wut.", permanent=True, form="Bear", dispellable=False,
   modifiers=[{"type": "ArmorPercent", "value": 180}, {"type": "MaxHealthPercent", "value": 20}, {"type": "ThreatPercent", "value": 30}])
A(c, id="druid_bear_form", name="Bärengestalt", description="Verwandelt Euch in einen Bären – der Tank des Druiden.", **{"class": C}, level=10, trainingCost=train_cost(10),
  powerType="Mana", cost=50, target="Self", icon="bear_form", animation="shapeshift", effects=[eff("ApplyAura", aura="druid_bear_form")])
ranked(c, A(c, id="druid_maul", name="Zermalmen", description="Ein mächtiger Prankenhieb mit viel Bedrohung.", **{"class": C}, powerType="Rage", cost=12,
  maxRange=5, target="Enemy", requiredForms=["Bear"], icon="maul", animation="attack_heavy",
  effects=[eff("WeaponDamage", weaponPercent=1.0, threat=30, threatMultiplier=1.75)]), [10, 18, 26, 34, 42, 50, 58], [0.5], fixed_cost=12)
A(c, id="druid_growl", name="Knurren", description="Zwingt das Ziel, Euch anzugreifen.", **{"class": C}, level=10, trainingCost=train_cost(10),
  powerType="Rage", cost=0, cooldown=10, maxRange=10, target="Enemy", triggersGcd=False, requiredForms=["Bear"], icon="growl", animation="roar", effects=[eff("Taunt")])
ranked(c, A(c, id="druid_swipe", name="Prankenhieb", description="Trifft bis zu drei Gegner vor Euch.", **{"class": C}, powerType="Rage", cost=20,
  target="AreaAroundSelf", radius=6, maxTargets=3, requiredForms=["Bear"], icon="swipe", animation="attack_wide",
  effects=[eff("SchoolDamage", school="Physical", threatMultiplier=1.5)]), [16, 24, 34, 44, 54], [0.45], fixed_cost=20)
AU(c, id="druid_cat_form", name="Katzengestalt", description="Katzengestalt: schneller und tödlicher. Nutzt Energie.", permanent=True, form="Cat", dispellable=False,
   modifiers=[{"type": "CritChance", "value": 3}, {"type": "MoveSpeedPercent", "value": 10}, {"type": "ThreatPercent", "value": -30}])
A(c, id="druid_cat_form", name="Katzengestalt", description="Verwandelt Euch in eine Raubkatze.", **{"class": C}, level=20, trainingCost=train_cost(20),
  powerType="Mana", cost=60, target="Self", icon="cat_form", animation="shapeshift", effects=[eff("ApplyAura", aura="druid_cat_form")])
ranked(c, A(c, id="druid_claw", name="Klaue", description="Ein Klauenhieb. Erzeugt einen Kombopunkt.", **{"class": C}, powerType="Energy", cost=40,
  maxRange=5, target="Enemy", requiredForms=["Cat"], icon="claw", animation="attack",
  effects=[eff("WeaponDamage", weaponPercent=1.0), eff("ComboPoints", min=1, max=1)]), [20, 28, 36, 44, 52, 60], [0.4, None], fixed_cost=40)
ranked(c, A(c, id="druid_rip", name="Zerfetzen", description="Finishing-Move: Starker Blutungsschaden pro Kombopunkt.", **{"class": C}, powerType="Energy", cost=30,
  maxRange=5, target="Enemy", requiresComboPoints=True, requiredForms=["Cat"], icon="rip", animation="attack_heavy",
  effects=[eff("SchoolDamage", school="Physical", perComboPoint=1)]), [20, 30, 40, 50, 60], [0.45], fixed_cost=30)
A(c, id="druid_rebirth", name="Wiedergeburt", description="Belebt ein totes Gruppenmitglied auch im Kampf wieder.", **{"class": C}, level=20,
  trainingCost=train_cost(20), powerType="Mana", cost=200, castTime=2, cooldown=1800, maxRange=30, target="DeadFriendly", school="Nature",
  icon="rebirth", animation="cast_long", effects=[eff("Resurrect", min=40, max=40)])
ranked(c, A(c, id="druid_starfire", name="Sternenfeuer", description="Ein gewaltiger arkaner Zauber. (Talent)", **{"class": C}, powerType="Mana", castTime=3.5,
  maxRange=30, target="Enemy", school="Arcane", notTrainable=True, icon="starfire", animation="cast",
  effects=[eff("SchoolDamage", school="Arcane", coefficient=1.0)]), [20, 28, 36, 44, 52, 60], [1.9], cost_c=1.6)
ranked(c, A(c, id="druid_swiftmend", name="Rasche Heilung", description="Sofortige Heilung. (Talent)", **{"class": C}, powerType="Mana", cooldown=15,
  maxRange=40, target="FriendlyOrSelf", school="Nature", notTrainable=True, icon="swiftmend", animation="cast_point",
  effects=[eff("Heal", coefficient=0.5)]), [40, 50, 60], [1.5], cost_c=0.8)

# =====================================================================================
# Allgemein: Gegenstände (Essen/Trinken/Tränke/Ruhestein) und Volksfähigkeiten
# =====================================================================================
c = "common"
AU(c, id="food_regen", name="Essen", description="Stellt Leben wieder her. Bewegung beendet den Effekt.", duration=18, breaksOnMove=True, periodic="Heal", tickInterval=3, tickMin=0, tickMax=0)
AU(c, id="drink_regen", name="Trinken", description="Stellt Mana wieder her. Bewegung beendet den Effekt.", duration=18, breaksOnMove=True, periodic="Power", tickInterval=3, tickMin=0, tickMax=0)
AU(c, id="resurrection_sickness", name="Wiederbelebungsschwäche", description="Alle Werte um 75 % verringert.", isDebuff=True, duration=600, dispellable=False,
   persistsThroughDeath=True, modifiers=[{"type": "StatPercent", "stat": s, "value": -75} for s in ("Strength", "Agility", "Stamina", "Intellect", "Spirit")])
A(c, id="item_eat", name="Essen", description="Stellt 18 Sekunden lang Leben wieder her.", **{"class": "None"}, level=1, notTrainable=True, target="Self", outOfCombatOnly=True,
  triggersGcd=False, icon="food", animation="eat", effects=[eff("RestorePercent", min=100, max=100, aura="food_regen")])
A(c, id="item_drink", name="Trinken", description="Stellt 18 Sekunden lang Mana wieder her.", **{"class": "None"}, level=1, notTrainable=True, target="Self", outOfCombatOnly=True,
  triggersGcd=False, icon="drink", animation="drink", effects=[eff("RestorePercent", min=100, max=100, aura="drink_regen")])
for name, lv, mn, mx in (("item_minor_healing_potion", 1, 70, 90), ("item_lesser_healing_potion", 10, 140, 180), ("item_healing_potion", 20, 280, 360)):
    A(c, id=name, name="Heiltrank", description="Stellt sofort Leben wieder her.", **{"class": "None"}, level=lv, notTrainable=True, target="Self", cooldown=120,
      triggersGcd=False, icon="potion_red", animation="drink", effects=[eff("Heal", min=mn, max=mx)])
A(c, id="item_hearthstone", name="Ruhestein", description="Bringt Euch zum Gasthaus Eures Heimatortes zurück.", **{"class": "None"}, level=1, notTrainable=True,
  castTime=10, cooldown=1800, target="Self", outOfCombatOnly=True, icon="hearthstone", animation="cast_long", effects=[eff("Teleport", zone="@bind")])
# Berufserzeugnisse (Kapitel 17): Manatränke, Elixiere, Essen mit „Satt“, Verbände
A(c, id="item_greater_healing_potion", name="Großer Heiltrank", description="Stellt sofort Leben wieder her.", **{"class": "None"}, level=30, notTrainable=True, target="Self",
  cooldown=120, triggersGcd=False, icon="potion_red", animation="drink", effects=[eff("Heal", min=455, max=585)])
for r, (lv, mn, mx) in enumerate(((1, 140, 180), (10, 280, 360), (20, 455, 585), (30, 700, 900)), 1):
    A(c, id=f"item_mana_potion_r{r}", name="Manatrank", description="Stellt sofort Mana wieder her.", **{"class": "None"}, level=lv, notTrainable=True, target="Self",
      cooldown=120, triggersGcd=False, icon="potion_blue", animation="drink", effects=[eff("GainPower", min=mn, max=mx)])
for eid, name, stat, val in (("elixir_strength", "Elixier der Löwenkraft", "Strength", 8), ("elixir_agility", "Elixier der Behändigkeit", "Agility", 8),
                             ("elixir_intellect", "Elixier der Weisheit", "Intellect", 8), ("elixir_stamina", "Elixier der Standhaftigkeit", "Stamina", 10)):
    AU(c, id=eid, name=name, description=f"{stat} erhöht.", duration=3600, modifiers=[{"type": "StatFlat", "stat": stat, "value": val}])
    A(c, id="item_" + eid, name=name, description="Eine Stunde lang gestärkt.", **{"class": "None"}, level=1, notTrainable=True, target="Self",
      triggersGcd=False, icon="potion_green", animation="drink", effects=[eff("ApplyAura", aura=eid)])
for r in range(1, 5):
    AU(c, id=f"well_fed_r{r}", name="Satt", description="Ausdauer und Willenskraft erhöht.", duration=900,
       modifiers=[{"type": "StatFlat", "stat": "Stamina", "value": 2 + r * 2}, {"type": "StatFlat", "stat": "Spirit", "value": 2 + r * 2}])
    A(c, id=f"item_cooked_food_r{r}", name="Gekochtes Essen", description="Stellt Leben wieder her und macht satt.", **{"class": "None"}, level=1, notTrainable=True,
      target="Self", outOfCombatOnly=True, triggersGcd=False, icon="food", animation="eat",
      effects=[eff("RestorePercent", min=100, max=100, aura="food_regen"), eff("ApplyAura", aura=f"well_fed_r{r}")])
AU(c, id="first_aid_recent", name="Kürzlich verbunden", description="Kann noch nicht erneut verbunden werden.", isDebuff=True, duration=60, dispellable=False)
for r, (lv, mn, mx) in enumerate(((1, 66, 66), (10, 114, 114), (20, 301, 301), (30, 640, 640)), 1):
    A(c, id=f"item_bandage_r{r}", name="Verband", description="Heilt das Ziel über einige Sekunden. Danach eine Minute lang nicht erneut möglich.", **{"class": "None"},
      level=lv, notTrainable=True, castTime=6, target="FriendlyOrSelf", maxRange=10, triggersGcd=False, icon="bandage", animation="bandage",
      effects=[eff("Heal", min=mn, max=mx), eff("ApplyAura", aura="first_aid_recent")])
# Reittiere (Kapitel 18): Aufsitzen dauert 1,5 Sekunden, Schaden oder eigene Angriffe werfen ab.
for sp in (60, 100):
    AU(c, id=f"mount_{sp}", name="Beritten", description=f"Bewegungstempo um {sp} % erhöht.", permanent=True, mounted=True, breaksOnDamage=True, dispellable=False,
       modifiers=[{"type": "MoveSpeedPercent", "value": sp}])
    A(c, id=f"mount_ride_{sp}", name="Aufsitzen", description=f"Ruft Euer Reittier (+{sp} % Tempo).", **{"class": "None"}, level=40 if sp == 60 else 60, notTrainable=True,
      castTime=1.5, target="Self", outOfCombatOnly=True, triggersGcd=False, icon="mount", animation="mount", effects=[eff("ApplyAura", aura=f"mount_{sp}")])
# Volksfähigkeiten
AU(c, id="racial_stoneform", name="Steingestalt", description="Rüstung um 10 % erhöht, Gifte und Blutungen enden.", duration=8, modifiers=[{"type": "ArmorPercent", "value": 10}])
A(c, id="racial_stoneform", name="Steingestalt", description="Felsgeborene: Verwandelt die Haut 8 Sekunden lang zu Stein.", **{"class": "None"}, level=1, notTrainable=True,
  cooldown=180, target="Self", triggersGcd=False, icon="stoneform", animation="stoneform", effects=[eff("ApplyAura", aura="racial_stoneform"), eff("Dispel", min=2, max=2, onSelf=True)])
AU(c, id="racial_bloodfury", name="Blutwut", description="Angriffskraft erhöht.", duration=15, modifiers=[{"type": "AttackPowerFlat", "value": 60}])
A(c, id="racial_bloodfury", name="Blutwut", description="Grakar: Erhöht 15 Sekunden lang die Angriffskraft.", **{"class": "None"}, level=1, notTrainable=True,
  cooldown=120, target="Self", triggersGcd=False, icon="bloodfury", animation="roar", effects=[eff("ApplyAura", aura="racial_bloodfury")])
AU(c, id="racial_warstomp", name="Kriegsdonner", description="Betäubt.", isDebuff=True, duration=2, stun=True)
A(c, id="racial_warstomp", name="Kriegsdonner", description="Hornvolk: Betäubt Gegner in 8 m für 2 Sekunden.", **{"class": "None"}, level=1, notTrainable=True,
  cooldown=120, castTime=0.5, target="AreaAroundSelf", radius=8, maxTargets=5, icon="warstomp", animation="stomp", effects=[eff("ApplyAura", aura="racial_warstomp")])
A(c, id="racial_shadowmeld", name="Schattenmimik", description="Elarii: Verschmilzt außerhalb des Kampfes mit den Schatten.", **{"class": "None"}, level=1, notTrainable=True,
  cooldown=10, target="Self", outOfCombatOnly=True, triggersGcd=False, icon="shadowmeld", animation="crouch", effects=[eff("ApplyAura", aura="rogue_stealth")])
A(c, id="racial_will_of_the_night", name="Wille der Nacht", description="Nachtgeborene: Beendet Furcht und ähnliche Effekte.", **{"class": "None"}, level=1, notTrainable=True,
  cooldown=120, target="Self", triggersGcd=False, icon="will_night", animation="cast_self", effects=[eff("Dispel", min=1, max=1, onSelf=True)])
AU(c, id="racial_regeneration", name="Echsenhaut-Regeneration", description="Regeneriert Leben.", duration=10, periodic="Heal", tickInterval=2, tickMin=0, tickMax=0)
A(c, id="racial_scale_regen", name="Schuppenregeneration", description="Skarr: Stellt 10 Sekunden lang 20 % Leben wieder her.", **{"class": "None"}, level=1, notTrainable=True,
  cooldown=180, target="Self", triggersGcd=False, icon="scale_regen", animation="cast_self", effects=[eff("RestorePercent", min=20, max=20, aura="racial_regeneration")])
AU(c, id="racial_escape", name="Entfesselungskunst", description="Bewegungstempo erhöht.", duration=4, modifiers=[{"type": "MoveSpeedPercent", "value": 40}])
A(c, id="racial_escape_artist", name="Entfesselungskunst", description="Fennlinge: Befreit sich aus Bewegungseinschränkungen.", **{"class": "None"}, level=1, notTrainable=True,
  cooldown=105, castTime=0.5, target="Self", icon="escape_artist", animation="cast_self", effects=[eff("Dispel", min=1, max=1, onSelf=True), eff("ApplyAura", aura="racial_escape")])
AU(c, id="racial_perception", name="Wachsamkeit", description="Kritische Trefferchance erhöht.", duration=20, modifiers=[{"type": "CritChance", "value": 5}])
A(c, id="racial_perception", name="Wachsamkeit", description="Aldorer: Erhöht 20 Sekunden lang die kritische Trefferchance.", **{"class": "None"}, level=1, notTrainable=True,
  cooldown=180, target="Self", triggersGcd=False, icon="perception", animation="cast_self", effects=[eff("ApplyAura", aura="racial_perception")])

# =====================================================================================
# Kreaturen- und Bossfähigkeiten
# =====================================================================================
c = "creature"
def mob_ability(id, name, desc, **kw):
    d = dict(id=id, name=name, description=desc, level=1, notTrainable=True, icon=id, animation="attack")
    d["class"] = "None"
    d.update(kw)
    return A(c, **d)
AU(c, id="mob_poison", name="Gift", description="Erleidet Naturschaden.", isDebuff=True, duration=12, school="Nature", periodic="Damage", tickInterval=3, tickMin=3, tickMax=5)
AU(c, id="mob_strong_poison", name="Starkes Gift", description="Erleidet Naturschaden.", isDebuff=True, duration=12, maxStacks=3, school="Nature", periodic="Damage", tickInterval=3, tickMin=9, tickMax=12)
AU(c, id="mob_web", name="Netz", description="Festgehalten.", isDebuff=True, duration=5, root=True)
AU(c, id="mob_bleed", name="Blutung", description="Blutet.", isDebuff=True, duration=9, periodic="Damage", tickInterval=3, tickMin=4, tickMax=6)
AU(c, id="mob_enrage", name="Raserei", description="Schaden und Angriffstempo stark erhöht.", duration=600, dispellable=False,
   modifiers=[{"type": "DamageDonePercent", "value": 50}, {"type": "AttackSpeedPercent", "value": 30}])
AU(c, id="mob_stun", name="Betäubt", description="Betäubt.", isDebuff=True, duration=2, stun=True)
AU(c, id="mob_fear", name="Entsetzen", description="Flieht voller Furcht.", isDebuff=True, duration=4, fear=True, school="Shadow")
AU(c, id="mob_rootgrip", name="Wurzelgriff", description="Von Wurzeln festgehalten, erleidet Schaden.", isDebuff=True, duration=6, root=True, school="Nature",
   periodic="Damage", tickInterval=2, tickMin=12, tickMax=16)
AU(c, id="mob_armor_crush", name="Rüstungsbruch", description="Rüstung verringert.", isDebuff=True, duration=20, maxStacks=5, modifiers=[{"type": "ArmorPercent", "value": -8}])
AU(c, id="mob_spore_cloud", name="Sporenwolke", description="Erleidet Naturschaden.", isDebuff=True, duration=10, school="Nature", periodic="Damage", tickInterval=2, tickMin=6, tickMax=8)
AU(c, id="mob_scorch", name="Glutbrand", description="Brennt.", isDebuff=True, duration=9, school="Fire", periodic="Damage", tickInterval=3, tickMin=5, tickMax=7)
mob_ability("mob_bite", "Biss", "Ein kräftiger Biss.", cooldown=6, maxRange=5, effects=[eff("WeaponDamage", weaponPercent=1.2)])
mob_ability("mob_poison_bite", "Giftbiss", "Vergiftet das Ziel.", cooldown=10, maxRange=5, school="Nature", effects=[eff("ApplyAura", aura="mob_poison")])
mob_ability("mob_strong_poison_bite", "Lähmender Giftbiss", "Starkes, stapelbares Gift.", cooldown=8, maxRange=5, school="Nature", effects=[eff("ApplyAura", aura="mob_strong_poison")])
mob_ability("mob_web_spit", "Netz spucken", "Hält das Ziel fest.", cooldown=15, maxRange=20, effects=[eff("ApplyAura", aura="mob_web")])
mob_ability("mob_rend", "Zerfleischen", "Verursacht Blutungen.", cooldown=12, maxRange=5, effects=[eff("ApplyAura", aura="mob_bleed")])
mob_ability("mob_firebolt", "Glutstoß", "Feuerschaden.", castTime=2.0, maxRange=30, school="Fire", effects=[eff("SchoolDamage", school="Fire", min=14, max=20)])
mob_ability("mob_firebolt_strong", "Flammenstoß", "Starker Feuerschaden.", castTime=2.5, maxRange=30, school="Fire", effects=[eff("SchoolDamage", school="Fire", min=40, max=55)])
mob_ability("mob_shadowbolt", "Schattenstoß", "Schattenschaden.", castTime=2.5, maxRange=30, school="Shadow", effects=[eff("SchoolDamage", school="Shadow", min=45, max=60)])
mob_ability("mob_heal_self", "Dunkle Genesung", "Heilt sich selbst.", castTime=2.0, target="Self", school="Shadow", effects=[eff("Heal", min=120, max=160)])
mob_ability("mob_heal_ally", "Verderbte Heilung", "Heilt einen Verbündeten.", castTime=2.0, target="FriendlyOrSelf", maxRange=30, school="Nature", effects=[eff("Heal", min=90, max=120)])
mob_ability("mob_shield_bash", "Schildhieb", "Unterbricht Zauber und betäubt kurz.", cooldown=12, maxRange=5, effects=[eff("Interrupt", duration=4), eff("ApplyAura", aura="mob_stun")])
mob_ability("mob_cleave", "Spalten", "Trifft mehrere Gegner vor sich.", cooldown=8, target="AreaAroundSelf", radius=6, maxTargets=3, effects=[eff("WeaponDamage", weaponPercent=1.0)])
mob_ability("mob_enrage", "Raserei", "Gerät in Raserei.", cooldown=600, target="Self", effects=[eff("ApplyAura", aura="mob_enrage")])
mob_ability("mob_ground_slam", "Erdstoß", "Schlägt auf den Boden und betäubt alle in der Nähe.", cooldown=18, castTime=1.5, target="AreaAroundSelf", radius=10,
            effects=[eff("SchoolDamage", school="Physical", min=60, max=80), eff("ApplyAura", aura="mob_stun")])
mob_ability("mob_terrify", "Schreckensschrei", "Lässt nahe Gegner fliehen.", cooldown=25, target="AreaAroundSelf", radius=10, school="Shadow", effects=[eff("ApplyAura", aura="mob_fear")])
mob_ability("mob_root_grip", "Wurzelgriff", "Wurzeln brechen aus dem Boden.", cooldown=14, maxRange=40, school="Nature", effects=[eff("ApplyAura", aura="mob_rootgrip")])
mob_ability("mob_armor_crush", "Rüstungsbrecher", "Zerschmettert die Rüstung.", cooldown=6, maxRange=5, effects=[eff("WeaponDamage", weaponPercent=0.8), eff("ApplyAura", aura="mob_armor_crush")])
mob_ability("mob_spore_burst", "Sporenausbruch", "Giftige Sporen um sich herum.", cooldown=12, target="AreaAroundSelf", radius=12, school="Nature", effects=[eff("ApplyAura", aura="mob_spore_cloud")])
mob_ability("mob_scorch", "Versengen", "Setzt das Ziel in Brand.", cooldown=9, maxRange=25, school="Fire", effects=[eff("SchoolDamage", school="Fire", min=10, max=14), eff("ApplyAura", aura="mob_scorch")])
# Dungeon-Mechaniken (Kapitel 43/44): Nutzlast von Bodeneffekten und Pflicht-Unterbrechungen. Bodeneffekte treffen jeden,
# der nach der Vorwarnung noch im markierten Kreis steht – wer ausweicht, bleibt unverletzt.
AU(c, id="mob_spore_slow", name="Sporenschleim", description="Bewegungstempo verringert.", isDebuff=True, duration=3, school="Nature",
   modifiers=[{"type": "MoveSpeedPercent", "value": -30}])
AU(c, id="mob_root_snare", name="Wurzelfessel", description="Von Wurzeln gehalten.", isDebuff=True, duration=3, root=True, school="Nature")
AU(c, id="mob_berserk", name="Ungezügelte Wut", description="Der Kampf dauert zu lange: Schaden und Tempo massiv erhöht.", duration=600, dispellable=False,
   modifiers=[{"type": "DamageDonePercent", "value": 150}, {"type": "AttackSpeedPercent", "value": 50}])
mob_ability("mob_spore_carpet", "Sporenteppich", "Ein Teppich aus Sporen, der jeden darin vergiftet.", maxRange=200, school="Nature",
            effects=[eff("SchoolDamage", school="Nature", min=36, max=42), eff("ApplyAura", aura="mob_spore_slow")])
mob_ability("mob_shadow_ring", "Schattenkreis", "Ein Kreis aus Schatten, der nach kurzer Zeit zerbirst.", maxRange=200, school="Shadow",
            effects=[eff("SchoolDamage", school="Shadow", min=160, max=185)])
mob_ability("mob_root_eruption", "Wurzelbruch", "Wurzeln brechen dort aus dem Boden, wo eben noch jemand stand.", maxRange=200, school="Nature",
            effects=[eff("SchoolDamage", school="Nature", min=200, max=230), eff("ApplyAura", aura="mob_root_snare")])
mob_ability("mob_dark_mending", "Verderbte Genesung", "Lange Selbstheilung – muss unterbrochen werden.", castTime=3.0, cooldown=1, target="Self", school="Shadow",
            effects=[eff("Heal", min=700, max=800)])
mob_ability("mob_berserk", "Ungezügelte Wut", "Gerät nach zu langem Kampf in ungezügelte Wut.", target="Self", effects=[eff("ApplyAura", aura="mob_berserk")])
mob_ability("mob_throw", "Wurfaxt", "Wirft eine Axt.", cooldown=6, minRange=5, maxRange=25, effects=[eff("SchoolDamage", school="Physical", min=12, max=18)])
mob_ability("mob_net", "Netzwurf", "Hält das Ziel fest.", cooldown=20, maxRange=15, effects=[eff("ApplyAura", aura="mob_web")])
mob_ability("pet_imp_firebolt", "Feuerblitz", "Der Wichtel schleudert einen Feuerblitz.", castTime=2.0, maxRange=30, school="Fire", effects=[eff("SchoolDamage", school="Fire", min=8, max=12, coefficient=0.1)])
mob_ability("pet_torment", "Qual", "Der Leerwandler zieht die Aufmerksamkeit auf sich.", cooldown=5, maxRange=5, school="Shadow", effects=[eff("SchoolDamage", school="Shadow", min=2, max=4, threat=120)])
mob_ability("pet_growl", "Knurren", "Der Wolf zieht die Aufmerksamkeit auf sich.", cooldown=5, maxRange=5, effects=[eff("SchoolDamage", school="Physical", min=2, max=4, threat=80)])

# =====================================================================================
# TALENTBÄUME (27). Jede Reihe verlangt 5 Punkte im Baum. Werte gelten pro Rang.
# =====================================================================================
def T(id, name, desc, tier, col, rank, mods, requires=None, grants=None):
    d = {"id": id, "name": name, "description": desc, "tier": tier, "column": col, "maxRank": rank, "modifiers": mods}
    if requires: d["requires"] = requires
    if grants: d["grantsAbility"] = grants
    return d

def M(t, v, **kw):
    d = {"type": t, "value": v}
    d.update(kw)
    return d

TREES = []
def TREE(id, cls, name, desc, role, talents):
    TREES.append({"id": id, "class": cls, "name": name, "description": desc, "primaryRole": role, "talents": talents})

# --- Krieger
TREE("warrior_arms", "Warrior", "Waffen", "Meisterschaft über schwere Waffen und brutale Treffer.", "MeleeDps", [
    T("wa_improved_heroic", "Verbesserter Heldenhafter Stoß", "Heldenhafter Stoß verursacht 4 % mehr Schaden.", 0, 0, 3, [M("DamageDonePercent", 4, ability="warrior_heroic_strike")]),
    T("wa_deflection", "Abwehr", "Erhöht die Parierchance um 1 %.", 0, 1, 5, [M("ParryChance", 1)]),
    T("wa_tactical_mastery", "Taktische Meisterschaft", "Erhöht die Angriffskraft um 6.", 1, 0, 5, [M("AttackPowerFlat", 6)]),
    T("wa_improved_rend", "Verbessertes Verwunden", "Verwunden verursacht 15 % mehr Schaden.", 1, 1, 3, [M("DamageDonePercent", 15, ability="warrior_rend")]),
    T("wa_impale", "Durchbohren", "Kritische Treffer verursachen 10 % mehr Schaden.", 2, 1, 2, [M("CritDamagePercent", 10)]),
    T("wa_two_hand_spec", "Zweihandspezialisierung", "Verursachter körperlicher Schaden +2 %.", 3, 1, 5, [M("DamageDonePercent", 2, school="Physical")]),
    T("wa_sweeping", "Weitreichende Hiebe", "Kritische Trefferchance +1 %.", 4, 1, 3, [M("CritChance", 1, crit="Melee")]),
    T("wa_mortal_strike", "Tödlicher Stoß", "Schaltet Tödlicher Stoß frei.", 6, 1, 1, [], "wa_two_hand_spec", "warrior_mortal_strike"),
])
TREE("warrior_fury", "Warrior", "Furor", "Ungebremster Zorn und beidhändiger Kampf.", "MeleeDps", [
    T("wf_cruelty", "Grausamkeit", "Kritische Trefferchance +1 %.", 0, 1, 5, [M("CritChance", 1, crit="Melee")]),
    T("wf_booming_voice", "Donnernde Stimme", "Schlachtruf wirkt 10 % stärker auf die Gruppe (mehr Bedrohung).", 0, 0, 2, [M("ThreatPercent", 5, ability="warrior_battle_shout")]),
    T("wf_unbridled", "Ungezügelter Zorn", "Wutgewinn +8 %.", 1, 0, 5, [M("RageGenerationPercent", 8)]),
    T("wf_improved_execute", "Verbessertes Hinrichten", "Hinrichten kostet 15 % weniger.", 2, 1, 2, [M("CostPercent", -15, ability="warrior_execute")]),
    T("wf_dual_wield", "Beidhändigkeitsspezialisierung", "Körperlicher Schaden +2 %.", 2, 2, 5, [M("DamageDonePercent", 2, school="Physical")]),
    T("wf_flurry", "Schlaghagel", "Angriffstempo +3 %.", 4, 1, 5, [M("AttackSpeedPercent", 3)], "wf_cruelty"),
    T("wf_bloodthirst", "Blutdurst", "Angriffskraft +20.", 6, 1, 1, [M("AttackPowerFlat", 20)]),
])
TREE("warrior_protection", "Warrior", "Schutz", "Unerschütterlicher Beschützer seiner Gruppe.", "Tank", [
    T("wp_shield_spec", "Schildspezialisierung", "Blockchance +1 %.", 0, 1, 5, [M("BlockChance", 1)]),
    T("wp_anticipation", "Vorahnung", "Ausweichchance +1 %.", 0, 2, 5, [M("DodgeChance", 1)]),
    T("wp_toughness", "Zähigkeit", "Rüstung +2 %.", 1, 1, 5, [M("ArmorPercent", 2)]),
    T("wp_last_stand", "Letztes Gefecht", "Schaltet Letztes Gefecht frei.", 2, 1, 1, [], None, "warrior_last_stand"),
    T("wp_defiance", "Trotz", "Bedrohung +3 %.", 2, 2, 5, [M("ThreatPercent", 3)]),
    T("wp_improved_sunder", "Verbessertes Rüstung zerreißen", "Rüstung zerreißen kostet 10 % weniger.", 3, 1, 3, [M("CostPercent", -10, ability="warrior_sunder_armor")]),
    T("wp_vitality", "Vitalität", "Maximales Leben +2 %.", 4, 1, 5, [M("MaxHealthPercent", 2)]),
    T("wp_shield_slam", "Schildschlag", "Schaltet Schildschlag frei.", 6, 1, 1, [], "wp_improved_sunder", "warrior_shield_slam"),
])
# --- Paladin
TREE("paladin_holy", "Paladin", "Heilig", "Heilendes Licht für die Verbündeten.", "Healer", [
    T("ph_divine_strength", "Göttliche Stärke", "Stärke +2 %.", 0, 1, 5, [M("StatPercent", 2, stat="Strength")]),
    T("ph_divine_intellect", "Göttliche Intelligenz", "Intelligenz +2 %.", 0, 2, 5, [M("StatPercent", 2, stat="Intellect")]),
    T("ph_healing_light", "Heilendes Licht", "Heilung +4 %.", 1, 1, 3, [M("HealingDonePercent", 4)]),
    T("ph_illumination", "Erleuchtung", "Heilzauber kosten 4 % weniger.", 2, 1, 5, [M("CostPercent", -4, ability="paladin_holy_light"), M("CostPercent", -4, ability="paladin_flash_of_light")]),
    T("ph_holy_power", "Heilige Macht", "Kritische Zaubertrefferchance +1 %.", 3, 1, 5, [M("CritChance", 1, crit="Spell")]),
    T("ph_lasting_judgement", "Dauerhaftes Richturteil", "Heiligschaden +2 %.", 3, 2, 3, [M("DamageDonePercent", 2, school="Holy")]),
    T("ph_holy_shock", "Heiliger Schock", "Schaltet Heiliger Schock frei.", 6, 1, 1, [], "ph_holy_power", "paladin_holy_shock"),
])
TREE("paladin_protection", "Paladin", "Schutz", "Schild und Aura gegen jede Übermacht.", "Tank", [
    T("pp_improved_devotion", "Verbesserte Aura der Hingabe", "Rüstung +3 %.", 0, 1, 5, [M("ArmorPercent", 3)]),
    T("pp_redoubt", "Wehrhaftigkeit", "Blockchance +2 %.", 0, 2, 5, [M("BlockChance", 2)]),
    T("pp_toughness", "Zähigkeit", "Maximales Leben +2 %.", 1, 1, 5, [M("MaxHealthPercent", 2)]),
    T("pp_improved_fury", "Verbesserter Zorn der Gerechtigkeit", "Bedrohung +5 %.", 2, 1, 3, [M("ThreatPercent", 5)]),
    T("pp_shield_spec", "Schildspezialisierung", "Ausweichchance +1 %.", 3, 1, 5, [M("DodgeChance", 1)]),
    T("pp_anticipation", "Vorahnung", "Erlittener Schaden -1 %.", 4, 1, 5, [M("DamageTakenPercent", -1)]),
    T("pp_holy_shield", "Heiliger Schild", "Blockchance +10 %, Heiligbedrohung +10 %.", 6, 1, 1, [M("BlockChance", 10), M("ThreatPercent", 10, school="Holy")]),
])
TREE("paladin_retribution", "Paladin", "Vergeltung", "Gerechter Zorn mit Siegel und Schwert.", "MeleeDps", [
    T("pr_benediction", "Segnung", "Siegel und Richturteil kosten 3 % weniger.", 0, 1, 5, [M("CostPercent", -3, ability="paladin_judgement"), M("CostPercent", -3, ability="paladin_seal_of_righteousness")]),
    T("pr_improved_judgement", "Verbessertes Richturteil", "Richturteil: Abklingzeit -1 Sekunde.", 0, 2, 2, [M("CooldownFlat", -1, ability="paladin_judgement")]),
    T("pr_improved_might", "Verbesserter Segen der Macht", "Angriffskraft +8.", 1, 1, 5, [M("AttackPowerFlat", 8)]),
    T("pr_conviction", "Überzeugung", "Kritische Trefferchance +1 %.", 2, 1, 5, [M("CritChance", 1, crit="Melee")]),
    T("pr_two_hand", "Zweihandspezialisierung", "Körperlicher Schaden +2 %.", 3, 1, 3, [M("DamageDonePercent", 2, school="Physical")]),
    T("pr_vengeance", "Rache", "Verursachter Schaden +2 %.", 4, 1, 5, [M("DamageDonePercent", 2)], "pr_conviction"),
    T("pr_crusader_strike", "Kreuzfahrerstoß", "Schaltet Kreuzfahrerstoß frei.", 6, 1, 1, [], None, "paladin_crusader_strike"),
])
# --- Jäger
TREE("hunter_beast_mastery", "Hunter", "Tierherrschaft", "Eins mit dem tierischen Gefährten.", "RangedDps", [
    T("hb_endurance", "Ausdauertraining", "Maximales Leben +2 %.", 0, 1, 5, [M("MaxHealthPercent", 2)]),
    T("hb_improved_hawk", "Verbesserter Aspekt des Falken", "Angriffskraft +6.", 0, 2, 5, [M("AttackPowerFlat", 6)]),
    T("hb_thick_hide", "Dickes Fell", "Rüstung +4 %.", 1, 1, 3, [M("ArmorPercent", 4)]),
    T("hb_pathfinding", "Pfadfinder", "Bewegungstempo +2 %.", 1, 2, 2, [M("MoveSpeedPercent", 2)]),
    T("hb_ferocity", "Wildheit", "Kritische Trefferchance +1 %.", 2, 1, 5, [M("CritChance", 1)]),
    T("hb_frenzy", "Raserei", "Verursachter Schaden +2 %.", 4, 1, 5, [M("DamageDonePercent", 2)]),
    T("hb_bestial_wrath", "Zorn des Wildtiers", "Angriffskraft +30.", 6, 1, 1, [M("AttackPowerFlat", 30)]),
])
TREE("hunter_marksmanship", "Hunter", "Treffsicherheit", "Der perfekte Schuss aus großer Distanz.", "RangedDps", [
    T("hm_lethal_shots", "Tödliche Schüsse", "Kritische Distanztrefferchance +1 %.", 0, 1, 5, [M("CritChance", 1, crit="Ranged")]),
    T("hm_efficiency", "Effizienz", "Schüsse kosten 2 % weniger Mana.", 0, 2, 5, [M("CostPercent", -2)]),
    T("hm_aimed_shot", "Gezielter Schuss", "Schaltet Gezielter Schuss frei.", 2, 1, 1, [], None, "hunter_aimed_shot"),
    T("hm_improved_arcane", "Verbesserter Arkaner Schuss", "Arkaner Schuss: Abklingzeit -0,2 s.", 1, 1, 5, [M("CooldownFlat", -0.2, ability="hunter_arcane_shot")]),
    T("hm_mortal_shots", "Tödliche Schüsse", "Kritischer Schaden +6 %.", 3, 1, 5, [M("CritDamagePercent", 6)]),
    T("hm_barrage", "Sperrfeuer", "Mehrfachschuss +5 % Schaden.", 4, 1, 3, [M("DamageDonePercent", 5, ability="hunter_multi_shot")]),
    T("hm_trueshot", "Aura des Volltreffers", "Angriffskraft +40.", 6, 1, 1, [M("AttackPowerFlat", 40)], "hm_mortal_shots"),
])
TREE("hunter_survival", "Hunter", "Überleben", "Fallen, Instinkt und Ausdauer.", "MeleeDps", [
    T("hs_survivalist", "Überlebenskünstler", "Maximales Leben +2 %.", 0, 1, 5, [M("MaxHealthPercent", 2)]),
    T("hs_deflection", "Abwehr", "Parierchance +1 %.", 0, 2, 5, [M("ParryChance", 1)]),
    T("hs_savage_strikes", "Wilde Stöße", "Raptorstoß: kritische Trefferchance +10 %.", 1, 1, 2, [M("CritChance", 10, ability="hunter_raptor_strike")]),
    T("hs_surefooted", "Trittsicherheit", "Trefferchance +1 %.", 2, 1, 3, [M("HitChance", 1)]),
    T("hs_killer_instinct", "Killerinstinkt", "Kritische Trefferchance +1 %.", 3, 1, 3, [M("CritChance", 1)]),
    T("hs_lightning_reflexes", "Blitzartige Reflexe", "Beweglichkeit +3 %.", 4, 1, 5, [M("StatPercent", 3, stat="Agility")]),
    T("hs_wyvern", "Wyverngift", "Verursachter Schaden +5 %.", 6, 1, 1, [M("DamageDonePercent", 5)]),
])
# --- Schurke
TREE("rogue_assassination", "Rogue", "Meucheln", "Gifte und tödliche Finishing-Moves.", "MeleeDps", [
    T("ra_improved_eviscerate", "Verbessertes Ausweiden", "Ausweiden +5 % Schaden.", 0, 0, 3, [M("DamageDonePercent", 5, ability="rogue_eviscerate")]),
    T("ra_malice", "Bosheit", "Kritische Trefferchance +1 %.", 0, 2, 5, [M("CritChance", 1)]),
    T("ra_ruthlessness", "Rücksichtslosigkeit", "Finishing-Moves kosten 5 % weniger.", 1, 1, 3, [M("CostPercent", -5, ability="rogue_eviscerate"), M("CostPercent", -5, ability="rogue_slice_and_dice")]),
    T("ra_lethality", "Tödlichkeit", "Kritischer Schaden +6 %.", 2, 1, 5, [M("CritDamagePercent", 6)], None),
    T("ra_vile_poisons", "Üble Gifte", "Naturschaden +4 %.", 3, 1, 5, [M("DamageDonePercent", 4, school="Nature")]),
    T("ra_murder", "Mord", "Verursachter Schaden +2 %.", 4, 1, 2, [M("DamageDonePercent", 2)]),
    T("ra_cold_blood", "Kaltblütigkeit", "Kritische Trefferchance +5 %.", 6, 1, 1, [M("CritChance", 5)], "ra_lethality"),
])
TREE("rogue_combat", "Rogue", "Kampf", "Fechtkunst und unermüdliche Angriffe.", "MeleeDps", [
    T("rc_improved_ss", "Verbesserter Finsterer Stoß", "Finsterer Stoß kostet 3 Energie weniger (≈7 %).", 0, 0, 2, [M("CostPercent", -7, ability="rogue_sinister_strike")]),
    T("rc_lightning_reflexes", "Blitzartige Reflexe", "Ausweichchance +1 %.", 0, 2, 5, [M("DodgeChance", 1)]),
    T("rc_precision", "Präzision", "Trefferchance +1 %.", 1, 1, 5, [M("HitChance", 1)]),
    T("rc_dual_wield", "Beidhändigkeitsspezialisierung", "Körperlicher Schaden +2 %.", 2, 1, 5, [M("DamageDonePercent", 2, school="Physical")]),
    T("rc_blade_flurry", "Klingenwirbel", "Angriffstempo +4 %.", 3, 1, 5, [M("AttackSpeedPercent", 4)]),
    T("rc_aggression", "Aggression", "Finsterer Stoß und Ausweiden +2 % Schaden.", 4, 1, 3, [M("DamageDonePercent", 2, ability="rogue_sinister_strike"), M("DamageDonePercent", 2, ability="rogue_eviscerate")]),
    T("rc_adrenaline_rush", "Adrenalinrausch", "Schaltet Adrenalinrausch frei.", 6, 1, 1, [], "rc_blade_flurry", "rogue_adrenaline_rush"),
])
TREE("rogue_subtlety", "Rogue", "Täuschung", "Aus den Schatten zuschlagen.", "MeleeDps", [
    T("rsu_master_of_deception", "Meister der Täuschung", "Bewegungstempo +2 %.", 0, 1, 5, [M("MoveSpeedPercent", 2)]),
    T("rsu_opportunity", "Günstige Gelegenheit", "Meucheln und Hinterhalt +4 % Schaden.", 0, 2, 5, [M("DamageDonePercent", 4, ability="rogue_backstab"), M("DamageDonePercent", 4, ability="rogue_ambush")]),
    T("rsu_camouflage", "Tarnung", "Verstohlenheit: Abklingzeit -1 Sekunde.", 1, 1, 5, [M("CooldownFlat", -1, ability="rogue_stealth")]),
    T("rsu_elusiveness", "Flüchtigkeit", "Entrinnen: Abklingzeit -45 Sekunden.", 2, 1, 2, [M("CooldownFlat", -45, ability="rogue_evasion")]),
    T("rsu_serrated", "Gezackte Klingen", "Körperlicher Schaden +2 %.", 3, 1, 3, [M("DamageDonePercent", 2, school="Physical")]),
    T("rsu_dirty_deeds", "Schmutzige Tricks", "Kritische Trefferchance +1 %.", 4, 1, 3, [M("CritChance", 1)]),
    T("rsu_hemorrhage", "Blutsturz", "Schaltet Blutsturz frei.", 6, 1, 1, [], None, "rogue_hemorrhage"),
])
# --- Priester
TREE("priest_discipline", "Priest", "Disziplin", "Willenskraft, Schilde und Manaeffizienz.", "Healer", [
    T("pd_unbreakable_will", "Unbeugsamer Wille", "Erlittener Schaden -1 %.", 0, 1, 5, [M("DamageTakenPercent", -1)]),
    T("pd_wand_spec", "Zauberstabspezialisierung", "Intelligenz +2 %.", 0, 2, 5, [M("StatPercent", 2, stat="Intellect")]),
    T("pd_improved_shield", "Verbessertes Machtwort: Schild", "Heilung +2 % (inkl. Schilde).", 1, 1, 3, [M("HealingDonePercent", 2)]),
    T("pd_meditation", "Meditation", "Manaregeneration +5 %.", 2, 1, 3, [M("PowerRegenPercent", 5)]),
    T("pd_mental_agility", "Geistige Beweglichkeit", "Sofortzauber kosten 2 % weniger.", 3, 1, 5, [M("CostPercent", -2)]),
    T("pd_mental_strength", "Geistige Stärke", "Maximales Mana +2 %.", 4, 1, 5, [M("MaxPowerPercent", 2)]),
    T("pd_power_infusion", "Seele der Macht", "Kritische Zaubertrefferchance +3 %.", 6, 1, 1, [M("CritChance", 3, crit="Spell")]),
])
TREE("priest_holy", "Priest", "Heilig", "Die reinste Form der Heilung.", "Healer", [
    T("pho_healing_focus", "Heilfokus", "Zurückwerfen beim Heilen -14 %.", 0, 0, 2, [M("PushbackResistPercent", 14)]),
    T("pho_holy_spec", "Heilige Spezialisierung", "Kritische Zaubertrefferchance +1 %.", 0, 2, 5, [M("CritChance", 1, crit="Spell")]),
    T("pho_improved_renew", "Verbesserte Erneuerung", "Heilung +2 %.", 1, 1, 3, [M("HealingDonePercent", 2)]),
    T("pho_divine_fury", "Göttlicher Furor", "Göttliche Pein und Heilung: Zauberzeit -0,1 s.", 2, 1, 5, [M("CastTimeFlat", -0.1, ability="priest_smite"), M("CastTimeFlat", -0.1, ability="priest_heal"), M("CastTimeFlat", -0.1, ability="priest_greater_heal")]),
    T("pho_holy_nova", "Heilige Nova", "Schaltet Heilige Nova frei.", 2, 2, 1, [], None, "priest_holy_nova"),
    T("pho_spiritual_healing", "Spirituelle Heilung", "Heilung +2 %.", 4, 1, 5, [M("HealingDonePercent", 2)], "pho_improved_renew"),
    T("pho_spirit_of_redemption", "Geist der Erlösung", "Willenskraft +5 %.", 6, 1, 1, [M("StatPercent", 5, stat="Spirit")]),
])
TREE("priest_shadow", "Priest", "Schatten", "Die dunkle Seite des Glaubens.", "RangedDps", [
    T("ps_spirit_tap", "Willensentzug", "Willenskraft +2 %.", 0, 1, 5, [M("StatPercent", 2, stat="Spirit")]),
    T("ps_blackout", "Blackout", "Schattenschaden +1 %.", 0, 2, 5, [M("DamageDonePercent", 1, school="Shadow")]),
    T("ps_improved_swp", "Verbessertes Schattenwort: Schmerz", "Schattenwort: Schmerz +5 % Schaden.", 1, 1, 2, [M("DamageDonePercent", 5, ability="priest_shadow_word_pain")]),
    T("ps_mind_flay", "Gedankenschinden", "Schaltet Gedankenschinden frei.", 2, 1, 1, [], None, "priest_mind_flay"),
    T("ps_shadow_focus", "Schattenfokus", "Trefferchance mit Schattenzaubern +2 %.", 2, 2, 5, [M("HitChance", 2, school="Shadow")]),
    T("ps_darkness", "Dunkelheit", "Schattenschaden +2 %.", 4, 1, 5, [M("DamageDonePercent", 2, school="Shadow")]),
    T("ps_shadowform", "Schattengestalt", "Schattenschaden +10 %, erlittener körperlicher Schaden -10 %.", 6, 1, 1, [M("DamageDonePercent", 10, school="Shadow"), M("DamageTakenPercent", -10, school="Physical")], "ps_darkness"),
])
# --- Schamane
TREE("shaman_elemental", "Shaman", "Elementar", "Herr über Blitz, Feuer und Erde.", "RangedDps", [
    T("se_convection", "Konvektion", "Schockzauber und Blitzschlag kosten 2 % weniger.", 0, 1, 5, [M("CostPercent", -2)]),
    T("se_concussion", "Erschütterung", "Naturschaden +1 %.", 0, 2, 5, [M("DamageDonePercent", 1, school="Nature")]),
    T("se_call_of_flame", "Ruf der Flamme", "Feuerschaden +5 %.", 1, 1, 3, [M("DamageDonePercent", 5, school="Fire")]),
    T("se_call_of_thunder", "Ruf des Donners", "Kritische Zaubertrefferchance +1 %.", 2, 1, 5, [M("CritChance", 1, crit="Spell")]),
    T("se_lightning_mastery", "Blitzmeisterschaft", "Blitzschlag: Zauberzeit -0,2 s.", 3, 1, 5, [M("CastTimeFlat", -0.2, ability="shaman_lightning_bolt")]),
    T("se_elemental_fury", "Elementarfuror", "Kritischer Schaden +20 %.", 4, 1, 1, [M("CritDamagePercent", 20)], "se_call_of_thunder"),
    T("se_elemental_mastery", "Elementarbeherrschung", "Kritische Zaubertrefferchance +4 %.", 6, 1, 1, [M("CritChance", 4, crit="Spell")]),
])
TREE("shaman_enhancement", "Shaman", "Verstärkung", "Die Elemente in der eigenen Klinge.", "MeleeDps", [
    T("sen_ancestral_knowledge", "Wissen der Ahnen", "Maximales Mana +1 %.", 0, 1, 5, [M("MaxPowerPercent", 1)]),
    T("sen_shield_spec", "Schildspezialisierung", "Blockchance +1 %.", 0, 2, 5, [M("BlockChance", 1)]),
    T("sen_thundering", "Donnernde Stöße", "Kritische Trefferchance +1 %.", 1, 1, 5, [M("CritChance", 1, crit="Melee")]),
    T("sen_toughness", "Zähigkeit", "Rüstung +2 %.", 2, 1, 5, [M("ArmorPercent", 2)]),
    T("sen_flurry", "Schlaghagel", "Angriffstempo +3 %.", 3, 1, 5, [M("AttackSpeedPercent", 3)], "sen_thundering"),
    T("sen_weapon_mastery", "Waffenmeisterschaft", "Verursachter körperlicher Schaden +2 %.", 4, 1, 5, [M("DamageDonePercent", 2, school="Physical")]),
    T("sen_stormstrike", "Sturmschlag", "Schaltet Sturmschlag frei.", 6, 1, 1, [], None, "shaman_stormstrike"),
])
TREE("shaman_restoration", "Shaman", "Wiederherstellung", "Heilende Wasser und Ahnenkraft.", "Healer", [
    T("sr_improved_hw", "Verbesserte Welle der Heilung", "Welle der Heilung: Zauberzeit -0,1 s.", 0, 1, 5, [M("CastTimeFlat", -0.1, ability="shaman_healing_wave")]),
    T("sr_tidal_focus", "Gezeitenfokus", "Heilzauber kosten 1 % weniger.", 0, 2, 5, [M("CostPercent", -1)]),
    T("sr_ancestral_healing", "Heilung der Ahnen", "Erlittener Schaden -1 %.", 1, 1, 3, [M("DamageTakenPercent", -1)]),
    T("sr_tidal_mastery", "Gezeitenmeisterschaft", "Kritische Zaubertrefferchance +1 %.", 2, 1, 5, [M("CritChance", 1, crit="Spell")]),
    T("sr_healing_way", "Weg der Heilung", "Heilung +2 %.", 3, 1, 3, [M("HealingDonePercent", 2)]),
    T("sr_purification", "Läuterung", "Heilung +2 %.", 4, 1, 5, [M("HealingDonePercent", 2)], "sr_healing_way"),
    T("sr_chain_heal", "Kettenheilung", "Schaltet Kettenheilung frei.", 6, 1, 1, [], None, "shaman_chain_heal"),
])
# --- Magier
TREE("mage_arcane", "Mage", "Arkan", "Reine Magie und Manabeherrschung.", "RangedDps", [
    T("ma_arcane_subtlety", "Arkane Feinsinnigkeit", "Bedrohung durch Arkanzauber -10 %.", 0, 0, 2, [M("ThreatPercent", -10, school="Arcane")]),
    T("ma_arcane_focus", "Arkane Konzentration", "Trefferchance mit Arkanzaubern +2 %.", 0, 2, 5, [M("HitChance", 2, school="Arcane")]),
    T("ma_arcane_mind", "Arkaner Verstand", "Maximales Mana +2 %.", 1, 1, 5, [M("MaxPowerPercent", 2)]),
    T("ma_arcane_meditation", "Arkane Meditation", "Manaregeneration +5 %.", 2, 1, 3, [M("PowerRegenPercent", 5)]),
    T("ma_arcane_instability", "Arkane Instabilität", "Verursachter Schaden +1 %, krit. Zaubertrefferchance +1 %.", 4, 1, 3, [M("DamageDonePercent", 1), M("CritChance", 1, crit="Spell")]),
    T("ma_improved_missiles", "Verbesserte Arkane Geschosse", "Arkane Geschosse +5 % Schaden.", 3, 1, 5, [M("DamageDonePercent", 5, ability="mage_arcane_missiles")]),
    T("ma_arcane_power", "Arkane Macht", "Verursachter Schaden +6 %.", 6, 1, 1, [M("DamageDonePercent", 6)], "ma_arcane_instability"),
])
TREE("mage_fire", "Mage", "Feuer", "Alles verzehrende Flammen.", "RangedDps", [
    T("mf_improved_fireball", "Verbesserter Feuerball", "Feuerball: Zauberzeit -0,1 s.", 0, 1, 5, [M("CastTimeFlat", -0.1, ability="mage_fireball")]),
    T("mf_impact", "Einschlag", "Feuerschlag: Abklingzeit -0,5 s.", 0, 2, 5, [M("CooldownFlat", -0.5, ability="mage_fire_blast")]),
    T("mf_ignite", "Entzünden", "Kritischer Schaden mit Feuer +8 %.", 1, 1, 5, [M("CritDamagePercent", 8, school="Fire")]),
    T("mf_pyroblast", "Pyroschlag", "Schaltet Pyroschlag frei.", 2, 1, 1, [], None, "mage_pyroblast"),
    T("mf_critical_mass", "Kritische Masse", "Kritische Trefferchance mit Feuer +2 %.", 4, 1, 3, [M("CritChance", 2, school="Fire")]),
    T("mf_fire_power", "Feuermacht", "Feuerschaden +2 %.", 4, 2, 5, [M("DamageDonePercent", 2, school="Fire")]),
    T("mf_combustion", "Verbrennung", "Kritische Trefferchance mit Feuer +5 %.", 6, 1, 1, [M("CritChance", 5, school="Fire")], "mf_critical_mass"),
])
TREE("mage_frost", "Mage", "Frost", "Eis, Kälte und Kontrolle.", "RangedDps", [
    T("mfr_improved_frostbolt", "Verbesserter Frostblitz", "Frostblitz: Zauberzeit -0,1 s.", 0, 1, 5, [M("CastTimeFlat", -0.1, ability="mage_frostbolt")]),
    T("mfr_elemental_precision", "Elementare Präzision", "Trefferchance mit Feuer und Frost +2 %.", 0, 2, 3, [M("HitChance", 2, school="Frost"), M("HitChance", 2, school="Fire")]),
    T("mfr_ice_shards", "Eissplitter", "Kritischer Schaden mit Frost +20 %.", 1, 1, 5, [M("CritDamagePercent", 20, school="Frost")]),
    T("mfr_frostbite", "Erfrierung", "Kritische Trefferchance mit Frost +1 %.", 1, 2, 3, [M("CritChance", 1, school="Frost")]),
    T("mfr_piercing_ice", "Durchdringendes Eis", "Frostschaden +2 %.", 2, 1, 3, [M("DamageDonePercent", 2, school="Frost")]),
    T("mfr_arctic_reach", "Arktische Reichweite", "Frostnova: Abklingzeit -2 s.", 3, 1, 2, [M("CooldownFlat", -2, ability="mage_frost_nova")]),
    T("mfr_winters_chill", "Winterkälte", "Kritische Zaubertrefferchance +1 %.", 4, 1, 5, [M("CritChance", 1, crit="Spell")], "mfr_piercing_ice"),
    T("mfr_icy_veins", "Eisige Adern", "Schaltet Eisige Adern frei.", 6, 1, 1, [], None, "mage_icy_veins"),
])
# --- Hexenmeister
TREE("warlock_affliction", "Warlock", "Gebrechen", "Flüche und langsamer Verfall.", "RangedDps", [
    T("waf_suppression", "Unterdrückung", "Trefferchance mit Schatten +2 %.", 0, 1, 5, [M("HitChance", 2, school="Shadow")]),
    T("waf_improved_corruption", "Verbesserte Verderbnis", "Verderbnis: Zauberzeit -0,4 s.", 0, 2, 5, [M("CastTimeFlat", -0.4, ability="warlock_corruption")]),
    T("waf_improved_coa", "Verbesserter Fluch der Pein", "Fluch der Pein +2 % Schaden.", 1, 1, 3, [M("DamageDonePercent", 2, ability="warlock_curse_of_agony")]),
    T("waf_improved_life_tap", "Verbesserter Aderlass", "Aderlass +10 % Wirkung.", 1, 2, 2, [M("DamageDonePercent", 10, ability="warlock_life_tap")]),
    T("waf_shadow_mastery", "Schattenbeherrschung", "Schattenschaden +2 %.", 4, 1, 5, [M("DamageDonePercent", 2, school="Shadow")]),
    T("waf_siphon_life", "Lebensentzug", "Schaltet Lebensentzug frei.", 2, 1, 1, [], None, "warlock_siphon_life"),
    T("waf_dark_pact", "Dunkler Pakt", "Manaregeneration +10 %.", 6, 1, 1, [M("PowerRegenPercent", 10)], "waf_shadow_mastery"),
])
TREE("warlock_demonology", "Warlock", "Dämonologie", "Herr über Dämonen und Paktmagie.", "RangedDps", [
    T("wd_improved_imp", "Verbesserter Wichtel", "Feuerschaden +2 %.", 0, 0, 3, [M("DamageDonePercent", 2, school="Fire")]),
    T("wd_demonic_embrace", "Dämonische Umarmung", "Ausdauer +3 %, Willenskraft -1 %.", 0, 2, 5, [M("StatPercent", 3, stat="Stamina"), M("StatPercent", -1, stat="Spirit")]),
    T("wd_improved_demon_skin", "Verbesserte Dämonenhaut", "Rüstung +3 %.", 1, 1, 2, [M("ArmorPercent", 3)]),
    T("wd_fel_intellect", "Teufelsintelligenz", "Maximales Mana +2 %.", 1, 2, 5, [M("MaxPowerPercent", 2)]),
    T("wd_fel_stamina", "Teufelsausdauer", "Maximales Leben +2 %.", 2, 1, 5, [M("MaxHealthPercent", 2)]),
    T("wd_master_demonologist", "Meister der Dämonologie", "Erlittener Schaden -2 %.", 4, 1, 5, [M("DamageTakenPercent", -2)]),
    T("wd_soul_link", "Seelenverbindung", "Erlittener Schaden -5 %.", 6, 1, 1, [M("DamageTakenPercent", -5)], "wd_master_demonologist"),
])
TREE("warlock_destruction", "Warlock", "Zerstörung", "Feuer und Chaos.", "RangedDps", [
    T("wds_improved_sb", "Verbesserter Schattenblitz", "Kritische Zaubertrefferchance +1 %.", 0, 1, 5, [M("CritChance", 1, crit="Spell")]),
    T("wds_bane", "Verderben", "Schattenblitz und Feuerbrand: Zauberzeit -0,1 s.", 0, 2, 5, [M("CastTimeFlat", -0.1, ability="warlock_shadow_bolt"), M("CastTimeFlat", -0.1, ability="warlock_immolate")]),
    T("wds_devastation", "Verwüstung", "Kritische Trefferchance mit Zerstörung +1 %.", 1, 1, 5, [M("CritChance", 1, school="Fire"), M("CritChance", 1, school="Shadow")]),
    T("wds_shadowburn", "Schattenbrand", "Schaltet Schattenbrand frei.", 2, 1, 1, [], None, "warlock_shadowburn"),
    T("wds_improved_immolate", "Verbesserter Feuerbrand", "Feuerbrand +5 % Schaden.", 3, 1, 5, [M("DamageDonePercent", 5, ability="warlock_immolate")]),
    T("wds_emberstorm", "Glutsturm", "Feuerschaden +2 %.", 4, 1, 5, [M("DamageDonePercent", 2, school="Fire")]),
    T("wds_ruin", "Ruin", "Kritischer Schaden +50 %.", 6, 1, 1, [M("CritDamagePercent", 50)], "wds_emberstorm"),
])
# --- Druide
TREE("druid_balance", "Druid", "Gleichgewicht", "Die Macht von Sonne und Mond.", "RangedDps", [
    T("db_improved_wrath", "Verbesserter Zorn", "Zorn: Zauberzeit -0,1 s.", 0, 1, 5, [M("CastTimeFlat", -0.1, ability="druid_wrath")]),
    T("db_natures_grasp", "Griff der Natur", "Naturschaden +1 %.", 0, 2, 4, [M("DamageDonePercent", 1, school="Nature")]),
    T("db_improved_moonfire", "Verbessertes Mondfeuer", "Mondfeuer +5 % Schaden.", 1, 1, 5, [M("DamageDonePercent", 5, ability="druid_moonfire")]),
    T("db_starfire", "Sternenfeuer", "Schaltet Sternenfeuer frei.", 2, 1, 1, [], None, "druid_starfire"),
    T("db_vengeance", "Rache", "Kritischer Zauberschaden +20 %.", 3, 1, 5, [M("CritDamagePercent", 20)]),
    T("db_moonglow", "Mondschein", "Zauber kosten 3 % weniger.", 4, 1, 3, [M("CostPercent", -3)]),
    T("db_moonkin", "Mondkingestalt", "Kritische Zaubertrefferchance +3 %, Rüstung +30 %.", 6, 1, 1, [M("CritChance", 3, crit="Spell"), M("ArmorPercent", 30)], "db_vengeance"),
])
TREE("druid_feral", "Druid", "Wilder Kampf", "Bär und Katze – Tank und Nahkämpfer.", "Tank", [
    T("dfe_ferocity", "Wildheit", "Zermalmen, Prankenhieb und Klaue kosten 5 % weniger.", 0, 1, 5, [M("CostPercent", -5, ability="druid_maul"), M("CostPercent", -5, ability="druid_swipe"), M("CostPercent", -5, ability="druid_claw")]),
    T("dfe_feral_instinct", "Instinkt der Wildnis", "Bedrohung +3 %.", 0, 2, 5, [M("ThreatPercent", 3)]),
    T("dfe_thick_hide", "Dickes Fell", "Rüstung +2 %.", 1, 1, 5, [M("ArmorPercent", 2)]),
    T("dfe_sharpened_claws", "Geschärfte Klauen", "Kritische Trefferchance +2 %.", 2, 1, 3, [M("CritChance", 2, crit="Melee")]),
    T("dfe_savage_fury", "Wilde Wut", "Körperlicher Schaden +5 %.", 3, 1, 2, [M("DamageDonePercent", 5, school="Physical")]),
    T("dfe_heart_of_the_wild", "Herz der Wildnis", "Stärke und Ausdauer +4 %.", 4, 1, 5, [M("StatPercent", 4, stat="Strength"), M("StatPercent", 4, stat="Stamina")]),
    T("dfe_leader_of_the_pack", "Rudelführer", "Kritische Trefferchance +3 %, erlittener Schaden -4 %.", 6, 1, 1, [M("CritChance", 3, crit="Melee"), M("DamageTakenPercent", -4)], "dfe_heart_of_the_wild"),
])
TREE("druid_restoration", "Druid", "Wiederherstellung", "Lebenskraft des Waldes.", "Healer", [
    T("drs_improved_mark", "Verbessertes Mal der Wildnis", "Alle Werte +1 %.", 0, 1, 5, [M("StatPercent", 1, stat="Stamina"), M("StatPercent", 1, stat="Intellect"), M("StatPercent", 1, stat="Spirit")]),
    T("drs_furor", "Furor", "Maximales Mana +1 %.", 0, 2, 5, [M("MaxPowerPercent", 1)]),
    T("drs_improved_ht", "Verbesserte Heilende Berührung", "Heilende Berührung: Zauberzeit -0,1 s.", 1, 1, 5, [M("CastTimeFlat", -0.1, ability="druid_healing_touch")]),
    T("drs_reflection", "Reflexion", "Manaregeneration +5 %.", 2, 1, 3, [M("PowerRegenPercent", 5)]),
    T("drs_improved_rejuv", "Verbesserte Verjüngung", "Heilung +2 %.", 3, 1, 3, [M("HealingDonePercent", 2)]),
    T("drs_gift_of_nature", "Gabe der Natur", "Heilung +2 %.", 4, 1, 5, [M("HealingDonePercent", 2)], "drs_improved_rejuv"),
    T("drs_swiftmend", "Rasche Heilung", "Schaltet Rasche Heilung frei.", 6, 1, 1, [], None, "druid_swiftmend"),
])

# =====================================================================================
# KI-KAMPFPROFILE: Wie Bots einer Klasse/Rolle spielen. Erste passende Regel gewinnt.
# =====================================================================================
def P(cls, role, tree, rotation, buffs=(), heals=(), res=None, taunt=None, interrupt=None, pet=None, ranged=False, form="None"):
    profiles.append({"class": cls, "role": role, "talentTree": tree, "form": form, "buffs": list(buffs), "rotation": rotation,
                     "heals": list(heals), "resurrect": res, "taunt": taunt, "interrupt": interrupt, "pet": pet, "ranged": ranged})

def R(ability, cond="Always", value=0, aura=None):
    d = {"ability": ability, "if": cond, "value": value}
    if aura: d["aura"] = aura
    return d

P("Warrior", "Tank", "warrior_protection", [
    R("warrior_shield_block", "SelfHealthBelow", 0.7), R("warrior_bloodrage", "SelfPowerBelow", 0.2), R("warrior_thunder_clap", "MultipleEnemies", 2),
    R("warrior_demoralizing_shout", "MultipleEnemies", 2), R("warrior_shield_slam"), R("warrior_sunder_armor"), R("warrior_heroic_strike", "SelfPowerAbove", 40)],
  buffs=["warrior_battle_shout"], taunt="warrior_taunt", form="DefensiveStance")
P("Warrior", "MeleeDps", "warrior_arms", [
    R("warrior_execute", "TargetHealthBelow", 0.2), R("warrior_mortal_strike"), R("warrior_rend", "TargetMissingAura", aura="warrior_rend"),
    R("warrior_thunder_clap", "MultipleEnemies", 3), R("warrior_heroic_strike", "SelfPowerAbove", 30)],
  buffs=["warrior_battle_shout"], form="BattleStance")
P("Paladin", "Tank", "paladin_protection", [
    R("paladin_consecration", "MultipleEnemies", 2), R("paladin_judgement"), R("paladin_seal_of_righteousness", "SelfMissingAura", aura="paladin_seal"),
    R("paladin_hammer_of_justice", "TargetCasting")],
  buffs=["paladin_devotion_aura", "paladin_righteous_fury", "paladin_blessing_of_might"], heals=["paladin_flash_of_light", "paladin_holy_light"], res="paladin_redemption")
P("Paladin", "Healer", "paladin_holy", [R("paladin_judgement"), R("paladin_seal_of_righteousness", "SelfMissingAura", aura="paladin_seal")],
  buffs=["paladin_devotion_aura", "paladin_blessing_of_might"], heals=["paladin_holy_shock", "paladin_flash_of_light", "paladin_holy_light"], res="paladin_redemption")
P("Paladin", "MeleeDps", "paladin_retribution", [
    R("paladin_seal_of_righteousness", "SelfMissingAura", aura="paladin_seal"), R("paladin_crusader_strike"), R("paladin_judgement"),
    R("paladin_consecration", "MultipleEnemies", 3), R("paladin_hammer_of_justice", "TargetCasting")],
  buffs=["paladin_devotion_aura", "paladin_blessing_of_might"], heals=["paladin_flash_of_light", "paladin_holy_light"], res="paladin_redemption")
P("Hunter", "RangedDps", "hunter_marksmanship", [
    R("hunter_aspect_of_the_hawk", "SelfMissingAura", aura="hunter_hawk"), R("hunter_raptor_strike", "InMeleeRange"),
    R("hunter_serpent_sting", "TargetMissingAura", aura="hunter_serpent_sting"), R("hunter_aimed_shot"), R("hunter_multi_shot", "MultipleEnemies", 2), R("hunter_arcane_shot")],
  pet="hunter_call_pet", ranged=True)
P("Rogue", "MeleeDps", "rogue_combat", [
    R("rogue_kick", "TargetCasting"), R("rogue_slice_and_dice", "ComboPointsAtLeast", 2), R("rogue_eviscerate", "ComboPointsAtLeast", 5),
    R("rogue_evasion", "SelfHealthBelow", 0.35), R("rogue_sinister_strike")], interrupt="rogue_kick")
P("Priest", "Healer", "priest_holy", [R("priest_shadow_word_pain", "TargetMissingAura", aura="priest_swp"), R("priest_smite")],
  buffs=["priest_power_word_fortitude"], heals=["priest_power_word_shield", "priest_renew", "priest_greater_heal", "priest_heal", "priest_lesser_heal"],
  res="priest_resurrection")
P("Priest", "RangedDps", "priest_shadow", [
    R("priest_power_word_shield", "SelfHealthBelow", 0.5), R("priest_shadow_word_pain", "TargetMissingAura", aura="priest_swp"), R("priest_mind_blast"),
    R("priest_mind_flay"), R("priest_smite")], buffs=["priest_power_word_fortitude"], heals=["priest_renew", "priest_heal", "priest_lesser_heal"], res="priest_resurrection", ranged=True)
P("Shaman", "Healer", "shaman_restoration", [R("shaman_flame_shock", "TargetMissingAura", aura="shaman_flame_shock"), R("shaman_lightning_bolt")],
  buffs=["shaman_strength_of_earth"], heals=["shaman_chain_heal", "shaman_lesser_healing_wave", "shaman_healing_wave"], res="shaman_ancestral_spirit", interrupt="shaman_earth_shock")
P("Shaman", "MeleeDps", "shaman_enhancement", [
    R("shaman_rockbiter_weapon", "SelfMissingAura", aura="shaman_rockbiter"), R("shaman_earth_shock", "TargetCasting"), R("shaman_stormstrike"),
    R("shaman_flame_shock", "TargetMissingAura", aura="shaman_flame_shock"), R("shaman_earth_shock")],
  buffs=["shaman_strength_of_earth"], heals=["shaman_lesser_healing_wave", "shaman_healing_wave"], res="shaman_ancestral_spirit", interrupt="shaman_earth_shock")
P("Shaman", "RangedDps", "shaman_elemental", [
    R("shaman_earth_shock", "TargetCasting"), R("shaman_flame_shock", "TargetMissingAura", aura="shaman_flame_shock"), R("shaman_lightning_bolt")],
  buffs=["shaman_strength_of_earth"], heals=["shaman_lesser_healing_wave", "shaman_healing_wave"], res="shaman_ancestral_spirit", interrupt="shaman_earth_shock", ranged=True)
P("Mage", "RangedDps", "mage_frost", [
    R("mage_frost_armor", "SelfMissingAura", aura="mage_frost_armor"), R("mage_counterspell", "TargetCasting"), R("mage_frost_nova", "InMeleeRange"),
    R("mage_arcane_explosion", "MultipleEnemies", 3), R("mage_fire_blast"), R("mage_frostbolt"), R("mage_fireball")],
  buffs=["mage_arcane_intellect"], interrupt="mage_counterspell", ranged=True)
P("Warlock", "RangedDps", "warlock_affliction", [
    R("warlock_demon_skin", "SelfMissingAura", aura="warlock_demon_skin"), R("warlock_life_tap", "SelfPowerBelow", 0.3),
    R("warlock_corruption", "TargetMissingAura", aura="warlock_corruption"), R("warlock_curse_of_agony", "TargetMissingAura", aura="warlock_coa"),
    R("warlock_immolate", "TargetMissingAura", aura="warlock_immolate"), R("warlock_siphon_life", "TargetMissingAura", aura="warlock_siphon"), R("warlock_shadow_bolt")],
  pet="warlock_summon_imp", ranged=True)
P("Druid", "Tank", "druid_feral", [R("druid_swipe", "MultipleEnemies", 2), R("druid_maul")],
  buffs=["druid_mark_of_the_wild"], heals=["druid_rejuvenation", "druid_healing_touch"], res="druid_rebirth", taunt="druid_growl", form="Bear")
P("Druid", "Healer", "druid_restoration", [R("druid_moonfire", "TargetMissingAura", aura="druid_moonfire"), R("druid_wrath")],
  buffs=["druid_mark_of_the_wild"], heals=["druid_swiftmend", "druid_rejuvenation", "druid_healing_touch"], res="druid_rebirth")
P("Druid", "RangedDps", "druid_balance", [
    R("druid_moonfire", "TargetMissingAura", aura="druid_moonfire"), R("druid_starfire"), R("druid_wrath")],
  buffs=["druid_mark_of_the_wild"], heals=["druid_rejuvenation", "druid_healing_touch"], res="druid_rebirth", ranged=True)
P("Druid", "MeleeDps", "druid_feral", [R("druid_rip", "ComboPointsAtLeast", 4), R("druid_claw")],
  buffs=["druid_mark_of_the_wild"], heals=["druid_rejuvenation", "druid_healing_touch"], res="druid_rebirth", form="Cat")

# =====================================================================================
# Schreiben
# =====================================================================================
def write(rel, data):
    path = os.path.join(ROOT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        f.write("// ERZEUGT von tools/gen_class_content.py – nicht von Hand bearbeiten.\n")
        json.dump(data, f, ensure_ascii=False, indent=1)
        f.write("\n")

def clean(d):
    return {k: v for k, v in d.items() if v is not None}

total_ranks = 0
for cls, lst in abilities.items():
    lst = [clean(a) for a in lst]
    total_ranks += sum(max(1, len(a.get("ranks", []))) for a in lst)
    write(f"abilities/{cls}.json", lst)
for cls, lst in auras.items():
    write(f"auras/{cls}.json", [clean(a) for a in lst])
by_class = {}
for t in TREES:
    by_class.setdefault(t["class"].lower(), []).append(t)
for cls, lst in by_class.items():
    write(f"talents/{cls}.json", lst)
write("bots/profiles.json", [clean(p) for p in profiles])
n_ab = sum(len(v) for v in abilities.values())
n_au = sum(len(v) for v in auras.values())
n_t = sum(len(t["talents"]) for t in TREES)
print(f"{n_ab} Fähigkeiten ({total_ranks} Ränge), {n_au} Auren, {len(TREES)} Talentbäume mit {n_t} Talenten, {len(profiles)} KI-Profile")
