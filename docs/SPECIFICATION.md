# AVAWorldOfAngBand: design specification (draft 2)

Drafted 2026-11-01 from a read of both source games: AVAAshenReliquary (82 commits, ~45.6k lines) and AVABand (335
commits, ~60k lines of engine, data and app).

**Decisions taken by the author:**
- turn-based, as in AVABand;
- Tolkien's names renamed to fit the Ashen setting;
- permadeath, as in AVABand;
- the name AVAWorldOfAngBand;
- the story carries on from Ashen Reliquary;
- Second Breath in place of Hero Points;
- the classic ASCII and tile views kept beside the isometric view.

## 1. The pitch

**AVABand's game, played with AshenReliquary's hero, rules, look and story.**

Years after the Cinder Throne, the Reliquary under Mount Cindral has been found to be only a lid. Below it, a
shaft goes down a hundred levels to the place where the Order of the Pale Lantern first burned Ashur.

You go down it as a new hero built on Pathfinder 2e rules. You have no Ember Heart, so **when you die, you stay
dead**. Every level is newly generated, as Angband 4.2.5 generates its levels:
- rooms, vaults, pits, caverns, labyrinths and lairs;
- 600-odd monsters that hunt you by sight, by noise and by scent.

The game is **turn-based**: AVABand's energy system and its one-key-one-action pace. Each action is resolved with
Pathfinder's d20, degrees of success, typed bonuses and conditions, and drawn isometrically in Flare's art as in
AshenReliquary. Loot follows Angband's rules, and you learn its runes by using them. At the bottom waits what is
left of a god.

### What each game gives

| From AVAAshenReliquary | From AVABand |
| --- | --- |
| Character building on PF2e rules: 6 ancestries, 9 backgrounds, 7 classes, 261 feats, level 1–20 | The turn loop: the energy table, the scheduler, commands in and events out, replays |
| PF2 rules: degrees of success, typed bonuses, proficiency, conditions, persistent damage, saves | Level generation ported from 4.2.5: 9 cave profiles, 500 room templates, 162 vaults, 40 pits |
| Runes (potency, striking, resilient), affixes, rarity, sets, crafting, ItemJudge comparisons | 623 monsters, 56 monster bases, 91 monster spells, blows and their effects, group AI |
| The setting: Vardmoor, the Taint, the Order, Ashur, Fort Gallowmere, its people and its endings | Noise and scent maps, field-of-view shadowcasting, light and map memory |
| The isometric Skia renderer, the sprite cache, the Flare parsers, lighting, the HUD orbs | 405 objects, 107 egos, 138 artifacts, 27 curses, and rune identification |
| Dialogue with skill checks, the journal, world flags | Permadeath and what comes with it: graveyard, death recap, museum, scores |
| OpenAL audio (both games already share this layer) | The town, 8 stores, AVABand's extras, birth options, soak bots, mods, the 4.2.5 check |

### Design pillars

1. **Every descent is new.** Only the town and the story levels are hand-made. The rest is Angband's generator.
2. **One life.** Death is final, so every risk is a choice. The game makes the odds visible so that death is the
   player's fault, never the interface's.
3. **The build is the long game.** You choose feats and runes over twenty levels and five mythic tiers.
4. **Learn by using.** You learn unknown runes, flavours and monster recall by playing, as in Angband 4.2.
5. **Deterministic underneath.** The seed and your commands reproduce the whole game, which gives replays, bots and
   tests.

## 2. The story: after the Cinder Throne

### 2.1 Where Ashen Reliquary left off

Ashen Reliquary has four endings:
- Ashes to Ashes: the Heart is destroyed;
- The Lantern Rekindled: Ashur is bound in the hero;
- The New Prophet: the Throne is claimed;
- The Lantern-Bird: Malachar takes the Heart into the fire.

All four put the god's fire out, or seem to. AVAWorldOfAngBand does not pick one of them. **Each new character
begins with a rumour of how it ended**, drawn at birth: Gallowmere tells the story four ways. The rumour changes:
- a few lines of dialogue in town;
- which Herald's remnant returns at depth 98 (section 2.3);
- the last line of the epilogue.

(An optional **Legacy** birth choice lets the player pick the ending instead, and a later milestone can read an
AVAAshenReliquary save for it.)

### 2.2 The new story

**Premise.** Ten years on, Vardmoor is mending. Then the refugees' wells at Gallowmere turn grey again. Lantern
scholars digging under the broken Throne find that the Reliquary's floor is a seal. Beneath it is the **Pyre**: a
shaft cut by the first Order, nine hundred years ago, down to where they burned Ashur the first time.

The Taint never came from the bones above. They were only what the Order could carry away. The god's **cinder**
(what was too hot to move) is still at the bottom. It is waking, and it is dreaming its servants back up the shaft,
level by level.

**The player** is one of the **Lantern-sworn**: volunteers the rekindled Order sends into the Pyre. Fort Gallowmere,
now walled and grown into a town, is where they gear up. No Ember Heart, no second life. The Order keeps a book
with every Lantern-sworn's name, and **a line is drawn through each one that does not come back**. That book is the
graveyard.

**The arc.** It runs in five *strata*, each with an Ashen Reliquary echo and a story level (section 7.3):

| Stratum | Depths | What is down there | Story beats |
| --- | --- | --- | --- |
| The Lid | 1–20 | The Order's old works: ossuaries, barracks, prisons | Remnants of Mother Sallow's plague cult; the first hint that someone else is going down ahead of you |
| The Drowned Choir | 21–40 | Flooded catacombs, monastery foundations | The Choir of Whispers' surviving novices; the Library of Ash's missing chapters |
| The Iron Dead | 41–60 | Ruins of a city the Order buried | Ser Hadric's oath-knights, unburied again; learn what the first Order did |
| The Cinder Deeps | 61–80 | Lava, ash and the drakes Vorthrax came from | The Cinderwyrm's brood; the Herald of Flame's mother |
| The Pyre | 81–100 | The god's dream given rooms | The first Order's last High Lector at 99; the Cinder of Ashur at 100 |

**The end.**
- At depth 99 waits **Saint Ysmere the Unburnt**, the first Order's High Lector. Nine hundred years ago she lit the
  first pyre, and she has kept it burning since by feeding it herself. She takes Sauron's place, and like him she
  must die before the way down opens (AVABand's quest rule).
- At depth 100 is **the Cinder of Ashur**, the god's last coal, in Morgoth's place.

Killing it wins the game. The epilogue comes from AshenReliquary's epilogue system and depends on:
- the rumour (section 2.1);
- the story quests done (section 7.3);
- the character's ancestry and class.

A winner can retire, as in AVABand, or keep going to depth 127, the "embers" past the end.

### 2.3 Echoes of the Heralds

Each of AshenReliquary's four Heralds has a **remnant** in the Pyre, as a unique:
- Sallow's Rot-Mother at about 18;
- the Unburied Commander at about 45;
- the Hollow Choir at about 33;
- the Cinderwyrm's dam at about 72.

The rumour drawn at birth puts **one of them at depth 98** at full power as an extra guard. It is the Herald whose
Cinder Relic, by that version of the story, was never destroyed.

## 3. Renaming Angband for the Ashen setting

All renaming is **data**: `data/setting/ashen_names.json` maps each 4.2.5 id to a new name, description and (for uniques)
a few lines of lore. The game mechanics do not change. The 4.2.5 data in `data/angband/` stays exactly as AVABand
has it, so the drift check still passes. The bridge check (section 9) fails if a Tolkien name would appear in game:
it uses a list of Tolkien proper nouns and checks every string shown to the player.

**What gets renamed:**
- all 101 uniques;
- the Tolkien-specific monster kinds: Nazgûl, Balrogs, Easterlings, Black Númenóreans, Uruks, Mûmakil, Ents and
  Huorns, Eagles of Manwë;
- all 138 artifacts, and the Tolkien-named egos;
- the races (as heritages, section 5.1);
- Morgoth, Sauron and the places named in the text.

Generic fantasy kinds (orc, troll, kobold, dragon, giant, ghoul) keep their names. They suit the setting and are
not Tolkien's.

**Samples** (the full list is a deliverable of milestone M4):

| 4.2.5 | AVAWorldOfAngBand | Note |
| --- | --- | --- |
| Grip and Fang, Farmer Maggot's Dogs | Ash and Ember, the Ferryman's Hounds | AshenReliquary's *Brothers of the Ferry* |
| Farmer Maggot | Old Corwen, the Gallowmere Gravedigger | Harmless, wanders the town, drops mushrooms |
| Wormtongue, Agent of Saruman | Lector Grysse, Voice of the Prophet | One of Malachar's surviving Inquisitors |
| Bullroarer the Hobbit | Tamsin Barrowbell | A halfling grave-robber |
| Grishnákh, Golfimbul, Shagrat, Gorbag, Bolg, Azog | The Ashen War-band (six orc chiefs) | Taint-fed orcs from Act 4's wastes |
| Mîm and his sons | Old Gath and his sons | Dwarf tomb-keepers who sold the Order the shaft |
| Bert, Bill and Tom the Stone Trolls | The Three Wardens of the Lid | |
| The Nine (Nazgûl), Uvatha … Khamûl | The Nine Unburied | Ser Hadric's oath-knights; the Herald of Iron's echo |
| The Balrog of Moria | The Cinder-Born | A flame demon of the Pyre |
| Smaug, Glaurung, Scatha, Itangast | The Cinderwyrm's dam and brood | The Herald of Flame's line |
| Shelob, Ungoliant | Mother Hollow, the Unlight | Spiders of the dark that eats lamplight |
| Saruman of Many Colours | Malachar's Shadow | A Taint-made copy of the Prophet; its dialogue depends on the rumour |
| The Mouth of Sauron | The Voice of the Pyre | Saint Ysmere's herald |
| Sauron, the Sorcerer (99) | Saint Ysmere the Unburnt | |
| Morgoth, Lord of Darkness (100) | The Cinder of Ashur | |
| Phial of Galadriel | The Lantern of Saint Oriel | |
| Ringil, Andúril, Narthanc … | Blades named for the Order's saints and AshenReliquary's people | Same powers and activations |
| *of Westernesse* | *of the Pale Lantern* | Slay orc, troll and undead, free action, see invisible |
| Angband (the dungeon) | The Pyre | |

## 4. Rules: Pathfinder 2e, one action at a time

### 4.1 Time: AVABand's turn loop

The game runs on AVABand's `Time/TurnScheduler` and `EnergyTable`, unchanged:
- every actor builds up energy at its speed;
- the most energetic actor acts first;
- the game waits whenever it is the player's turn.

There is no real time and no pause; nothing moves until you do.

**The action economy is Pathfinder's.** One PF2 action costs **100 energy**, which is one Angband player turn at
normal speed. So:
- **Moving one square, a Strike, Interact or Step** costs one action (100 energy).
- **Two-action and three-action abilities** (most spells, Power Attack, Sudden Charge) cost 200 or 300 energy.
  Monsters with the same speed get those turns in between, so a slow spell is a real risk.
- **Free actions** (drop an item, speak) cost nothing.
- **Reactions** (Shield Block, Attack of Opportunity, Nimble Dodge) happen in the other actor's turn. You get one
  per *round*, as below.

**Rounds.** A round is your last **three actions**. The HUD shows three pips that fill as you act. Rounds keep PF2's
rules that depend on them:
- the **multiple attack penalty** counts the Strikes within your current round (−5/−10, −4/−8 when agile), as
  AshenReliquary's `AttackCadence` counts by time now;
- your reaction refreshes at the start of each round;
- conditions that last "1 round" or "until the end of your next turn" count your rounds;
- conditions on monsters count *their* rounds.

**Speed** keeps Angband's meaning. +10 speed gives an actor twice the energy, so twice the actions:
- PF2's **Quickened** is +10 speed;
- **Slowed 1** is −10;
- Angband's speed items and potions add as they do in AVABand.

### 4.2 Combat

- **Strikes:** d20 + attack bonus against AC, with four degrees (critical, hit, miss, critical miss) and natural
  20/1 stepping, from AshenReliquary's `Check` and `Strike`.
- **Damage:** weapon dice from striking runes, plus attribute and bonuses. Resistances, weaknesses and persistent
  damage come from AshenReliquary's `Combat` code.
- **Flanking:** an ally or summon on the far side makes a monster off-guard. Monsters flank you the same way, and
  AVABand's surround AI makes this matter in the open.
- **Cover:** a pillar, rubble or a monster between you and the attacker gives +2 AC against ranged attacks,
  computed from AVABand's `ProjectionPath`.
- **Saves:** basic saves (Fortitude, Reflex, Will) against breaths, bolts and balls, with four degrees (double,
  full, half, none).
- **The message log shows every roll**, as Angband's message line does: *You hit the cinder ghoul (14 + 11 = 25 vs
  AC 21). 9 slashing.* A setting can hide the numbers.
- **Hero Points** are taken out: with permadeath, a reroll is too strong to give in every fight. In their place, every
  character gets **one Second Breath**: at 0 HP, once per descent, you stay at 1 HP. It refreshes when you go back
  to town.

### 4.3 Levels and depth

| Depth | Feet | Creature levels | Player expects |
| --- | --- | --- | --- |
| Town | 0 | 0–1 | 1 |
| 1–10 | 50–500 | 1–3 | 1–5 |
| 11–30 | 550–1500 | 3–8 | 5–10 |
| 31–60 | 1550–3000 | 8–12 | 10–16 |
| 61–90 | 3050–4500 | 12–16 | 16–20 |
| 91–100 | 4550–5000 | 16–20 | 20, plus mythic tiers 1–5 |
| 101–127 | to 6350 | 20–24 | after the win |

- **Creature level** comes from a fitted curve of Angband depth, tuned by the balance bots. From depth 30 the deep's
  monsters stand a few levels below the hero who reaches them, since an Angband level throws far more of them at one
  hero than a Pathfinder encounter does; uniques fight two levels up, and kills are worth three times Pathfinder's
  experience.
- **XP** uses PF2's table by level difference, so diving deep pays off as it does in Angband.
- **Mythic tiers 21–25** come after level 20. They are AshenReliquary's planned milestone M6, built here first.

### 4.4 Death

As in AVABand:
- **One life.** On death the save is closed for good.
- **The death recap**: what killed you, the last messages, and your equipment.
- **The graveyard**, drawn as the Order's book with your line struck through.
- **Scores and the museum.**

Birth options from AVABand carry over, all off by default and all scored lower:
- **Second chance:** one extra life, AVABand's option;
- **Persistent levels**, **connected stairs**, **force descend**, **no recall**.

AVABand's daily and weekly challenges carry over with fixed seeds.

**Shrines** stay, but not as respawn points. A shrine (Ashur's old waystones in the walls) can be used once. You
choose one blessing from three, drawn by Ashur's whim: heal, cleanse a condition, learn a rune, or a temporary
boon. This is AVABand's `birth_ava_shrines` dressed as AshenReliquary's shrines.

## 5. The hero

### 5.1 Creation

AshenReliquary's screens and order: ancestry → heritage → background → class → free boosts → skills, with
portraits. Then AVABand's birth options and the **rumour** (section 2.1).

- **Ancestries:** AshenReliquary's 6 (human, elf, dwarf, halfling, orc and the Ashborn), plus the **Kobold** from
  Angband.
- **Angband's races become heritages.** Their racial abilities come from AVABand's `ava_races.json`:

| Angband race | Becomes |
| --- | --- |
| Dúnadan | Human heritage *Lantern-blooded*: sustains Con, +1 to saves against the Taint |
| High-Elf | Elf heritage *Elder-eyed*: sees invisible |
| Half-Troll | Orc heritage *Trollblood*: regenerates, slow digestion |
| Gnome | Halfling heritage *Tinker*: free action |
| Half-Elf, Half-Orc, Hobbit, Dwarf, Human, Elf | Merged into the ancestry of the same kind |

- **Classes:** AshenReliquary's 7 (Fighter, Champion, Rogue, Ranger, Wizard, Necromancer, Gunslinger). Angband's
  Priest, Druid and Blackguard come in M6 as new PF2-style classes.
- **Backgrounds:** AshenReliquary's 9, plus three for the sequel: *Lantern Novice*, *Gallowmere Born* (grew up in
  the refugee camp) and *Pyre Digger*.

### 5.2 Progression

- PF2 levels 1–20 through AshenReliquary's `LevelProgression` and its 261 feats, then mythic tiers.
- **New feats for diving:** Tunneller (dig faster), Light Sleeper (wakes when noise reaches you), Stair-sense (feel
  the level at once), Deep Delver (resist drain).
- **Angband's level feeling** becomes a Perception check on arrival. The degree of success decides how much of the
  danger feeling and the treasure feeling you learn.

### 5.3 Things Angband players will look for

| Angband | In AVAWorldOfAngBand |
| --- | --- |
| Speed | Energy, unchanged; speed runes and items are rare and precious |
| Free action | Immunity to Paralyzed, and to Slowed from blows; a rune |
| See invisible | A sense; without it, invisible creatures are Hidden (DC 11 flat check to target) |
| Resistances (base 4, high 8+) | PF2 resistance that scales with item level; only the highest of a type applies |
| Hold life, experience drain | The Drained condition, cured at the temple or by potion |
| Word of Recall | The Lantern Rift scroll: to town, and back to your deepest depth |
| Digging | Digging takes actions: granite needs many, magma and quartz fewer; a digger's potency rune helps |
| Stealth | Your Stealth DC against each monster's Perception; noise feeds AVABand's noise map |
| Disturb, run, auto-explore | AVABand's, unchanged |

## 6. The bridge: translating Angband's content into PF2

The bridge is all **data**, made by `tools/bridge` from AVABand's 4.2.5 JSON and checked on every build. The 4.2.5
data itself stays as AVABand has it.

### 6.1 Monsters → creatures

| Angband field | Becomes |
| --- | --- |
| depth, rarity | creature level (section 4.3); spawn weight |
| speed | speed, unchanged |
| hit points, armour | HP and AC from AshenReliquary's `CreatureStats.For(level, rank)`, nudged ± by how Angband's numbers rank within that depth |
| unique, escorts, groups | rank: Unique → Boss; groups → packs with a leader |
| blows (up to 4: method, effect, dice) | one **melee action** that makes each blow as a Strike, with the multiple attack penalty from the second blow (agile, −4/−8) |
| spells (91), frequency `1_IN_x` | abilities; the frequency becomes the chance per turn that it casts, as in AVABand, so no cooldowns |
| breaths | cones and lines; basic Reflex save; damage from the monster's current HP, as in Angband |
| invisible, pass wall, kill wall, multiply | PF2 traits and movement modes |
| resistances, vulnerabilities, immunities | resistance, weakness and immunity values by level |
| sleep, alertness, hearing, smell | starting awareness, Perception, how far noise and scent reach |

**Blow effects to conditions** (a sample):

| Angband | PF2 |
| --- | --- |
| POISON | persistent poison damage |
| PARALYZE | Paralyzed for 1 round on a failed Fortitude save; Free Action makes you immune |
| CONFUSE | Confused |
| TERRIFY | Frightened 1–2 |
| BLIND | Blinded |
| LOSE_STR and the other stat drains | Enfeebled, Clumsy, Stupefied (cured at the temple) |
| EAT_GOLD, EAT_ITEM | steals, then the thief teleports |
| EXP_10 … EXP_80 | Drained 1–4 |
| DISENCHANT | strips a rune's potency until it is re-etched |

### 6.2 Elements → damage types

| Angband | PF2 |
| --- | --- |
| acid, fire, cold, electricity, poison | the same |
| light, dark | vitality, void |
| nether | void, plus Drained |
| sound | sonic, plus Stunned |
| shards | slashing, plus bleed |
| nexus | force, plus teleport |
| chaos | untyped, plus Confused |
| disenchantment | force, plus disenchant (section 6.1) |
| time | untyped, plus Drained |
| gravity | bludgeoning, plus Slowed and knockback |
| inertia | Slowed |
| force, plasma, water, ice | force; fire and electricity; bludgeoning; cold and piercing |
| mana, holy orb | force; spirit |

### 6.3 Objects, egos and artifacts → items

- **Bases** map to AshenReliquary item types by dice and weight. Angband's bows and slings are bows and slings;
  guns stay the Gunslinger's, as AVA-only items.
- **Enchantments.** Angband's to-hit and to-dam become **potency** and **striking** runes, capped by item level,
  through AshenReliquary's `Runework`. Armour's to-a becomes potency on AC.
- **Egos** become **property runes**. Each item has one rune slot per point of potency.
- **Artifacts** are **Unique** items with a fixed set of runes, renamed lore (section 3) and their activation as an
  ability. Randarts (a birth option) use AVABand's generator over the same rune pool.
- **Curses** (27) become **cursed runes**, revealed in use, removed by the artificer or by crafting.
- **Consumables:**
  - potions, scrolls, wands, rods and staves keep their Angband roles;
  - charges and recharging follow AVABand;
  - healing scales to PF2 HP.
- **Identification** follows AVABand's `PlayerKnowledge`: runes and flavours are unknown until noticed.
  AshenReliquary's ItemJudge shows "?" where an unknown rune's bonus would be.

### 6.4 Magic

- AshenReliquary's casters keep their ability lists, with heightening by rank. Spells cost 1–3 actions (section 4.1).
- **Angband's magic books** become **grimoires** found and sold in the Archive. Each grants abilities to any class
  of the matching tradition:
  - arcane for the Wizard;
  - necromantic for the Necromancer;
  - divine for the Champion;
  - nature for the Ranger.
- The 144 Angband spells are mapped or merged into roughly 60 new abilities.
- AVABand's grimoire art is shown on the books.

## 7. The world

### 7.1 Monsters in motion

The AI is AVABand's, unchanged in its turn logic:
- **Senses:** monsters notice you by sight, noise and scent; noise comes from what you do.
- **Groups:** ambush in corridors, surround in the open, flee when hurt, hide.
- **Breeders** breed as they do in Angband.
- **The 91 spells** are all used, as AVABand uses them now.

AshenReliquary adds two things:
- **pack leaders** that buff the rest of the pack;
- the **Herald echoes'** boss phases, as turn-based scripts.

### 7.2 The town: Gallowmere, ten years on

The town is generated once per character by AVABand's town generator and dressed as a walled town grown out of the
refugee camp. Its day and night cycle come from AVABand.

| Angband store | In Gallowmere | Also |
| --- | --- | --- |
| General store | Quartermaster | Food, light, flasks, spikes |
| Armoury | Armourer | |
| Weaponsmith | Smith | AshenReliquary's **runework**: etch, move and remove runes |
| Alchemist | Apothecary (Ysolde the herbalist, from Act 1) | Potions and scrolls |
| Magic shop | Curio dealer | Wands, rods, amulets, rings |
| Bookseller (AVABand) | The Lantern Archive | Grimoires |
| Temple | The Lantern Chapel | Cures drain and curses |
| Black market | The Hanged Man | Steep prices, rare goods |
| Home | Your room at the inn | The stash |

AVABand's inn, artificer, caravans, strongroom and town growth stay. The town grows as Lantern-sworn come back with
treasure.

### 7.3 Story levels and quests

- **Quests:** AVABand's quest boards and bounties, posted by Gallowmere's people.
- **Dialogue:** AshenReliquary's engine, with skill checks and world flags.
- **Story levels:** one per stratum, made in AshenReliquary's ASCII-and-legend zone format. The generator places
  each one as a vault on its level:

| Depth | Story level | Choice |
| --- | --- | --- |
| 15 | The Rot-Mother's Font | Seal the font or take its water (a cure, or a poison weapon) |
| 33 | The Drowned Library | Read the Library of Ash's last chapters: the truth about Saint Ysmere |
| 45 | The Oath Hall | Free the Nine Unburied's commander from his oath, or fight him |
| 72 | The Brood Nest | Kill the Cinderwyrm's dam, or let her pass (she fights Saint Ysmere with you) |
| 99 | The First Pyre | Saint Ysmere: kill her, or (with the Library read) talk her into lowering the shaft's last seal |

The choices are world flags, which the epilogue reads. **Malachar's daughter Wren**, grown up, is the Lantern
Archive's keeper. She knows the most about the Pyre and gives the story quests.

## 8. The screen

- **The world:** AshenReliquary's isometric WorldView, at its tile size of 192×96, one Angband square to a tile.
  Turn-based movement is drawn as a short slide between squares. Monster turns are drawn quickly, all together,
  except when one is in view and attacking.
- **The dressing pass** maps the terrain Angband's generator makes (granite, magma, quartz, doors, rubble, lava,
  stairs, shop entrances, traps) to Flare tile ids, with a theme per stratum:

| Stratum | Theme | Tiles |
| --- | --- | --- |
| Town | Gallowmere | Flare grassland and town |
| The Lid | Ossuaries, prisons | Flare dungeon |
| The Drowned Choir | Flooded catacombs | Flare cave, with water banks |
| The Iron Dead | Buried city | rubberduck's Old Ruins |
| The Cinder Deeps | Ash and lava | Act 4's cinder set |
| The Pyre | The god's dream | Flare dungeon, with Act 4's colour grade |

- **Monster art.** 623 monsters, about 60 Flare creature sprites, so:
  1. **Sprite families** by monster base, with each monster's Angband colour as a tint, its size and level as its
     scale, and an aura for its elements.
  2. **Hand-picked sprites** for every unique and every monster below depth 60, as art turns up.
  3. **A paper fallback**: a flat card from AVABand's tilesets (Gervais or DCSS), standing on the isometric floor
     and lit by its light. There is also a "Tiles of Old" option to show the whole bestiary that way.

  The bridge check reports how many monsters fall into each layer.
- **Classic view:** AVABand's top-down ASCII and tile views stay, behind Ctrl+T, for players who want them. The
  game state is the same; only the drawing changes.
- **The HUD:**
  - AshenReliquary's orbs, action bar, belt and conditions row;
  - the three round pips (section 4.1);
  - depth and feet, the level feeling and speed;
  - AVABand's message log.
- **Panels:** AshenReliquary's inventory, character sheet, level-up and journal; AVABand's knowledge browser, map
  (M) and monster recall.
- **Input:** AVABand's rebindable keys, Angband keymaps, gamepad, click-to-travel and auto-explore. Clicking a
  monster attacks it; Shift-click looks.

## 9. Architecture

The new repo **copies** code from both games rather than linking to them. Both parents keep changing, and neither
should have to keep a shared API stable.

```
AVAWorldOfAngBand.slnx
src/
  WorldOfAngBand.Engine/   from AVABand Angband.Core, with no packages:
                             Randomness/ Time/ Sight/ Geometry/ World/ Generation/
                             Monsters/ (noise, scent, AI)   Combat/ProjectionPath   Persistence/ (saves, replays)
  WorldOfAngBand.Rules/    from AshenReliquary Core: Rules/ Combat/ Characters/ Abilities/ Items/ Progression/
  WorldOfAngBand.Game/     new: the session (below)
  WorldOfAngBand.Data/     AVABand's loader with mods by id, plus
                             data/angband/  the 4.2.5 data, unchanged
                             data/pf2/      AshenReliquary's ancestries, classes, feats, abilities, items
                             data/bridge/   section 6
                             data/setting/  names, lore, story levels, dialogue
  WorldOfAngBand.Audio/    the shared OpenAL layer, with AVABand's SoundDirector
  WorldOfAngBand.Input/    from AVABand
  WorldOfAngBand.App/      Avalonia: AshenReliquary's WorldView, SpriteCache, HUD and panels;
                             AVABand's knowledge, birth and graveyard windows and its classic map views
tools/  bridge/ (Python)   balance/ (bots)   AssetTool/
tests/  Engine  Rules  Game  App (headless)
```

**The session** (`WorldOfAngBand.Game`) is the one big new piece.
- It is AVABand's `GameSession` design: an `ITurnHandler` on the scheduler, `GameCommand` records in, `IGameEvent`
  records out, deterministic and replayable.
- Its rules come from `WorldOfAngBand.Rules` in place of Angband's `CombatMath`.
- It is **split by concern from the start** (`Combat`, `MonsterBrain`, `Spells`, `Loot`, `Knowledge`, `Stores`,
  `Quests`, `Story`), not one class in a hundred partial files. AGENTS.md makes that a rule.

**Licence.** The code is GPL-2: AVABand's data and the code ported from Angband require it, and AshenReliquary's
MIT code can go into a GPL-2 work. The art and audio keep their own licences (Flare's art is CC-BY-SA 3.0; the rest
is listed per file in `assets.manifest.json`). PF2 rules are used under the ORC licence, credited in game, without
the Pathfinder name.

## 10. Milestones

| Milestone | What it proves | Done when |
| --- | --- | --- |
| **M1: Walk the deep** | The engines meet | A generated level drawn isometric in Flare tiles, with FOV and light; the hero moves on the scheduler, takes stairs; the town exists |
| **M2: Fight** | PF2 on energy | Rounds and the multiple attack penalty, 40 monsters bridged (depth 1–10) with AI and senses, conditions, death and the graveyard; a bot reaches depth 5 |
| **M3: Loot** | The item bridge | Bases, potency and striking, 30 egos as runes, rune identification, the stores and runework |
| **M4: The whole bestiary** | Coverage | Every monster, spell and element bridged; all names renamed and checked; sprite families and the paper fallback |
| **M5: To the Cinder** | The whole game | All profiles, vaults and pits; the five story levels; Saint Ysmere and the Cinder of Ashur; mythic tiers; bots clear it |
| **M6: Breadth** | Polish | Priest, Druid and Blackguard; challenges; randarts; mods; Legacy from an AVAAshenReliquary save |

**Tests and CI** (from both games):
- `-warnaserror`, and tests with every change;
- headless screenshot tests;
- AVABand's **soak**: bots play seeded games, record them and replay them;
- AshenReliquary's **balance** reports, per stratum;
- AVABand's **4.2.5 drift** check;
- the **bridge** check: every entry is mapped, there are no Tolkien names, and sprite coverage is reported;
- builds for eight platforms, a rolling `latest` release, and a dated "what's new" page.

## 11. Risks

| Risk | Mitigation |
| --- | --- |
| Balancing 623 translated monsters by hand is impossible | Formulas first, then bots flag outliers; tune only those |
| PF2's maths with permadeath: a crit-fishing monster kills in one round | Severe threats are telegraphed by the level feeling and monster recall; Second Breath; bots measure deaths per stratum |
| Monster art coverage | Families with tint and scale, the paper fallback, and the coverage report |
| A third god class | The session is split by concern from the start, as a rule in AGENTS.md |
| A Tolkien name slips through | The bridge check scans every string shown to the player |
| Licences | GPL-2 code, licence per asset, generated credits, ORC credit |

## 12. Settled after draft 2

- **Second Breath** replaces Hero Points (section 4.2).
- **The classic views stay:** AVABand's top-down ASCII and tile views, behind Ctrl+T (section 8).
- **Names:** the sample names in section 3 are placeholders for the author to change.
