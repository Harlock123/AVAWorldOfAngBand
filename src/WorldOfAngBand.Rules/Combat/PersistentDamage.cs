using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Rules.Combat;

/// <summary>
/// Bleed, burning, poison and the like. Each ticks once a round (three actions, a second and a half of the game's
/// clock), then a DC 15 flat check (a 30% chance) ends it. A new effect of the same type replaces the old one only if its average damage is higher.
/// </summary>
public sealed class PersistentDamageSet
{
    public const double TickSeconds = 1.5;
    public const int EndFlatCheckDc = 15;

    private readonly Dictionary<DamageType, (Dice Dice, double Timer)> _effects = [];

    public IReadOnlyCollection<DamageType> Types => _effects.Keys;

    public void Add(DamageType type, Dice dice)
    {
        if (_effects.TryGetValue(type, out var existing) && existing.Dice.Average >= dice.Average)
            return;
        _effects[type] = (dice, existing.Timer);
    }

    public void Remove(DamageType type) => _effects.Remove(type);

    public void Clear() => _effects.Clear();

    /// <summary>The dice of one running effect (for tooltips), or null.</summary>
    public Dice? DiceFor(DamageType type) => _effects.TryGetValue(type, out var e) ? e.Dice : null;

    /// <summary>Advances time and returns the damage dealt by every tick that came due, before defences.</summary>
    public IReadOnlyList<(DamageType Type, int Amount)> Update(double deltaSeconds, IRandomSource random)
    {
        var ticks = new List<(DamageType, int)>();
        foreach (var type in _effects.Keys.ToList())
        {
            var (dice, timer) = _effects[type];
            timer += deltaSeconds;
            var ended = false;
            while (timer >= TickSeconds && !ended)
            {
                timer -= TickSeconds;
                ticks.Add((type, dice.Roll(random)));
                ended = random.FlatCheck(EndFlatCheckDc);
            }
            if (ended)
                _effects.Remove(type);
            else
                _effects[type] = (dice, timer);
        }
        return ticks;
    }
}
