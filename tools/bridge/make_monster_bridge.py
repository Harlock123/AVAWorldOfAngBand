#!/usr/bin/env python3
"""Writes src/WorldOfAngBand.Data/data/bridge/monsters.json: every Angband 4.2.5 monster (and AVABand's own, and the
story's Herald echoes from data/setting), each with
how it is drawn (the spec's section 3.4):

  1. a Flare sprite family by its monster base, tinted by its Angband colour where the family looks right tinted,
     scaled by what its name says it is (baby, giant, greater...);
  2. the paper-doll body, dressed by what it is (a warrior, a mage, a priest, an archer, a rogue), for people;
  3. nothing, for the paper fallback: its DCSS tile standing on the floor.

Seasonal monsters stay out. Run it from anywhere: python3 tools/bridge/make_monster_bridge.py
"""
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
DATA = ROOT / "src/WorldOfAngBand.Data/data"


def load(name):
    d = json.load(open(DATA / name, encoding="utf-8"))
    return d if isinstance(d, list) else d[list(d)[0]]


WARRIOR = {"head": "chain_coif", "chest": "chain_cuirass", "legs": "chain_greaves", "feet": "chain_boots", "main": "longsword", "off": "shield"}
KNIGHT = {"head": "plate_helm", "chest": "plate_cuirass", "legs": "plate_greaves", "feet": "plate_boots", "hands": "plate_gauntlets", "main": "greatsword"}
MAGE = {"head": "mage_hood", "chest": "mage_vest", "legs": "mage_skirt", "feet": "mage_boots", "hands": "mage_sleeves", "main": "staff"}
PRIEST = {"head": "mage_hood_alt2", "chest": "mage_vest_alt2", "legs": "mage_skirt_alt2", "feet": "mage_boots_alt2", "hands": "mage_sleeves_alt2", "main": "mace"}
ARCHER = {"head": "leather_hood", "chest": "leather_chest", "legs": "leather_pants", "feet": "leather_boots", "hands": "leather_gloves", "off": "longbow"}
ROGUE = {"head": "leather_hood", "chest": "leather_chest", "legs": "cloth_pants", "feet": "cloth_sandals", "main": "dagger"}
PEASANT = {"chest": "cloth_shirt", "legs": "cloth_pants", "feet": "cloth_sandals"}
BRUTE = {"chest": "leather_chest", "legs": "leather_pants", "main": "maul"}
GOBLIN = {"chest": "leather_chest", "legs": "cloth_pants", "main": "club"}

MAGIC = {"BO_", "BA_", "BLINK", "TPORT", "CONF", "SCARE", "BLIND", "HOLD", "SLOW", "MISSILE", "S_", "HEAL", "WOUND", "DARKNESS", "MIND_BLAST", "BRAIN_SMASH", "TELE"}


def outfit(m):
    name = m["name"].lower()
    spells = m.get("spells") or []
    if any(w in name for w in ("priest", "acolyte", "cleric", "shaman", "druid", "templar")):
        return PRIEST
    if any(w in name for w in ("mage", "sorcer", "wizard", "witch", "illusionist", "necromancer", "warlock", "conjurer", "enchant", "demonologist", "apprentice", "lich")):
        return MAGE
    if any(w in name for w in ("archer", "scout", "ranger", "hunter")) or "ARROW" in spells:
        return ARCHER
    if any(w in name for w in ("cutpurse", "thief", "rogue", "urchin", "burglar", "assassin", "bandit", "ruffian", "mugger")):
        return ROGUE
    if any(w in name for w in ("knight", "paladin", "champion", "lord", "captain", "chief", "king", "berserker", "blackguard")):
        return KNIGHT
    if any(s.startswith(p) for s in spells for p in MAGIC) and "UNIQUE" not in m.get("flags", []):
        return MAGE
    return WARRIOR


def scale(m, base_scale):
    name = m["name"].lower()
    s = base_scale
    for word, f in (("baby", 0.6), ("young", 0.8), ("small", 0.75), ("lesser", 0.85), ("giant", 1.2), ("great", 1.2),
                    ("greater", 1.25), ("ancient", 1.2), ("mature", 1.0), ("large", 1.1), ("elder", 1.15), ("king", 1.1),
                    ("queen", 1.2), ("emperor", 1.25), ("lord", 1.1), ("titan", 1.3), ("colossus", 1.4)):
        if word in name:
            s *= f
    if "UNIQUE" in m.get("flags", []):
        s *= 1.1
    return round(s, 2)


# Base -> (sprite family, scale, tint). Bases not listed take the paper fallback.
FAMILIES = {
    "canine": ("wolf", 0.85, True), "feline": ("wolf", 0.75, True), "zephyr_hound": ("wolf", 0.8, True),
    "ant": ("fire_ant", 0.7, True), "killer_beetle": ("antlion", 0.7, True), "centipede": ("antlion_small", 0.75, True),
    "spider": ("antlion_small", 0.6, True), "insect": ("antlion_small", 0.35, True),
    "skeleton": ("skeleton", 0.95, False), "zombie": ("zombie", 0.95, True),
    "wraith": ("skeleton_mage", 1.0, True), "lich": ("skeleton_mage", 1.1, True),
    "dragon": ("wyvern_fire", 0.55, True), "ancient_dragon": ("wyvern_fire", 1.0, True), "hydra": ("wyvern_fire", 0.75, True),
    "major_demon": ("minotaur", 1.2, True), "golem": ("skeleton_knight_boss", 1.0, True), "quylthulg": ("cursed_grave", 0.9, True),
    "yeti": ("minotaur", 0.9, True),
    "person": ("doll", 1.0, False), "humanoid": ("doll", 0.85, False), "townsfolk": ("doll", 0.95, False),
    "kobold": ("doll", 0.8, True), "yeek": ("doll", 0.6, True), "orc": ("doll", 0.95, True), "troll": ("doll", 1.35, True),
    "ogre": ("doll", 1.3, True), "giant": ("doll", 1.8, True), "minor_demon": ("doll", 0.65, True),
    "ghost": ("doll", 1.0, True), "vampire": ("doll", 1.0, True), "ainu": ("doll", 1.15, True), "morgoth": ("doll", 2.0, True),
}
OUTFIT_BY_BASE = {"kobold": GOBLIN, "yeek": PEASANT, "orc": WARRIOR, "troll": BRUTE, "ogre": BRUTE, "giant": BRUTE,
                  "minor_demon": {}, "ghost": MAGE, "vampire": MAGE, "ainu": PRIEST, "morgoth": KNIGHT, "townsfolk": PEASANT}
SKELETONS = {"skeleton_kobold": "skeleton_weak", "skeleton_orc": "skeleton", "skeleton_human": "skeleton"}


def entry(m):
    e = {"id": m["id"]}
    base = m["base"]
    if base in ("hybrid",) and ("minotaur" in m["name"].lower() or "baphomet" in m["id"]):
        e.update(sprite="minotaur", scale=scale(m, 1.0))
        return e
    if base not in FAMILIES:
        return e
    family, s, tint = FAMILIES[base]
    if base == "skeleton":
        family = SKELETONS.get(m["id"], "skeleton_knight_boss" if "UNIQUE" in m.get("flags", []) or "troll" in m["id"] or "etten" in m["id"] else
                               None if "druj" in m["id"] else "skeleton")
        if family is None:
            return e
    if base == "ant" and m.get("color") in ("White", "LightBlue", "Blue", "LightSlate", "Slate"):
        family = "ice_ant"
    e["sprite"] = family
    e["scale"] = scale(m, s)
    if tint:
        e["tint"] = True
    if family == "doll":
        e["doll"] = outfit(m) if base == "orc" else OUTFIT_BY_BASE[base] if base in OUTFIT_BY_BASE else outfit(m)
        if not e["doll"]:
            del e["doll"]
    if base in ("ghost",):
        e["ghost"] = True
    return e


# Hand-picked: the story's Herald echoes (data/setting/ava_monsters.json).
PICKED = {
    "rot_mother": {"sprite": "doll", "scale": 1.05, "tint": True, "doll": MAGE},
    "hollow_choir": {"sprite": "doll", "scale": 1.1, "tint": True, "ghost": True, "doll": PRIEST},
    "unburied_commander": {"sprite": "skeleton_knight_boss", "scale": 1.25, "tint": True},
}


def picked(m):
    return {"id": m["id"], **PICKED[m["id"]]} if m["id"] in PICKED else entry(m)


monsters = [m for m in load("monsters.json") + load("ava_monsters.json") + load("setting/ava_monsters.json")]
out = {
    "_comment": "Generated by tools/bridge/make_monster_bridge.py; edit the script, not this file. How each Angband 4.2.5 monster is brought into the game (the spec's sections 3.4 and 6.1). sprite: a Flare sprite family, 'doll' for the paper-doll body (dressed in 'doll', Flare's avatar layers by slot), or absent for the paper fallback (a DCSS tile standing on the floor). tint: colour the sprite by the monster's Angband colour. ghost: drawn half seen. Names in the Ashen setting are data/setting/ashen_names.json's.",
    "monsters": [picked(m) for m in monsters],
}
json.dump(out, open(DATA / "bridge/monsters.json", "w"), indent=1)
families = {}
for e in out["monsters"]:
    families[e.get("sprite", "(paper)")] = families.get(e.get("sprite", "(paper)"), 0) + 1
print(f"{len(out['monsters'])} monsters:", ", ".join(f"{k} {v}" for k, v in sorted(families.items(), key=lambda kv: -kv[1])))
