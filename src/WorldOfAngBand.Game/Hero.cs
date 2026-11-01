using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Engine.Items;
using WorldOfAngBand.Engine.Time;
using WorldOfAngBand.Rules.Characters;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Content;

namespace WorldOfAngBand.Game;

/// <summary>
/// The Lantern-sworn the player plays: a Pathfinder character (AshenReliquary's rules) carrying Angband's items, where
/// they stand, and how they take their turns. What their gear comes to in Pathfinder's terms is <see cref="Gear"/>.
/// </summary>
public sealed class Hero : IActor
{
    /// <summary>Pathfinder's experience to the next level.</summary>
    public const int ExperiencePerLevel = 1000;

    public Hero(Character character) => Character = character;

    /// <summary>
    /// A new Lantern-sworn: until character creation comes, a human fighter (a Bulwark, from the Deserter background).
    /// Their kit is given by the session, which makes the items.
    /// </summary>
    public static Hero Create(ContentDatabase content, string name = "Lantern-sworn")
    {
        var choices = CharacterBuilder.Recommend(content, name, "human", "deserter", "fighter") with { Subclass = "bulwark" };
        var hero = new Hero(CharacterBuilder.Create(content, choices));
        hero.Character.RestAtShrine();
        return hero;
    }

    public Character Character { get; }

    /// <summary>What the hero carries and wears: Angband's pack and equipment.</summary>
    public Inventory Pack { get; } = new();

    /// <summary>What the worn items come to (set by the session whenever they change).</summary>
    public Gear Gear { get; } = new();

    public int Gold { get; set; }

    /// <summary>Temporary effects from potions and scrolls, and how many of the hero's turns each has left.</summary>
    public Dictionary<string, int> Timed { get; } = [];

    /// <summary>Turns until Word of Recall takes the hero (0 when none is pending).</summary>
    public int RecallIn { get; set; }

    /// <summary>Turns until Deep Descent takes the hero down (0 when none is pending).</summary>
    public int DescentIn { get; set; }

    public string Name => Character.Name;

    public int ActorId => 0;

    /// <summary>
    /// Speed relative to normal: +10 is twice as many actions. Angband's speed items add to it on its own scale; haste is
    /// +10 and Pathfinder's Slowed takes 10 off.
    /// </summary>
    public int Speed => BaseSpeed + Gear.Speed + (Timed.ContainsKey("haste") ? 10 : 0)
                        - (Character.Conditions.Has(ConditionKind.Slowed) ? 10 : 0);

    public int BaseSpeed { get; set; }

    public int Energy { get; set; }

    public bool IsActive => !Character.IsDead;

    public Loc Position { get; set; }

    /// <summary>The way the hero last moved or struck, for which way their sprite faces.</summary>
    public Direction Facing { get; set; } = Direction.South;

    public int MaxDepth { get; set; }

    /// <summary>How far the hero's light reaches: their light source's, and what else they wear adds.</summary>
    public int LightRadius => Math.Max(0, Gear.Light);

    public Round Round { get; } = new();

    /// <summary>Experience toward the next level.</summary>
    public int Experience { get; set; }

    public int Level => Character.Level;

    public int HitPoints => Character.HitPoints;

    public int MaxHitPoints => Character.MaxHitPoints;

    /// <summary>
    /// How quiet the hero is on Angband's scale (0 noisy to 30 silent), from their Pathfinder Stealth (with their armour's
    /// check penalty) and what they wear: monsters asleep hear an Angband stealth of s at 2^(30 − s).
    /// </summary>
    public int Stealth => Math.Clamp(1 + Character.Skill("Stealth").Total / 2 + Gear.Stealth, 0, 30);
}
