# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` says what the game is; `docs/SPECIFICATION.md`
is the design and the milestones; this says how the code is put together and what keeps it working.

## Commands

```bash
dotnet build AVAWorldOfAngBand.slnx
dotnet test AVAWorldOfAngBand.slnx            # headless; the window tests run on Avalonia's headless platform
dotnet run --project src/WorldOfAngBand.App   # the game (--seed N plays game N)
WORLDOFANGBAND_SHOTS=docs/images dotnet test AVAWorldOfAngBand.slnx   # also redraws the README's pictures
```

## Layout

The code is **copied** from the two parent games, not linked, so neither has to keep an API stable for this one.

- `src/WorldOfAngBand.Engine/`: AVABand's `Angband.Core` without its game session (`Game/`), `Persistence/`,
  `Records/` and `Quests/`. It has level generation (`Generation/`), sight (`Sight/`), the energy scheduler (`Time/`),
  the level and its squares (`World/`), and the monster, item and magic definitions and helpers that those use. It has
  no packages and no UI.
- `src/WorldOfAngBand.Data/`: AVABand's loader with mods by id, and Angband 4.2.5's data as AVABand converted it
  (`data/*.json`). The loader's checks of effect wording are left out until effects are ported.
- `src/WorldOfAngBand.Rules/`: AshenReliquary's Core without its world:
  - `Rules/`: checks and degrees of success, typed bonuses, proficiency;
  - `Combat/`: conditions, damage, persistent damage;
  - `Characters/`, `Items/`, `Abilities/`, `Progression/`;
  - `Content/`: ancestries, classes, feats and items, embedded.
  Changes from AshenReliquary: Hero Points are Second Breath (`Combat/Survival.cs`), and persistent damage ticks once
  a round. Its clock is in seconds; the game runs it at half a second per action (`DungeonSession.SecondsPerGameTurn`).
- `src/WorldOfAngBand.Game/`: the new game.
  - `DungeonSession`: an `ITurnHandler` on the scheduler. `GameCommand` records go in through `Perform`, and
    `IGameEvent` records come out through `Published`. It owns the levels, the foes, sight, noise and scent, death and
    experience. The other work goes to helpers:
    - `Fight`: Strikes, blows, Shield Block and blow effects;
    - `MonsterBrain`: AVABand's port of mon-move.c, trimmed;
    - `Bridge/CreatureBridge`: Angband monsters in Pathfinder terms, from `data/bridge/monsters.json` (made by
      `tools/bridge/make_monster_bridge.py`; edit the script, not the file): level by depth, hit points from
      AshenReliquary's `CreatureStats` nudged by Angband's, the depth scale and Pathfinder caps for their damage, and
      their speed;
    - `MonsterSpells`: Angband's 91 monster spells in Pathfinder terms;
    - `Elements`: Angband's 25 elements as damage types and riders;
    - `Bridge/ItemBridge`: Angband items in Pathfinder terms (weapons, armour, shields, what potions and scrolls do,
      the egos in the game), from `data/bridge/items.json`;
    - `Gear`: what the worn Angband items come to in Pathfinder's terms, set on the character whenever they change;
    - `Belongings`: picking up, dropping, wielding and learning runes;
    - `Loot`: floor objects and monster drops;
    - `ItemEffects`: what potions and scrolls do;
    - `Stores`: the shops, the home and the Smith's runework.
  - `Powers`: the class abilities on the action system (AshenReliquary's ability kinds), with the class resource,
    cooldowns on the game clock, wards, auras, hiding and traps. `Powers.IsPlayable` says which classes the game can
    play; `LevelPicks` suggests a level's feats.
  - `Birth`: what the player chose (the hero, the rumour or a Legacy, randarts, a challenge). `Challenges`: the daily
    and weekly seeds, heroes and twists. `Activations`: an artifact's activation as the game's effects or a one-off
    ability.
  - `Round`: Pathfinder's three-action round, with the multiple attack penalty and the reaction.
  - `Hero`: the character, their Angband pack and equipment, gold and timed effects.
  - `Foe`: an engine `Monster` with its Pathfinder numbers and conditions.
  - `Story`: the story levels, rumours, altar choices, Wren's lines and the epilogue (the spec's section 2). The
    session places the story (`PlaceStory`, `Altar`, `Choose`, `StoryKill`); the rooms are
    `data/setting/templates/ava_vaults.json` and the story's own monsters `data/setting/ava_monsters.json`.
  - `Bots/DiveBot`: plays the game, for balance tests. `Wary` dives as a player does; `Hunt` goes for a quest level's
    keeper.
  - `Bots/Campaign`: makes a hero ready for a depth and runs the dive bot from there, or through the finale.
  - `Dressing/LevelDressing`: turns a level's terrain into Flare tiles. The wall faces follow AshenReliquary's map
    scripts.
  - `Province/`: AshenReliquary's zones above the shaft (the spec's section 7.4). `ProvinceMap` loads
    `data/province/zones/*.json`; `ZoneBuilder` makes a zone a level and its dressing, as AshenReliquary's TileMap does
    (its ground table, per-cell hash, shores and lava). The session travels through gates (`EnterZone`, `Wayfare`).
  - `Flare/`: AshenReliquary's parsers for Flare's tileset and animation files.
  - `Setting`: names in the Ashen setting, from `data/setting/ashen_names.json` and each mod's own (`Setting.UseMods`,
    set by the session from the data's mod folders), and the words random artifacts are named from.
- `src/WorldOfAngBand.App/`: Avalonia, built in code with no XAML.
  - `Rendering/ViewState`: what the views show besides the game, followed from its events: messages, slides,
    swings, floating numbers, the slain.
  - `Rendering/IsoView`: the isometric view, using AshenReliquary's projection, sprite cache, depth sorting and
    darkness. Monsters are drawn by their bridge entry: a Flare creature, the paper doll, or a DCSS card.
  - `Rendering/ClassicView`: AVABand's letters and tiles from above.
  - `Rendering/HudPainter`: the HUD, the death screen and the epilogue, over either view.
  - `Services/Graveyard`: the book of the dead (`graveyard.json`); `Services/ChallengeBoard` the challenge tries
    (`challenges.json`); `Services/AppSettings` the mods switched off (`settings.json`); `Services/AshenLegacy` reads
    AVAAshenReliquary's saves for a Legacy, and never writes them.
  - `Windows/InventoryWindow`, `Windows/StoreWindow` and `Windows/StoryWindow`: the pack, the shops and a story
    level's altar. `Windows/BirthWindow`, `LevelUpWindow`, `ChallengeWindow` and `ModsWindow`: a new hero, a level's
    feats, the challenges, and the mods.
  - `Rendering/ItemArt`: Flare's loot sprites (or DCSS tiles) for things on the floor.
  - `MainWindow`: keys, clicks, travel and rest.
  - `Assets/Flare` holds the embedded art; `tilesets/dcss` (AVABand's DCSS tiles) ships beside the executable.
- `tests/WorldOfAngBand.Tests/` (the engine and the game) and `tests/WorldOfAngBand.App.Tests/` (the window, headless,
  with real Skia drawing). `BalanceTests` runs the campaign bots and takes about a minute.

## Rules that keep it working

- **Deterministic.** Everything random comes from the game seed through `GameRandom`, so a seed and its commands make
  the same game. Level seeds are derived from the game seed with `GameRandom.DeriveSeed`. Dressing hashes the level
  seed and the square, so a level always looks the same.
- **The session stays headless.** Nothing in Engine, Data or Game touches Avalonia. The view only reads the session
  and turns input into commands.
- **The session is split by concern** as it grows (combat, monster brains, spells, loot, knowledge, stores, quests,
  story), never one class across a hundred partial files. Both parent games ended up that way; this one mustn't.
- **The 4.2.5 data stays as AVABand converted it.** What this game changes (names, PF2 translations) goes in its own
  files beside it.
- **No Tolkien names on screen.** The setting renames them (`data/setting/ashen_names.json`, the spec's section 3), and
  `BridgeCheckTests` fails on any name it shows that matches its list of Tolkien's names. Add a rename there, never
  an exception to the test.
- **The bridge check** (`BridgeCheckTests`): every monster, spell and element is bridged; the test output reports
  sprite coverage.
- **The view is changed under `IsoView.Gate`**, since Avalonia draws on its own thread.
- **Items stay Angband's.** The hero carries engine `Item`s in an engine `Inventory`; Pathfinder's numbers are derived
  (`Gear`), never stored. Only bridged kinds and egos are made (`ObjectFactory.AllowedKind`, `AllowedEgo`); artifacts
  and curses are off until they come in.
- **Monsters come in through the bridge.** The spawner only places races in `data/bridge/monsters.json`. Bring one in
  by adding its entry, with a sprite family if Flare has one; otherwise it stands as its DCSS tile.
- **Balance is measured, not guessed.** `FightTests` and `BalanceTests` run the bots over fixed seeds. If a change
  moves those numbers, look at why before changing the test: run `Campaign.Dive` and `Campaign.Finale` over more seeds
  to see the spread.
- **The quest levels hold.** Saint Ysmere's level (99) and the Cinder's (100) have no way down until their keeper is
  gone, and nothing takes the hero past them (`DungeonSession.NoDeeperThan`). A keeper never teleports away or sends
  the hero off its level, and keeps its vigil without escorts. A unique left alive on a level the hero leaves can turn
  up again, as in Angband.
- **Content files keep their layout.** `Rules/Content/Data/classes.json` and `data/setting/ashen_names.json` are laid out
  by hand; add to them without rewriting the rest (a script's `json.dump` reflows the whole file).
- **AshenReliquary's zones stay as it wrote them.** `data/province/zones/` is a copy of AshenReliquary's
  `Content/Data/zones` (its mapgen scripts make them; don't hand-edit). What this game changes goes beside them.
  `ProvinceTests` checks every cell is in its legend and every tile is in its tileset.
- **Mods layer, never replace.** A mod's `bridge/*.json` and `ashen_names.json` go over the shipped ones by id
  (`CreatureBridge.Load`, `ItemBridge.Load`, `Setting`), as its data files do (`DataLoader`).
- **Warnings are errors** (`TreatWarningsAsErrors`), nullable reference types are on, and every change comes with
  tests.

## CI and releases

`.github/workflows/build.yml` builds and tests every push, then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. A push to `main` refreshes the rolling `latest`
pre-release; a `v*` tag makes a proper release. The release step only runs on `main`.
