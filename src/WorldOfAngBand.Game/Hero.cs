using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Engine.Time;
using WorldOfAngBand.Rules.Characters;
using WorldOfAngBand.Rules.Content;
using WorldOfAngBand.Rules.Items;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Game;

/// <summary>
/// The Lantern-sworn the player plays: a Pathfinder character (AshenReliquary's rules) with their gear, where they
/// stand, and how they take their turns.
/// </summary>
public sealed class Hero : IActor
{
    /// <summary>A wooden torch's light, as in Angband 4.2.</summary>
    public const int TorchRadius = 2;

    /// <summary>Pathfinder's experience to the next level.</summary>
    public const int ExperiencePerLevel = 1000;

    public Hero(Character character)
    {
        Character = character;
        Gear = new Equipment(character);
    }

    /// <summary>
    /// A new Lantern-sworn: until character creation comes, a human fighter (a Bulwark, from the Deserter background)
    /// in AshenReliquary's fighter kit.
    /// </summary>
    public static Hero Create(ContentDatabase content, string name = "Lantern-sworn")
    {
        var choices = CharacterBuilder.Recommend(content, name, "human", "deserter", "fighter") with { Subclass = "bulwark" };
        var hero = new Hero(CharacterBuilder.Create(content, choices));
        foreach (var id in new[] { "longsword", "wooden_shield", "chain_cuirass", "leather_pants", "leather_boots", "leather_gloves" })
            hero.Gear.Equip(new Item(content.Item(id), Rarity.Common, 1));
        hero.Character.RestAtShrine();
        return hero;
    }

    public Character Character { get; }

    public Equipment Gear { get; }

    public string Name => Character.Name;

    public int ActorId => 0;

    /// <summary>Speed relative to normal: +10 is twice as many actions. Pathfinder's Slowed takes 10 off.</summary>
    public int Speed => BaseSpeed - (Character.Conditions.Has(Rules.Combat.ConditionKind.Slowed) ? 10 : 0);

    public int BaseSpeed { get; set; }

    public int Energy { get; set; }

    public bool IsActive => !Character.IsDead;

    public Loc Position { get; set; }

    /// <summary>The way the hero last moved or struck, for which way their sprite faces.</summary>
    public Direction Facing { get; set; } = Direction.South;

    public int MaxDepth { get; set; }

    public int LightRadius { get; set; } = TorchRadius;

    public Round Round { get; } = new();

    /// <summary>Experience toward the next level.</summary>
    public int Experience { get; set; }

    public int Level => Character.Level;

    public int HitPoints => Character.HitPoints;

    public int MaxHitPoints => Character.MaxHitPoints;

    /// <summary>
    /// How quiet the hero is on Angband's scale (0 noisy to 30 silent), from their Pathfinder Stealth: monsters asleep
    /// hear an Angband stealth of s at 2^(30 − s).
    /// </summary>
    public int Stealth => Math.Clamp(1 + Character.Skill("Stealth").Total / 2, 0, 30);
}
