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

1. **Every descent is new.** Below Gallowmere only the story levels are hand-made; the rest is Angband's generator.
   Above it, from M7, lies AshenReliquary's own hand-made province (section 7.4).
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

### 7.4 The province: Vardmoor, ten years on (M7)

AshenReliquary is an open world of 22 hand-made zones across the province of Vardmoor. Here they become the surface
above the shaft: the same maps, ten years after the Cinder Throne. The dungeon stays the game's spine; the province is
where a hero goes for its own stories, for loot and experience at their level, and for what it tells them about what
waits below.

**The map.** AshenReliquary's zones keep their places and the exits between them:

| Region (Ashen act) | Zones (creature level) | Waystation |
| --- | --- | --- |
| Gallowmere's country (1) | Gallows Road (1), Rotwater Fens (2), Drowned Chapel (3), Prior's Ossuary (4), Sallow's Mire (4) | Gallowmere itself |
| The Iron March (2) | Iron March (6), Barrow Fields (7), Old Barracks (8), Siege Walls (9) | Ironhold (6) |
| The Oriel highlands (3) | Frostbitten Pass (11), Hollow Woods (12), Catacombs of Oriel (13), Bell Tower (14) | Saint Oriel's Rest (11) |
| The Cinder country (4) | Ash Wastes (16), Lava Rivers (17), Wyrm's Nest (18), the Reliquary (19), the Cinder Throne (20) | Ember Gate (16) |

- Fort Gallowmere's two exits become gates in Gallowmere's wall, where Ashen has them: the south gate to the Gallows
  Road, the west gate to the Rotwater Fens. Gallowmere itself stays AVABand's generated town.
- **Waystations** (Ironhold, Saint Oriel's Rest, Ember Gate) are small hand-made towns: a shrine, one trader each
  (Ironhold a smith, the Rest an apothecary, Ember Gate a quartermaster) and a Lantern Rift.
- **The Reliquary** is the Lid seen from above: its deepest hall ends at the sealed mouth of the shaft. The Cinder
  Throne is where Ashen Reliquary ended; ten years on it is empty but for what the rumour (or the Legacy) left there.

**Levels from Ashen's files.** A zone is a fixed level, read from AshenReliquary's zone format (rows and a legend),
as the story rooms are:
- the zone files are copied unchanged into `data/province/zones/`; what this game changes (its ten-years-on
  taglines, its stores, its tie-ins) goes in `data/province/province.json` beside them, as the 4.2.5 data is kept;
- the legend's ground, objects, `blocks` and `blocksSight` become terrain: floor, path, grass, water (shallow, slows),
  trees and rubble (block, and block sight), walls;
- `playerStart` is where the hero arrives from Gallowmere's gate; an exit is a gate square, which takes the hero to the
  next zone's exit and its `arrive` square.

**Danger.** A zone's level is its creature level (section 4.3). For loot and for Angband's wanderers it stands for the
depth where a hero is expected to be that level (level 5 is depth 10, level 10 depth 30, level 16 depth 60, level 20
depth 90). Ashen's `xpScale` stays.

**Creatures.**
- Ashen's 58 creatures come in as monsters of the setting (`data/setting/`, in Angband's format): their attacks
  become blows, `undead`, `immunities` and `weaknesses` become flags. They are bridged with their Flare sprites.
- Their packs stand where Ashen placed them, at the pack's rank.
- A few of Angband's monsters of the zone's depth wander in, more at night (AVABand's day and night).

**Persistence.** Unlike the dungeon, a zone is kept for the hero:
- the packs a hero kills stay dead until they have been down at least five dungeon levels since;
- each cache opens once;
- each shrine is used once.

**What a zone holds.**
- **Caches** are chests (Angband's chest objects) holding good objects of the zone's depth.
- **Shrines** are section 4.4's: one blessing from three, drawn by Ashur's whim. They are built here first.
- **NPCs and keepsakes** stand where Ashen put them. Talking to an NPC gives their line ten years on; Ashen's full
  dialogue trees (section 7.3) come later. Keepsakes are lore, kept in the hero's journal.
- **Weather.** `darkness` and `heroLight` set the zone's light. `ashStorms` halve sight radius while a storm blows.
  `cold` (the Frostbitten Pass) calls for a Fortitude save every minute against 1d6 cold, which cold resistance
  negates.

**Ten years on: tie-ins.** Each region has one side story that points below:
- Sallow's Mire: where the Rot-Mother began (before the Font, at 15);
- the Catacombs and the Bell Tower: the Choir's first song (before the Drowned Library, at 33);
- the Barrow Fields and the Siege Walls: the words of Ser Hadric's oath (before the Oath Hall, at 45);
- the Wyrm's Nest: the dam's old nest, cold now (before the Brood Nest, at 72).

Each sets a story flag. Wren and the epilogue read it, and an altar below can offer a third choice for it.

**Travel.**
- Zones are walked, exit to exit.
- The Lantern Rift (Word of Recall) takes the hero from any zone to Gallowmere.
- A waystation's rift, once touched, takes the hero from Gallowmere back to that waystation.

**Art.** Flare's cinder, ironhold and oriel tilesets come over from AshenReliquary (CC-BY-SA, as the rest), each
with its level dressing. The classic views draw zones with AVABand's surface tiles and letters.

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

### 8.1 Sound and music (M8)

The game is silent until M8. Both parents already have sound on the same OpenAL layer, so M8 brings theirs together
and adds nothing new to the libraries.

**What comes over.**

| From | What | Licence |
| --- | --- | --- |
| The OpenAL layer both games share | AshenReliquary's copy (it pans, and has the extra-large mixing period): the OpenAL Soft engine (Silk.NET), Ogg and MP3 decoders, loudness, failure handling, the period fix for virtual machines (`OpenAlConfig`); AVABand's `SoundPack` and the shape of its `SoundDirector` | GPL-2 and MIT |
| AshenReliquary `AudioDirector` | Distance and screen-position panning, the repeat guard, the combat hold and the ability-ready tick | MIT |
| AVABand's CC0 dungeon music | 16 tracks: the title ("The Pits of Angband"), town (2), dungeon (7), deep (5), boss (1); its two night tracks stay out, as this game has no night | CC0 |
| AshenReliquary's music | Kevin MacLeod's and Alexandr Zhelanov's: title, explore (2), combat (2), boss | CC BY 4.0 |
| AVABand's ambience | 11 loops: town by day and night, the dungeon shallow, deep and abyss, cavern, labyrinth, lair, Moria-style mines, fortress, gauntlet | CC0 |
| AshenReliquary's ambience | Its road loop is JaggedStone's dungeon loop again, so it is not copied twice | CC0 |
| Dubtrain's Angband sounds | 149 effects named by Angband's message types: hits by degree, every breath and summons, monster blows, doors, stairs, stores, statuses | CC BY 4.0 |
| AshenReliquary's effects | 45 groups (its guns' four stay out): swings, hits and crits, blocks, wards, element bursts, gold, pickups by material, potions, chests, shrines, level-up, death, footsteps, and creature voices (zombie, skeleton, wolf, crawler, cultist, crone) | CC0 and CC BY 3.0, per file |

They ship as a sound pack folder, `audio/`, beside the executable as `tilesets/` does: about 53 MB (339
files), in each platform's archive. `audio/soundpack.json` is AVABand's pack format with ambience added: effects keyed by event name, music by
mood, ambience loops by name, a volume per event, and the source and licence of every file (`audio/CREDITS.md` lists
them). `tools/audio/make_audio_pack.py` makes it from the two parents' repositories. A mod may bring its own pack (section 6's layering, by key).

**Music, by where the hero is.** One mood plays at a time and crossfades to the next:

| Where | Music | Ambience |
| --- | --- | --- |
| The birth window and the book of the dead | "The Pits of Angband" (AVABand's title) | none |
| Gallowmere and the waystations | AVABand's town tracks | Gallowmere the town by day; a waystation by its tileset (below) |
| Province zones, outdoors | Ashen's explore tracks | by tileset (below) |
| Province zones, indoors: darkness 0.85 or more (chapel, ossuary, barracks, catacombs, bell tower, the Reliquary, the Wyrm's Nest, the Cinder Throne) | AVABand's dungeon tracks | dungeon, shallow; the cinder caves the lair |
| The shaft, depth 1–30 | AVABand's dungeon tracks | dungeon, shallow; cavern, labyrinth and the rest by the level's profile, as in AVABand |
| The shaft, depth 31 and below | AVABand's deep tracks | dungeon, deep to 60, the abyss below |
| Fighting: a monster hunting the hero in view | Ashen's combat tracks, held ten game turns after the last one leaves view | as before, quieter |
| A great foe in view: a unique, a story keeper, Saint Ysmere, the Cinder | Ashen's boss track, then AVABand's | as before, quieter |
| The epilogue | Ashen's title | none |

Province ambience by tileset: grassland the morning loop (birds), the Iron March's the fortress (wind in halls, a
forge-hammer), Oriel's snow the wind, the cinder country the gusting wind of the gauntlet. A zone may name its own
mood or loop in `data/province/province.json` (`sound`): the Rotwater Fens and Sallow's Mire have the night's
crickets.

**Effects, from the game's events.** `SoundDirector` listens to `Published` and nothing else, so the session never
knows about sound. Where the game has no event yet for something worth hearing, M8 adds one; the view's own events
stay the view's.

- **The hero's Strikes** are Ashen's: a swing on a miss, a hit, a crit on a critical success, and a block when Shield
  Block takes damage. Abilities sound by kind: Ashen's ignition, force bolt and impact for spell attacks, element
  bursts for blasts by their damage type, the ward sounds for wards and auras.
- **Monsters' blows** are Dubtrain's by Angband blow method (MON_BITE, MON_CLAW, MON_CRUSH, MON_STING and the rest).
  Their spells are Dubtrain's by spell (BR_FIRE, SUM_UNDEAD, SHRIEK, TELEPORT...). A creature's voice when it appears
  and dies is Ashen's by monster base: zombies, skeletons, dogs as the wolf, insects and spiders as the crawler,
  casting people as the cultist, hags and witches as the crone. Angband's monsters with none are silent.
- **Kills** are Dubtrain's KILL, KILL_UNIQUE and KILL_KING (a story keeper).
- **Things:** Ashen's gold, pickups by material (cloth, metal, jewel) and potions; Dubtrain's WIELD, ZAP_ROD and
  ACT_ARTIFACT; Ashen's rare-drop when an artifact or ego lands.
- **Places:** Dubtrain's doors, stairs and stores; Ashen's chest (a cache), shrine (a blessing or a rift), level-up
  and zone-complete (the last pack of a zone falls). A reading turns a page (Ashen's inventory sound).
- **The hero:** Ashen's hero-hurt by how much is lost; Dubtrain's statuses as conditions start and end (BLIND,
  POISONED, AFRAID, PARALYZED, SLOW, STUN, RECOVER); HITPOINT_WARN below a quarter; Second Breath its own sound; death
  Ashen's.
- **Footsteps:** Ashen's, one per step, quiet, and only outdoors and in town, where the turn rate is slow enough.
- **Ready:** an ability with a cooldown of 5 seconds or more ticks when it comes ready, as in Ashen.

**Placement.** An effect at a square is panned by where that square is on screen and quieter with distance from the
hero, as in Ashen. Only what the hero sees, or hears by Angband's noise (section 7.1), is played: a breath out of
sight is a distant rumble, a blow behind a wall is nothing. The repeat guard keeps a pack's blows in one turn from
playing as one loud note.

**Determinism.** Sound picks its variants with its own random generator, never `GameRandom`, so a seed and its
commands make the same game with sound on or off.

**Options.** A Sound window (`Ctrl+O`): master, music, effects and ambience volumes; music, effects and ambience each
on or off; and AVABand's mixing period (Automatic, which uses 4096 frames in a virtual machine, or Normal, Large,
Extra large). They are kept in `settings.json`. `Ctrl+M` mutes and unmutes everything.

**Failure.** No audio device, no OpenAL, or a bad file is never an error: the game plays silently and says so once
in the message log (AVABand's `AudioFailure`). The tests and CI run with a recording engine and never open a device.

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
  WorldOfAngBand.Audio/    the shared OpenAL layer, with AVABand's SoundDirector (M8; section 8.1)
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
| **M7: The province** | Ashen's world above the shaft | All 18 wild zones and 3 waystations, reached through Gallowmere's gates, with their creatures, caches, shrines, NPCs' lines and weather; kept per hero; the four tie-ins; bots clear each zone at its level |
| **M8: Sound** | Both games' audio as one | Music by place, fight and great foe; ambience by zone, profile and depth; effects for every event worth hearing, placed on screen; the Sound window; silent and unbroken with no device; credits per file |

M7 comes in three parts:
- **M7a** (done): fixed-map levels from Ashen's files, gates and exits, the three new tilesets, and the first two
  zones (the Gallows Road and the Rotwater Fens). Outdoor zones are known from the moment the hero arrives, as in
  Ashen; dark ones are explored as the dungeon is;
- **M7b** (done): Ashen's creatures bridged, caches, shrines, persistence, and every zone and waystation. Packs
  that hang on Ashen's story flags stand down with its Heralds; a creature strikes or shoots once a turn, its own dice at
  its rank's share;
- **M7c** (done): NPCs and keepsakes, weather, the tie-ins, the rifts, and a province bot that walks each zone at its
  expected level.

M8 comes in two parts:
- **M8a** (done): the audio project copied from AVABand, the sound pack beside the executable with its credits, the music
  and ambience by place (town, zone, depth and profile), and the Sound window;
- **M8b** (done): the effects for every event, with the events the session lacks; placement by screen and distance;
  hearing by noise; the combat and great-foe music; footsteps and the ready tick.

M8b as built:
- The session publishes `CueEvent`s (doors, stairs, recall, pickups by item base, potions, shops, caches,
  blessings, rifts, a zone cleared) and `AbilityUsedEvent`s, and its strikes and spells carry the blow's method
  and the spell's id. What changes without an event (hit points, conditions, blessings, cooldowns, who is in view)
  the director reads each frame. `SoundCues` says what each sounds like.
- Angband's monster spells keep their own ids (BR_NETH, S_KIN); `SoundCues` gives them Dubtrain's names (BR_NETHER,
  SUM_MONSTER). Bolts and balls, which Dubtrain has no sound for, use Ashen's: ignition and force bolt, its element
  bursts.
- A sound out of sight is heard if it is loud (a spell, a door) and the hero's noise reaches it within 20 steps, at a
  third of its volume; a blow or a voice out of sight is not.
- The fight's music holds 100 game turns (ten of the hero's at normal speed) after the last hunter leaves view.
- Second Breath is Dubtrain's INVULN; the win is KILL_KING.

M8a as built: the music and ambience follow the screen and the place each frame and change only when the mood or
loop does; a mood plays a track at a time from its playlist, never the same one twice running. Sound names are
case-sensitive, since Dubtrain's DEATH and Ashen's death are different sounds. A pack that fails its check is left
out and says why in the message log.

M7 as built:
- The NPCs who stand ten years on are those the province notes give a line (eleven of them, the copies of a person
  for each turn of Ashen's story left out); readings are Ashen's own words.
- A waystation's shrine is its Lantern Rift; the wild's shrines give blessings.
- Speaking the oath's last words (the Barrow Fields tie-in) releases all Nine Unburied, so none is met below.
- Ashen's creature tricks (kiting, summoning, reviving, rallying) are not carried over.
- The province bot survives about 85% of zone walks at the expected level. The Barrow Fields (69 creatures, wights
  casting cold) is the hardest zone.

M6 as built: the Priest, Druid and Blackguard come with AshenReliquary's ability system played one action at a time,
which makes its Fighter, Champion, Rogue and Wizard playable too. The Ranger, Necromancer and Gunslinger wait for
companions, raised dead, bows and guns. AVABand's Famine twist has no food to work on here, so it becomes Thin Flasks
(two healing flasks, not three).

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
| Ashen's zones were made for real time: a pack that rushed in over seconds now arrives over turns, and big zones (up to 96 by 220) are slow to cross | Ashen's packs are placed asleep at their posts and wake by Angband's rules; long zones get travel (click to travel already crosses seen ground) and their waystation rifts |

## 12. Settled after draft 2

- **Second Breath** replaces Hero Points (section 4.2).
- **The classic views stay:** AVABand's top-down ASCII and tile views, behind Ctrl+T (section 8).
- **Names:** the sample names in section 3 are placeholders for the author to change.
