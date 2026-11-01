#!/usr/bin/env python3
"""Makes AshenReliquary's creatures into this game's monsters (the spec's section 7.4, milestone M7b).

Reads data/province/creatures.json (AshenReliquary's Content/Data/creatures.json, copied unchanged) and writes:
  data/province/monsters.json          each creature as an Angband-format monster race, id "ashen_<id>", flagged
                                       PROVINCE so Angband's spawner never picks it; its numbers in play come from
                                       creatures.json and the pack's level and rank (Game/Province/AshenCreatures.cs);
  data/province/bridge/monsters.json   its bridge entry: its Flare sprite, or the paper doll in its look.

Run from the repository root: python3 tools/province/make_province_monsters.py
"""
import json
import os

ROOT = os.path.join(os.path.dirname(__file__), "..", "..", "src", "WorldOfAngBand.Data", "data", "province")

# Each sprite family's Angband look: its glyph and base.
SPRITES = {
    "zombie": ("z", "zombie"), "skeleton": ("s", "skeleton"), "skeleton_weak": ("s", "skeleton"),
    "skeleton_archer": ("s", "skeleton"), "skeleton_mage": ("s", "skeleton"), "skeleton_knight_boss": ("s", "skeleton"),
    "avatar": ("p", "person"), "wolf": ("C", "canine"), "fire_ant": ("a", "ant"), "ice_ant": ("a", "ant"),
    "antlion": ("I", "insect"), "antlion_small": ("I", "insect"), "wyvern_fire": ("d", "dragon"),
    "minotaur": ("H", "hybrid"), "cursed_grave": ("G", "ghost"),
}

ELEMENTS = {"Fire": "IM_FIRE", "Cold": "IM_COLD", "Electricity": "IM_ELEC", "Acid": "IM_ACID", "Poison": "IM_POIS"}

COLOURS = {"Fire": "Red", "Cold": "LightBlue", "Electricity": "Blue", "Poison": "Green", "Void": "Purple",
           "Mental": "Violet", "Acid": "LightGreen"}


def race(c):
    glyph, base = SPRITES.get(c["sprite"], ("p", "person"))
    flags = ["PROVINCE", "EVIL"] if c["sprite"] != "wolf" else ["PROVINCE", "ANIMAL"]
    if c.get("undead"):
        flags += ["UNDEAD", "NO_FEAR", "COLD_BLOOD"]
    for immunity in c.get("immunities", []):
        if immunity in ELEMENTS:
            flags.append(ELEMENTS[immunity])
    if c.get("stationary"):
        flags.append("NEVER_MOVE")
    if c.get("unseen"):
        flags.append("INVISIBLE")
    if c.get("named") or c.get("boss"):
        flags.append("UNIQUE")
    method = {"zombie": "hit", "wolf": "bite", "fire_ant": "bite", "ice_ant": "bite", "antlion": "bite",
              "antlion_small": "bite", "wyvern_fire": "bite", "minotaur": "butt"}.get(c["sprite"], "hit")
    return {
        "id": "ashen_" + c["id"],
        "name": c["name"].lower() if not (c.get("named") or c.get("boss")) else c["name"],
        "glyph": glyph,
        "color": COLOURS.get(c.get("damageType"), "Slate" if c.get("undead") else "Umber"),
        "base": base,
        "depth": 1,
        "rarity": 1,
        "speed": 0,
        "hitPoints": 10,
        "armour": 10,
        "sleep": 10,
        "hearing": int(c.get("aggroRadius", 8)) + 6,
        "smell": 10,
        "experience": 0,
        # The numbers in play come from creatures.json; this blow is only Angband's shape for it.
        "blows": [{"method": method, "effect": "hurt", "damage": c.get("damage") or "1d6"}],
        "flags": sorted(set(flags)),
        "description": f"One of AshenReliquary's own: {c['name']}, ten years on.",
    }


def bridge(c):
    entry = {"id": "ashen_" + c["id"]}
    if c["sprite"] == "avatar":
        entry["sprite"] = "doll"
        entry["doll"] = c.get("look") or {"chest": "cloth_shirt", "legs": "cloth_pants"}
    else:
        entry["sprite"] = c["sprite"]
    if c.get("scale"):
        entry["scale"] = c["scale"]
    if c.get("tint"):
        entry["tint"] = True
    return entry


def main():
    with open(os.path.join(ROOT, "creatures.json"), encoding="utf-8") as f:
        creatures = json.load(f)
    with open(os.path.join(ROOT, "monsters.json"), "w", encoding="utf-8") as f:
        json.dump([race(c) for c in creatures], f, indent=1, ensure_ascii=False)
        f.write("\n")
    with open(os.path.join(ROOT, "bridge", "monsters.json"), "w", encoding="utf-8") as f:
        json.dump({"_comment": "Made by tools/province/make_province_monsters.py from creatures.json; edit the script, not this file.",
                   "monsters": [bridge(c) for c in creatures]}, f, indent=1, ensure_ascii=False)
        f.write("\n")


if __name__ == "__main__":
    main()
