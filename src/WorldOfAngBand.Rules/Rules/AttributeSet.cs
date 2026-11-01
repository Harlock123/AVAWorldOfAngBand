namespace WorldOfAngBand.Rules.Rules;

/// <summary>
/// Attribute modifiers in the Pathfinder Remaster style: every attribute starts at +0, a boost adds +1
/// and a flaw subtracts 1. Once an attribute is at +4 or higher a boost is only a <em>partial</em> boost,
/// and two partial boosts make +1.
/// </summary>
public sealed class AttributeSet
{
    /// <summary>From this modifier upwards a boost is partial.</summary>
    public const int PartialBoostThreshold = 4;

    /// <summary>The highest modifier a level-1 character may start with.</summary>
    public const int CreationCap = 4;

    /// <summary>The levels that grant four attribute boosts.</summary>
    public static readonly IReadOnlyList<int> BoostLevels = [5, 10, 15, 20];

    public const int BoostsPerLevelUp = 4;

    private readonly int[] _modifiers = new int[6];
    private readonly bool[] _partial = new bool[6];

    public int this[AttributeKind kind] => _modifiers[(int)kind];

    /// <summary>True when one partial boost is banked on this attribute, waiting for its pair.</summary>
    public bool HasPartialBoost(AttributeKind kind) => _partial[(int)kind];

    public void Boost(AttributeKind kind)
    {
        var i = (int)kind;
        if (_modifiers[i] < PartialBoostThreshold)
        {
            _modifiers[i]++;
        }
        else if (_partial[i])
        {
            _modifiers[i]++;
            _partial[i] = false;
        }
        else
        {
            _partial[i] = true;
        }
    }

    public void Flaw(AttributeKind kind) => _modifiers[(int)kind]--;

    /// <summary>Applies the four boosts a character gains at level 5, 10, 15 or 20. Each must go to a different attribute.</summary>
    public void ApplyLevelUpBoosts(int level, IReadOnlyCollection<AttributeKind> boosts)
    {
        if (!BoostLevels.Contains(level))
            throw new RulesException($"Level {level} does not grant attribute boosts.");
        if (boosts.Count != BoostsPerLevelUp)
            throw new RulesException($"A level-up grants exactly {BoostsPerLevelUp} boosts, not {boosts.Count}.");
        if (boosts.Distinct().Count() != boosts.Count)
            throw new RulesException("Level-up boosts must go to different attributes.");
        foreach (var kind in boosts)
            Boost(kind);
    }

    /// <summary>Takes on another set's values (a respec back to the level-1 attributes).</summary>
    public void CopyFrom(AttributeSet other)
    {
        Array.Copy(other._modifiers, _modifiers, 6);
        Array.Copy(other._partial, _partial, 6);
    }

    public AttributeSet Clone()
    {
        var copy = new AttributeSet();
        Array.Copy(_modifiers, copy._modifiers, 6);
        Array.Copy(_partial, copy._partial, 6);
        return copy;
    }

    public override string ToString() =>
        string.Join(" ", Enum.GetValues<AttributeKind>().Select(k => $"{k.Abbreviation()} {this[k]:+0;-0;+0}"));
}
