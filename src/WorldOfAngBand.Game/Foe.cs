using WorldOfAngBand.Engine.Monsters;
using WorldOfAngBand.Game.Bridge;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Game;

/// <summary>
/// A monster in a fight: the engine's <see cref="Monster"/> (its place, hit points, speed, sleep) with its Pathfinder
/// numbers, conditions and round. Hit points stay the monster's own.
/// </summary>
public sealed class Foe(Monster monster, FoeStats stats, DamageProfile defenses, string name) : IDefender
{
    public Monster Monster { get; } = monster;

    /// <summary>One of AshenReliquary's creatures in a province pack (its strikes are its own), or null for Angband's.</summary>
    public Province.AshenFoe? Ashen { get; init; }

    public FoeStats Stats { get; } = stats;

    public string Name { get; } = name;

    public ConditionSet Conditions { get; } = new();

    public PersistentDamageSet Persistent { get; } = new();

    public DamageProfile Defenses { get; } = defenses;

    public Round Round { get; } = new();

    public int Level => Stats.Level;

    public ModifierTotal ArmorClass() => ModifierStack.Combine(
        new[] { new Modifier("Base", Stats.ArmorClass) }
            .Concat(Conditions.PenaltiesFor(StatisticKind.ArmorClass, AttributeKind.Dexterity)));

    public ModifierTotal AttackModifier() => ModifierStack.Combine(
        new[] { new Modifier("Base", Stats.AttackBonus) }
            .Concat(Conditions.PenaltiesFor(StatisticKind.Attack, AttributeKind.Strength)));

    /// <summary>The DC its blows' effects call for a save against.</summary>
    public int SaveDc => Stats.SaveDc - Conditions.Value(ConditionKind.Frightened) - Conditions.Value(ConditionKind.Sickened);
}
