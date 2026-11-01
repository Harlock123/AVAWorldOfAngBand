using WorldOfAngBand.Rules.Characters;
using WorldOfAngBand.Rules.Content;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Abilities;

/// <summary>
/// The class resource abilities spend. Fury (Fighter) starts empty, builds when you hit or are hit and drains
/// after a few seconds out of combat. Mana (casters) starts full and regenerates with INT and level. Momentum (Rogue)
/// starts empty and builds as the hero moves (only up to a running start out of a fight) and on hits, crits most; it
/// drains once the hero stands still out of a fight. Devotion (Champion) starts empty and builds most from the blows the
/// hero takes (blocked or not) and a little from their own hits; it drains slowly once a fight is over. Grit
/// (Gunslinger) starts empty and builds from the hero's own hits, most from critical hits, never from blows taken; it
/// drains once a fight is over.
/// </summary>
public sealed class ClassResource
{
    public const double FuryPerHit = 12;
    public const double FuryPerCrit = 20;
    public const double FuryWhenHit = 6;
    public const double FuryDecayDelay = 4;
    public const double FuryDecayPerSecond = 8;
    public const double MomentumPerTile = 4;
    public const double MomentumPerHit = 4;
    public const double MomentumPerCrit = 15;

    /// <summary>Out of a fight, moving builds Momentum only this far: a running start.</summary>
    public const double MomentumRunningStart = 30;
    public const double MomentumDecayDelay = 3;
    public const double MomentumDecayPerSecond = 10;
    public const double DevotionWhenHit = 10;
    public const double DevotionPerHit = 4;
    public const double DevotionPerCrit = 8;
    public const double DevotionDecayDelay = 6;
    public const double DevotionDecayPerSecond = 5;
    public const double GritPerHit = 8;
    public const double GritPerCrit = 20;
    public const double GritDecayDelay = 5;
    public const double GritDecayPerSecond = 6;

    /// <summary>Seconds after the last blow struck or taken that the hero still counts as in a fight.</summary>
    public const double CombatSeconds = 5;

    private readonly Character _character;
    private readonly AttributeKind _key;
    private double _sinceCombat = double.PositiveInfinity;
    private double _sinceMoved = double.PositiveInfinity;

    public ClassResource(Character character) : this(character, character.Class, dedication: false)
    {
    }

    /// <summary>
    /// The resource of <paramref name="source"/>'s class. A <paramref name="dedication"/>'s pool (multiclassing: a
    /// Fighter with Wizard Dedication) is small: a Mana or Focus pool half the size, regenerating half as fast, from that
    /// class's key attribute; Fury, Momentum and Devotion build as ever.
    /// </summary>
    public ClassResource(Character character, ClassDefinition source, bool dedication)
    {
        _character = character;
        Source = source;
        IsDedication = dedication;
        _key = dedication ? source.KeyAttributes[0] : character.KeyAttribute;
        Kind = KindOf(source);
        Current = StartsEmpty ? 0 : Max;
    }

    /// <summary>The kind of resource a class spends.</summary>
    public static ResourceKind KindOf(ClassDefinition c) =>
        c.Resource.StartsWith("Fury", StringComparison.OrdinalIgnoreCase) ? ResourceKind.Fury
        : c.Resource.StartsWith("Mana", StringComparison.OrdinalIgnoreCase) ? ResourceKind.Mana
        : c.Resource.StartsWith("Momentum", StringComparison.OrdinalIgnoreCase) ? ResourceKind.Momentum
        : c.Resource.StartsWith("Devotion", StringComparison.OrdinalIgnoreCase) ? ResourceKind.Devotion
        : c.Resource.StartsWith("Grit", StringComparison.OrdinalIgnoreCase) ? ResourceKind.Grit
        : ResourceKind.Focus;

    /// <summary>The class whose resource this is.</summary>
    public ClassDefinition Source { get; }

    /// <summary>A multiclass dedication's small pool, beside the class's own.</summary>
    public bool IsDedication { get; }

    /// <summary>
    /// A dedication's pool that rides along with this one: the blows, steps and seconds that feed this feed it too.
    /// </summary>
    internal ClassResource? Partner { get; set; }

    public ResourceKind Kind { get; }
    public string Name => Kind.ToString();
    public double Current { get; private set; }

    /// <summary>Fury, Momentum, Devotion and Grit start empty and are built in a fight; Mana and Focus start full and regenerate.</summary>
    public bool StartsEmpty => Kind is ResourceKind.Fury or ResourceKind.Momentum or ResourceKind.Devotion or ResourceKind.Grit;

    /// <summary>Whether a blow was struck or taken in the last <see cref="CombatSeconds"/>.</summary>
    public bool InCombat => _sinceCombat < CombatSeconds;

    public double Max => Kind switch
    {
        ResourceKind.Fury or ResourceKind.Momentum or ResourceKind.Devotion or ResourceKind.Grit => 100,
        _ => (20 + 5 * _character.Level + 4 * Math.Max(0, _character.Attributes[_key])) * (IsDedication ? 0.5 : 1),
    };

    /// <summary>Mana or Focus per second: 1 + a quarter per point of the key attribute + a tenth per level.</summary>
    public double RegenPerSecond => StartsEmpty ? 0
        : (1 + 0.25 * Math.Max(0, _character.Attributes[_key]) + 0.1 * _character.Level) * (1 + _character.Gear.ResourceRegen + _character.ResourceRegenBonus)
          * (IsDedication ? 0.5 : 1);

    public bool CanSpend(double amount) => Current + 1e-9 >= amount;

    public bool TrySpend(double amount)
    {
        if (!CanSpend(amount))
            return false;
        Current -= amount;
        return true;
    }

    /// <summary>
    /// A hit landed (or, with <paramref name="taken"/>, a hit received): Fury builds, Momentum on the hero's own
    /// hits (most on a critical), and Devotion most from blows taken. A graze builds half as much; with
    /// <paramref name="builds"/> false (a sweep's other targets) the fight is only kept going. Gear's and feats' "per
    /// hit" adds to the hero's own hits.
    /// </summary>
    public void OnCombat(bool critical = false, bool taken = false, bool graze = false, bool builds = true)
    {
        Partner?.OnCombat(critical, taken, graze, builds);
        _sinceCombat = 0;
        if (!builds || Kind is not (ResourceKind.Fury or ResourceKind.Momentum or ResourceKind.Devotion or ResourceKind.Grit)
            || (taken && Kind is ResourceKind.Momentum or ResourceKind.Grit))
            return;
        var perHit = _character.Gear.FuryPerHit + _character.FuryPerHitBonus;
        var gain = Kind switch
        {
            ResourceKind.Momentum => (critical ? MomentumPerCrit : MomentumPerHit) + perHit,
            ResourceKind.Devotion => taken ? DevotionWhenHit : (critical ? DevotionPerCrit : DevotionPerHit) + perHit,
            ResourceKind.Grit => (critical ? GritPerCrit : GritPerHit) + perHit,
            _ => taken ? FuryWhenHit : (critical ? FuryPerCrit : FuryPerHit) + perHit,
        };
        Current = Math.Min(Max, Current + (graze ? gain / 2 : gain));
    }

    /// <summary>
    /// The hero moved this many tiles: Momentum builds, in a fight up to the full bar and out of one only up to
    /// <see cref="MomentumRunningStart"/>.
    /// </summary>
    public void OnMoved(double tiles)
    {
        Partner?.OnMoved(tiles);
        if (Kind != ResourceKind.Momentum || tiles <= 0)
            return;
        _sinceMoved = 0;
        var cap = InCombat ? Max : Math.Max(Current, MomentumRunningStart);
        Current = Math.Min(cap, Current + tiles * MomentumPerTile);
    }

    /// <summary>Gives some back, up to the maximum (the Marksman's critical hits).</summary>
    public void Gain(double amount) => Current = Math.Min(Max, Current + Math.Max(0, amount));

    /// <summary>Spends everything there is (Finishing Blow); returns how much that was.</summary>
    public double SpendAll()
    {
        var spent = Current;
        Current = 0;
        return spent;
    }

    public void Update(double dt)
    {
        Partner?.Update(dt);
        _sinceCombat += dt;
        _sinceMoved += dt;
        if (Kind == ResourceKind.Fury)
        {
            if (_sinceCombat > FuryDecayDelay)
                Current = Math.Max(0, Current - FuryDecayPerSecond * dt);
        }
        else if (Kind == ResourceKind.Devotion)
        {
            if (_sinceCombat > DevotionDecayDelay)
                Current = Math.Max(0, Current - DevotionDecayPerSecond * dt);
        }
        else if (Kind == ResourceKind.Grit)
        {
            if (_sinceCombat > GritDecayDelay)
                Current = Math.Max(0, Current - GritDecayPerSecond * dt);
        }
        else if (Kind == ResourceKind.Momentum)
        {
            if (_sinceCombat > MomentumDecayDelay && _sinceMoved > MomentumDecayDelay)
                Current = Math.Max(0, Current - MomentumDecayPerSecond * dt);
        }
        else
        {
            Current = Math.Min(Max, Current + RegenPerSecond * dt);
        }
    }

    internal void Restore(double value) => Current = Math.Clamp(value, 0, Max);

    /// <summary>Back to the starting value: empty Fury, Momentum, Devotion and Grit, full Mana (respawning at a shrine).</summary>
    public void Reset()
    {
        Partner?.Reset();
        Current = StartsEmpty ? 0 : Max;
    }
}

public enum ResourceKind
{
    Fury,
    Mana,
    Focus,
    Momentum,
    Devotion,
    Grit,
}
