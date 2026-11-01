using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Game;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Audio;

/// <summary>
/// What each thing sounds like (the spec's section 8.1): the sound pack's names for the game's events. Upper-case
/// names are Dubtrain's Angband sounds (Angband's message types), lower-case ones AshenReliquary's.
/// </summary>
public static class SoundCues
{
    /// <summary>The hero's Strike: a swing on a miss, a hit, a crit; a monster's blow by its method (MON_BITE...).</summary>
    public static string? Strike(StrikeEvent e)
    {
        if (e.ByHero)
            return e.Degree < DegreeOfSuccess.Success ? "swing" : e.Degree == DegreeOfSuccess.CriticalSuccess ? "crit" : "hit";
        if (e.Method is null)
            return null;
        if (e.Degree < DegreeOfSuccess.Success)
            return "swing";
        // Angband's blows by method; AshenReliquary's creatures "hit".
        return "MON_" + e.Method.ToUpperInvariant();
    }

    /// <summary>A monster's spell by its id (Angband's monster spells, in Dubtrain's names); AshenReliquary's shots and casts.</summary>
    public static string? Spell(string? id)
    {
        if (id is null)
            return null;
        if (id.StartsWith("BR_", StringComparison.Ordinal))
            return id switch
            {
                "BR_COLD" => "BR_FROST",
                "BR_POIS" => "BR_GAS",
                "BR_NETH" => "BR_NETHER",
                "BR_SOUN" => "BR_SOUND",
                "BR_CHAO" => "BR_CHAOS",
                "BR_DISE" => "BR_DISEN",
                "BR_NEXU" => "BR_NEXUS",
                "BR_SHAR" => "BR_SHARDS",
                "BR_INER" => "BR_INERTIA",
                "BR_GRAV" => "BR_GRAVITY",
                "BR_PLAS" => "BR_PLASMA",
                "BR_WALL" => "BR_FORCE",
                "BR_MANA" => "BR_ELEMENTS",
                _ => id,
            };
        if (id.StartsWith("S_", StringComparison.Ordinal))
            return id switch
            {
                "S_MONSTER" or "S_MONSTERS" or "S_KIN" => "SUM_MONSTER",
                _ => "SUM_" + id[2..],
            };
        if (id.StartsWith("BO_", StringComparison.Ordinal) || id.StartsWith("BE_", StringComparison.Ordinal))
            return id is "BO_FIRE" or "BO_PLAS" ? "ignition" : "force-bolt";
        if (id.StartsWith("BA_", StringComparison.Ordinal))
            return id switch
            {
                "BA_FIRE" => "bomb-fire",
                "BA_COLD" => "bomb-cold",
                "BA_ELEC" => "bomb-electricity",
                "BA_ACID" or "BA_POIS" => "bomb-acid",
                _ => "impact",
            };
        return id switch
        {
            "ARROW" or "BOLT" or "SHOT" or "MISSILE" or "BOULDER" or "ASHEN_SHOOT" => "bow",
            "ASHEN_CAST" => "witch-cast",
            "SPIT" => "MON_SPIT",
            "WHIP" => "MON_HIT",
            "SHRIEK" => "SHRIEK",
            "SCARE" => "CAST_FEAR",
            "BLINK" or "TPORT" or "TELE_TO" or "TELE_AWAY" or "TELE_SELF_TO" => "TELEPORT",
            "TELE_LEVEL" => "TPLEVEL",
            "HASTE" => "SPEED",
            "HEAL" or "HEAL_KIN" => "ward-spell",
            "TRAPS" => "CREATE_TRAP",
            "DRAIN_MANA" => "DRAIN_STAT",
            "STORM" => "bomb-electricity",
            "MIND_BLAST" or "BRAIN_SMASH" or "WOUND" => "impact",
            _ => "SPELL",
        };
    }

    /// <summary>The hero's ability by its kind: Strikes sound as Strikes, spells and bursts by their element, wards as wards.</summary>
    public static string? Ability(AbilityUsedEvent e) => e.Kind switch
    {
        "WeaponStrike" or "ChargeStrike" or "Hide" => null,
        "SpellBolt" => e.DamageType == "Fire" ? "ignition" : "force-bolt",
        "Missiles" => "force-bolt",
        "Blast" or "Bomb" or "Burst" => Burst(e.DamageType),
        "Ward" => "ward-shield",
        "Aura" or "Command" or "Mark" => "ward-spell",
        "Heal" or "Elixir" => "PRAYER",
        "Tumble" => "footstep",
        "Trap" => "CREATE_TRAP",
        "Summon" => "SUM_MONSTER",
        _ => "SPELL",
    };

    private static string Burst(string? type) => type switch
    {
        "Fire" => "bomb-fire",
        "Cold" => "bomb-cold",
        "Electricity" => "bomb-electricity",
        "Acid" or "Poison" => "bomb-acid",
        _ => "impact",
    };

    public static string? Cue(CueEvent e) => e.Cue switch
    {
        Game.Cue.DoorOpened or Game.Cue.DoorBroken => "OPENDOOR",
        Game.Cue.StairsDown => "STAIRS_DOWN",
        Game.Cue.StairsUp => "STAIRS_UP",
        Game.Cue.Recalled or Game.Cue.LevelTeleported => "TPLEVEL",
        Game.Cue.Teleported => "TELEPORT",
        Game.Cue.GoldTaken or Game.Cue.Sold => "gold",
        Game.Cue.ItemTaken => Pickup(e.Detail),
        Game.Cue.ItemDropped => "DROP",
        Game.Cue.Wielded => "WIELD",
        Game.Cue.Quaffed or Game.Cue.FlaskDrunk => "potion",
        Game.Cue.Read => "inventory",
        Game.Cue.Activated => "ACT_ARTIFACT",
        Game.Cue.RareDrop => "rare-drop",
        Game.Cue.Bought => "STORE5",
        Game.Cue.RuneEtched => "HIT_GOOD",
        Game.Cue.CacheOpened => "chest",
        Game.Cue.BlessingTaken => "shrine",
        Game.Cue.RiftStepped => "ward-spell",
        Game.Cue.ZoneCleared => "zone-complete",
        _ => null,
    };

    /// <summary>A pickup by what it's made of, as AshenReliquary sounds them: metal, jewellery, or cloth and paper.</summary>
    public static string Pickup(string? itemBase) => itemBase switch
    {
        "sword" or "hafted" or "polearm" or "digger" or "hard_armour" or "shield" or "helm" or "crown" or "dragon_armour"
            or "shot" or "wand" or "rod" or "light" or "flask" => "pickup-metal",
        "ring" or "amulet" => "pickup-jewel",
        "potion" => "potion",
        _ => "pickup-cloth",
    };

    /// <summary>
    /// A creature's voice, as AshenReliquary has them: its own creatures name theirs; Angband's by monster base
    /// (zombies, skeletons, dogs as the wolf, insects and spiders as the crawler, casting people as the cultist, hags
    /// and witches as the crone). Null: it has none.
    /// </summary>
    public static string? Voice(MonsterRaceDef race, string? ashenVoice = null)
    {
        if (ashenVoice is not null)
            return ashenVoice == "grave" ? null : ashenVoice;
        var name = race.Name;
        if (name.Contains("hag", StringComparison.OrdinalIgnoreCase) || name.Contains("witch", StringComparison.OrdinalIgnoreCase)
            || name.Contains("crone", StringComparison.OrdinalIgnoreCase))
            return "crone";
        return race.Base switch
        {
            "zombie" or "mummy" => "zombie",
            "skeleton" => "skeleton",
            "canine" or "zephyr_hound" => "wolf",
            "spider" or "insect" or "ant" or "centipede" or "killer_beetle" or "worm" => "crawler",
            "person" when race.Spells.Count > 0 => "cultist",
            _ => null,
        };
    }

    /// <summary>A condition as it starts (Dubtrain's statuses), or null.</summary>
    public static string? ConditionStarts(ConditionKind kind) => kind switch
    {
        ConditionKind.Frightened => "AFRAID",
        ConditionKind.Slowed => "SLOW",
        ConditionKind.Stunned => "STUN",
        ConditionKind.Blinded or ConditionKind.Dazzled => "BLIND",
        ConditionKind.Immobilized or ConditionKind.Grabbed => "PARALYZED",
        ConditionKind.Sickened => "POISONED",
        ConditionKind.Drained or ConditionKind.Enfeebled or ConditionKind.Clumsy or ConditionKind.Stupefied => "DRAIN_STAT",
        _ => null,
    };

    /// <summary>A timed blessing as it starts (Dubtrain's), or null.</summary>
    public static string? TimedStarts(string effect) => effect switch
    {
        "haste" => "SPEED",
        "heroism" => "HERO",
        "berserk" => "BERSERK",
        "bless" => "BLESSED",
        _ => null,
    };
}
