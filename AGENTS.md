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
- `src/WorldOfAngBand.Game/`: the new game.
  - `DungeonSession`: an `ITurnHandler` on the scheduler. `GameCommand` records go in through `Perform`, and
    `IGameEvent` records come out through `Published`.
  - `Dressing/LevelDressing`: turns a level's terrain into Flare tiles. The wall faces follow AshenReliquary's map
    scripts.
  - `Flare/`: AshenReliquary's parsers for Flare's tileset and animation files.
  - `Setting`: names in the Ashen setting.
- `src/WorldOfAngBand.App/`: Avalonia, built in code with no XAML.
  - `IsoView`: draws the session, using AshenReliquary's projection, sprite cache, depth sorting and darkness.
  - `MainWindow`: keys and clicks.
  - `Assets/Flare`: the art, embedded as resources.
- `tests/WorldOfAngBand.Tests/` (the engine and the game) and `tests/WorldOfAngBand.App.Tests/` (the window, headless,
  with real Skia drawing).

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
- **No Tolkien names on screen.** The setting renames them (see the spec's section 3).
- **The view is changed under `IsoView.Gate`**, since Avalonia draws on its own thread.
- **Warnings are errors** (`TreatWarningsAsErrors`), nullable reference types are on, and every change comes with
  tests.

## CI and releases

`.github/workflows/build.yml` builds and tests every push, then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. A push to `main` refreshes the rolling `latest`
pre-release; a `v*` tag makes a proper release. The release step only runs on `main`.
