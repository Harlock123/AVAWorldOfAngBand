#!/usr/bin/env python3
"""Builds the game's sound pack (src/WorldOfAngBand.App/audio) from the two parent games, as the spec's section 8.1
says: AVABand's music, ambience and Dubtrain's Angband sounds, and AshenReliquary's music and effects.

Run from the repository root, with AVABand and AVAAshenReliquary checked out beside it (or pass their paths):

    python3 tools/audio/make_audio_pack.py [--avaband ../AVABand] [--ashen ../AVAAshenReliquary]

It copies the files unchanged and writes audio/soundpack.json (effects by event name, music by mood, ambience by name,
and the source and licence of every file) and audio/CREDITS.md. Edit this script, not its output.
"""

import argparse
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "src" / "WorldOfAngBand.App" / "audio"

CC0 = "CC0"
CCBY4 = "CC BY 4.0"

# AVABand's dungeon music pack: file -> (title, author, url).
AVABAND_MUSIC = {
    "title_theme.ogg": ("The Pits of Angband", "composed and synthesised for AVABand (tools/title_theme.py)", "https://github.com/Harlock123/AVABand"),
    "boss_theme.ogg": ("The Foe Before You", "composed and synthesised for AVABand (tools/boss_theme.py)", "https://github.com/Harlock123/AVABand"),
    "The_Old_Tower_Inn.mp3": ("Medieval: The Old Tower Inn", "RandomMind", "https://opengameart.org/content/medieval-the-old-tower-inn"),
    "town_theme.mp3": ("Town Theme RPG", "cynicmusic", "https://opengameart.org/content/town-theme-rpg"),
    "dungeon_ambient_1_0.ogg": ("Loopable Dungeon Ambience", "JaggedStone", "https://opengameart.org/content/loopable-dungeon-ambience"),
    "dungeon002_0.ogg": ("Dungeon Ambience", "yd", "https://opengameart.org/content/dungeon-ambience"),
    "dark_theme.ogg": ("Dark Theme", "JaggedStone", "https://opengameart.org/content/dark-theme"),
    "cave_theme.ogg": ("Cave Theme", "HaelDB", "https://opengameart.org/content/cave-theme"),
    "deep_humidity.ogg": ("Deep Humidity", "TinyWorlds", "https://opengameart.org/content/deep-humidity"),
    "rat_sewer.ogg": ("Rat Sewer", "HaelDB", "https://opengameart.org/content/rat-sewer"),
    "within_the_abandoned_castle.ogg": ("Within The Abandoned Castle", "Spring Spring", "https://opengameart.org/content/within-the-abandoned-castle"),
    "dark_cavern_ambient_002.ogg": ("Dark Cavern Ambient", "Paul Wortmann", "https://opengameart.org/content/dark-cavern-ambient"),
    "cold_silence.ogg": ("Cold Silence", "Eponasoft", "https://opengameart.org/content/cold-silence"),
    "eternal_sleep.ogg": ("Eternal Sleep", "The Oracle", "https://opengameart.org/content/eternal-sleep"),
    "evil_temple.ogg": ("Evil temple Ambiance loop", "HaelDB", "https://opengameart.org/content/evil-temple-ambiance-loop"),
    "horror_atmosphere.ogg": ("Post Apocalyptic Wastelands", "Juhani Junkala (SubspaceAudio)", "https://opengameart.org/content/horror-atmosphere"),
}

# AVABand's ambience loops: file -> (title, author, url).
AVABAND_AMBIENCE = {
    "ambient-town-day.ogg": ("AMB Morning Sounds (Perfect Loop)", "Kresiek The Furry", "https://opengameart.org/content/amb-morning-sounds-perfect-loop"),
    "ambient-town-night.ogg": ("Crickets Ambient Noise - loopable", "Wolfgang_", "https://opengameart.org/content/crickets-ambient-noise-loopable"),
    "ambient-dungeon-shallow.ogg": ("Loopable Dungeon Ambience", "JaggedStone", "https://opengameart.org/content/loopable-dungeon-ambience"),
    "ambient-dungeon-deep.ogg": ("underwater or space engine rumble", "gmason", "https://opengameart.org/content/underwater-or-space-engine-rumble"),
    "ambient-dungeon-abyss.ogg": ("Droning Sound Effects", "bretbernhoft", "https://opengameart.org/content/droning-sound-effects"),
    "ambient-labyrinth.ogg": ("wind1", "Luke.RUSTLTD", "https://opengameart.org/content/wind1"),
    **{f"ambient-{name}.ogg": (f"{name.capitalize()} loop", "made for AVABand (tools/ambience_loops.py)", "https://github.com/Harlock123/AVABand")
       for name in ("cavern", "fortress", "moria", "lair", "gauntlet")},
}

# AshenReliquary's effects for what this game doesn't have yet (guns), left out.
ASHEN_LEFT_OUT = {"gunshot", "gunshot-heavy", "reload", "misfire"}

# Music moods (section 8.1): AVABand's by place, AshenReliquary's for the province, fights and great foes.
MOODS = {
    "title": ["avaband/title_theme.ogg"],
    "town": ["avaband/The_Old_Tower_Inn.mp3", "avaband/town_theme.mp3"],
    "dungeon": [f"avaband/{f}" for f in ("dungeon_ambient_1_0.ogg", "dungeon002_0.ogg", "dark_theme.ogg", "cave_theme.ogg",
                                         "deep_humidity.ogg", "rat_sewer.ogg", "within_the_abandoned_castle.ogg")],
    "deep": [f"avaband/{f}" for f in ("dark_cavern_ambient_002.ogg", "cold_silence.ogg", "eternal_sleep.ogg",
                                      "evil_temple.ogg", "horror_atmosphere.ogg")],
    "boss": ["ashen/combat-battle-theme-2.ogg", "avaband/boss_theme.ogg"],
}
# AshenReliquary's own moods, from its audio.json: explore (the province outdoors), combat, and its title (the epilogue).
ASHEN_MOODS = {"explore": "explore", "combat": "combat", "title": "epilogue"}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--avaband", type=Path, default=ROOT.parent / "AVABand")
    parser.add_argument("--ashen", type=Path, default=ROOT.parent / "AVAAshenReliquary")
    args = parser.parse_args()
    avaband = args.avaband / "src" / "Angband.Avalonia"
    ashen = args.ashen / "src" / "AshenReliquary.App" / "Assets" / "Audio"

    if OUT.exists():
        shutil.rmtree(OUT)
    files: list[dict] = []

    def copy(src: Path, rel: str, credit: dict) -> str:
        dest = OUT / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(src, dest)
        files.append({"file": rel, **credit})
        return rel

    # Music.
    for name, (title, author, url) in AVABAND_MUSIC.items():
        copy(avaband / "soundpacks" / "cc0-dungeon-music" / name, f"music/avaband/{name}",
             {"title": title, "author": author, "url": url, "licence": CC0})
    music = {mood: [f"music/{f}" for f in tracks] for mood, tracks in MOODS.items()}
    listing = json.loads((ashen / "audio.json").read_text())
    credits = {f["file"]: f for f in listing["files"]}

    def ashen_credit(rel: str) -> dict:
        c = credits[rel]
        return {"title": c.get("original", ""), "author": c["source"], "url": c["url"], "licence": c["licence"]}

    for group, mood in ASHEN_MOODS.items():
        tracks = []
        for rel in listing["music"][group]:
            name = Path(rel).name
            if not (OUT / "music" / "ashen" / name).exists():
                copy(ashen / rel, f"music/ashen/{name}", ashen_credit(rel))
            tracks.append(f"music/ashen/{name}")
        music[mood] = tracks
    for rel in listing["music"]["boss"]:
        name = Path(rel).name
        if not (OUT / "music" / "ashen" / name).exists():
            copy(ashen / rel, f"music/ashen/{name}", ashen_credit(rel))

    # Ambience: AVABand's loops. AshenReliquary's road loop is JaggedStone's dungeon loop again (re-encoded), so it is
    # not copied twice; the province's grassland plays the morning loop instead.
    ambience = {}
    for name, (title, author, url) in AVABAND_AMBIENCE.items():
        rel = copy(avaband / "ambience" / name, f"ambience/{name}", {"title": title, "author": author, "url": url, "licence": CC0})
        ambience[name.removeprefix("ambient-").removesuffix(".ogg")] = [rel]

    # Effects: Dubtrain's Angband sounds under Angband's message names, AshenReliquary's under its own (lower case).
    dub = json.loads((avaband / "soundpacks" / "angband-dubtrain" / "soundpack.json").read_text())
    sounds: dict[str, list[str]] = {}
    for event, names in dub["sounds"].items():
        rels = []
        for name in names:
            rel = f"effects/angband/{name}"
            if not (OUT / rel).exists():
                copy(avaband / "soundpacks" / "angband-dubtrain" / name, rel,
                     {"title": name, "author": "Dubtrain", "url": dub["source"], "licence": CCBY4})
            rels.append(rel)
        sounds[event] = rels
    for group, rels in listing["effects"].items():
        if group in ASHEN_LEFT_OUT:
            continue
        out = []
        for rel in rels:
            name = Path(rel).name
            dest = f"effects/ashen/{name}"
            if not (OUT / dest).exists():
                copy(ashen / rel, dest, ashen_credit(rel))
            out.append(dest)
        sounds[group] = out
    volumes = {k: v for k, v in dub.get("volumes", {}).items()}

    pack = {
        "name": "AVAWorldOfAngBand",
        "author": "AVABand and AVAAshenReliquary's sources, listed per file below",
        "license": "Per file: CC0, CC BY 3.0 or CC BY 4.0",
        "source": "tools/audio/make_audio_pack.py",
        "sounds": dict(sorted(sounds.items())),
        "music": music,
        "ambience": dict(sorted(ambience.items())),
        "volumes": volumes,
        "files": sorted(files, key=lambda f: f["file"]),
    }
    (OUT / "soundpack.json").write_text(json.dumps(pack, indent=1, ensure_ascii=False) + "\n")
    for licence in (avaband / "soundpacks" / "angband-dubtrain" / "LICENSE.txt",):
        shutil.copyfile(licence, OUT / "effects" / "angband" / "LICENSE.txt")
    write_credits(files)
    total = sum((OUT / f["file"]).stat().st_size for f in files)
    print(f"{len(files)} files, {total / 1e6:.1f} MB: {len(music)} moods, {len(ambience)} ambience loops, {len(sounds)} effects")


def write_credits(files: list[dict]) -> None:
    lines = ["# Sound credits", "",
             "Every file in this folder, where it came from and its licence. Made by `tools/audio/make_audio_pack.py`.",
             "The files are as their parent games ship them: AVABand's (music, ambience and Dubtrain's Angband sounds)",
             "and AVAAshenReliquary's (music and effects). CC BY licences ask for the credit given here.", ""]
    by_source: dict[tuple[str, str, str], list[str]] = {}
    for f in files:
        key = (f["author"], f["licence"], f["url"])
        by_source.setdefault(key, []).append(f["file"] + (f" (\"{f['title']}\")" if f["title"] and f["title"] != Path(f["file"]).name else ""))
    for (author, licence, url), names in sorted(by_source.items()):
        lines.append(f"## {author}")
        lines.append(f"- Licence: {licence}")
        lines.append(f"- Source: {url}")
        for name in names:
            lines.append(f"- `{name}`")
        lines.append("")
    (OUT / "CREDITS.md").write_text("\n".join(lines))


if __name__ == "__main__":
    main()
