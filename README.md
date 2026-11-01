# AVAWorldOfAngBand

A turn-based dungeon crawl that joins two games: [AVABand](https://github.com/Harlock123/AVABand)'s Angband (its
dungeon generator, its turn system and its 4.2.5 data) and [AVAAshenReliquary](https://github.com/Harlock123/AVAAshenReliquary)'s
hero, rules, art and story. Ten years after the Cinder Throne, the Reliquary under Mount Cindral turns out to be only a
lid: below it a shaft goes down a hundred levels, to where the Order of the Pale Lantern first burned Ashur. You go
down it as one of the Lantern-sworn, with no Ember Heart. When you die, you stay dead.

**Status: milestone 2 of 6 ("Fight").** Levels are generated as Angband 4.2.5 generates them and drawn
isometrically in Flare's art. The hero is a Pathfinder 2e fighter on AshenReliquary's rules, and fights forty of
Angband's monsters from its first ten levels, turn by turn on Angband's energy system. Death is final, and every
death goes into the Order's book. Items, the shops and character creation come next. The plan for all of it is in
[the design specification](docs/SPECIFICATION.md).

![A fight on the first level: a skeleton kobold, a jackal, a giant centipede and a rock lizard round the hero in a
lit hall, a "miss" floating over the hero, the hit points, round and Second Breath at the top right, and the rolls in
the message log](docs/images/WorldOfAngBand-fight.png)

![Gallowmere: grass between the town's stone buildings, a wood of dead trees round it, the Apothecary's sign over its
door, and the hero in mail with a sword and shield](docs/images/WorldOfAngBand-town.png)

## What there is so far

- **The dungeon.** Every level is made by AVABand's port of Angband 4.2.5's generator: its nine cave profiles, 500
  room templates, 162 vaults and 40 monster pits. The same game seed always makes the same game.
- **Isometric, in Flare's art.** AshenReliquary's tiles at their full size. Each wall shows a face toward the floor
  beside it, and lit rooms carry torches on their walls. Doors, stairs, rubble, magma and quartz veins and lava each
  have their own look.
- **Sight as in Angband.** What the hero sees by torchlight or in a lit room is drawn in full. What they have seen
  before is drawn dim. What they have never seen is not drawn at all.
- **Pathfinder's rules, one action at a time.** Each step or Strike is one action and one Angband turn. Three actions
  make a round:
  - the multiple attack penalty counts the Strikes in the round (−5, −10);
  - a fighter's Shield Block comes back each round.
  Every check is a d20 with four degrees of success, and the message log shows the roll: *You hit the kobold
  (14 + 9 = 23 vs AC 15): 9 slashing.*
- **Forty monsters,** from Angband's first ten levels, brought into Pathfinder's terms (the spec's section 6):
  - their level comes from their depth, and their AC, attack and saves from AshenReliquary's creature formulas at
    that level;
  - each Angband blow is a Strike, and its effect (poison, fear, paralysis, blindness, drained strength) comes with a
    save;
  - they think as Angband's do, ported from AVABand: they wake by degrees, hunt by sight, noise and scent, gather in
    packs, surround the hero in the open, lurk while the hero holds a corridor, flee when hurt, open and bash doors,
    and breed.
  - **The art:** a kobold or a cutpurse is Flare's paper-doll body, dressed and tinted. A jackal is Danimal's wolf, and
    ants, centipedes, skeletons and zombies are Flare's creatures. Anything with no sprite yet stands on the floor as
    its DCSS tile.
- **One life.** At 0 HP, Second Breath holds the hero at 1 HP once per trip down; it comes back in town. After that,
  death is final. The hero's line goes into the Order's book of the Lantern-sworn, struck through, and the next one
  starts from Gallowmere.
- **Healing:** three flasks, filled again in town, and rest (`R`) with no monster in sight.
- **The classic views.** `Ctrl+T` turns from the isometric view to AVABand's letters and then its DCSS tiles, seen from
  above.
- **Gallowmere,** the town, by day: stone shops with their names over the doors, and a wood of dead trees round them.
- **A dive bot** that plays the game: in tests it takes a level-1 fighter to depth 5 in most games.

![The first level in the classic tile view: DCSS tiles seen from above, the hero in a corridor with a kobold ahead and a jackal in the room behind](docs/images/WorldOfAngBand-tiles.png)

## Keys

In the isometric view the map is turned forty-five degrees, so keys move the hero the way they point on screen. In the
classic views up is north, as in Angband.

| | |
| --- | --- |
| Arrows, keypad, or `h` `j` `k` `l` `y` `u` `b` `n` | Walk; walk into a monster to strike it |
| `>` / `<` | Go down / up the stairs underfoot |
| `,` or keypad `5` | Stay put a turn |
| `R` | Rest until healed (stops when a monster appears) |
| `q` | Drink from a healing flask |
| Click | Travel to a square you have seen, or strike a monster beside you |
| `Escape` | Stop travelling or resting |
| `Ctrl+T` | Isometric, letters, tiles |
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
  - The wolf is ["Wolf monsters animated"](https://opengameart.org/content/wolf-monsters-animated) (CC-BY-SA 4.0 and
    3.0), by Danimal, from Micket's CC0 mesh with Poss's fur textures (`README-wolf.txt`).
  - The classic tiles, and monsters with no Flare sprite yet, are the
    [Dungeon Crawl Stone Soup tiles](https://opengameart.org/content/dungeon-crawl-32x32-tiles) (CC0), as AVABand ships
    them (`src/WorldOfAngBand.App/tilesets/dcss`).
- **The rules** are Pathfinder 2e's, under the ORC licence, as AshenReliquary's code builds them. The Pathfinder name, setting and trademarks are
  not used.
