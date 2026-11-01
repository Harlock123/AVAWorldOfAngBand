# AVAWorldOfAngBand

A turn-based dungeon crawl that joins two games: [AVABand](https://github.com/Harlock123/AVABand)'s Angband (its
dungeon generator, its turn system and its 4.2.5 data) and [AVAAshenReliquary](https://github.com/Harlock123/AVAAshenReliquary)'s
hero, rules, art and story. Ten years after the Cinder Throne, the Reliquary under Mount Cindral turns out to be only a
lid: below it a shaft goes down a hundred levels, to where the Order of the Pale Lantern first burned Ashur. You go
down it as one of the Lantern-sworn, with no Ember Heart. When you die, you stay dead.

**Status: milestone 1 of 6 ("Walk the deep").** Levels are generated as Angband 4.2.5 generates them and drawn
isometrically in Flare's art. The hero walks them turn by turn on Angband's energy system, sees by torchlight, opens
doors, takes the stairs, and starts in the town of Gallowmere. There are no monsters, items or character building
yet; the plan for all of it is in [the design specification](docs/SPECIFICATION.md).

![Gallowmere: grass between the town's stone buildings, a wood of dead trees round it, the Apothecary's sign over its
door, and the hero in leathers with a longsword](docs/images/WorldOfAngBand-town.png)

![The first level down: a lit room of flagstones in Flare's dungeon set, walls showing their faces toward the floor, the
hero at the room's edge with a torch, and the halls already seen dim and blue in the dark beyond](docs/images/WorldOfAngBand-lid.png)

## What there is so far

- **The dungeon.** Every level is made by AVABand's port of Angband 4.2.5's generator: its nine cave profiles, 500
  room templates, 162 vaults and 40 monster pits. The same game seed always makes the same levels.
- **Isometric, in Flare's art.** AshenReliquary's tiles at their full size. Each wall shows a face toward the floor
  beside it, and lit rooms carry torches on their walls. Doors, stairs, rubble, magma and quartz veins and lava each
  have their own look.
- **Sight as in Angband.** What the hero sees by torchlight or in a lit room is drawn in full. What they have seen
  before is drawn dim. What they have never seen is not drawn at all.
- **Turns as in Angband.** Each step is one turn on AVABand's energy scheduler. Walking into a wall costs no time,
  and walking into a closed door opens it.
- **Gallowmere,** the town, by day: stone shops with their names over the doors, and a wood of dead trees round
  them.

## Keys

The map is turned forty-five degrees, so keys move the hero the way they point on screen.

| | |
| --- | --- |
| Arrows, keypad, or `h` `j` `k` `l` `y` `u` `b` `n` | Walk |
| `>` / `<` | Go down / up the stairs underfoot |
| `,` or keypad `5` | Stay put a turn |
| Click | Travel to a square you have seen |
| `Escape` | Stop travelling |
| Mouse wheel | Zoom |

`--seed N` on the command line plays game N again.

## Download

Builds for Windows (x64, x86, ARM64), macOS (Intel and Apple Silicon) and Linux (x64, ARM64 and 32-bit ARM) are on the
[releases page](../../releases): `latest` is the newest build of `main`. Unzip or untar it and run `AVAWorldOfAngBand`
(`AVAWorldOfAngBand.exe` on Windows). Each is a single self-contained file.

## Building

```bash
dotnet build AVAWorldOfAngBand.slnx
dotnet test AVAWorldOfAngBand.slnx
dotnet run --project src/WorldOfAngBand.App
```

.NET 10 (see `global.json`). `WORLDOFANGBAND_SHOTS=docs/images dotnet test` redraws the pictures above. See
[AGENTS.md](AGENTS.md) for how the code is put together.

## Licences and credits

- **The code** is GPL-2 ([LICENSE](LICENSE)), because AVABand's data and the code it ported from Angband are.
  AshenReliquary's own code is MIT, which may be included in a GPL-2 work.
- **The game data** in `src/WorldOfAngBand.Data/data` comes from Angband 4.2.5, by Ben Harrison, Robert Ruehlmann,
  Andi Sidwell, Nick McConnell and the Angband team (GPL-2), as AVABand converted it.
- **The art** is from [Flare](https://flarerpg.org) (CC-BY-SA 3.0), by Clint Bellanger and the Flare artists. See
  `src/WorldOfAngBand.App/Assets/Flare/LICENSE.txt` and `CREDITS.txt`.
- **The rules** to come are Pathfinder 2e's, under the ORC licence. The Pathfinder name, setting and trademarks are
  not used.
