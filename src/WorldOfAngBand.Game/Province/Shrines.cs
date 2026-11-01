using WorldOfAngBand.Engine.Randomness;

namespace WorldOfAngBand.Game.Province;

/// <summary>
/// Ashur's old waystones (the spec's section 4.4): a shrine is used once, and offers three blessings drawn by Ashur's
/// whim from six: healing, cleansing, a rune learned, a blessing, heroism, or a ward against the elements.
/// </summary>
public static class Shrines
{
    public static readonly IReadOnlyDictionary<string, string> Blessings = new Dictionary<string, string>
    {
        ["heal"] = "Mend: every wound closes.",
        ["cleanse"] = "Cleanse: every condition and lingering harm lifts.",
        ["rune"] = "Insight: what you wear shows its runes to you.",
        ["bless"] = "Blessing: Ashur's favour for a while (+1 to attack and AC).",
        ["heroism"] = "Courage: heroism, and no fear, for a while.",
        ["ward"] = "Ward: fire, cold, lightning and acid touch you less for a while.",
    };

    /// <summary>Three blessings, drawn by Ashur's whim.</summary>
    public static IReadOnlyList<string> Offer(GameRandom rng) =>
        Blessings.Keys.OrderBy(_ => rng.RandInt0(1000)).Take(3).ToList();

    internal static void Bless(DungeonSession game, string blessing)
    {
        var c = game.Hero.Character;
        switch (blessing)
        {
            case "heal":
                c.Heal(c.MaxHitPoints);
                game.Say("Warmth runs through you, and every wound closes.");
                break;
            case "cleanse":
                foreach (var condition in c.Conditions.Active.Select(a => a.Kind).ToList())
                    c.Conditions.Remove(condition);
                c.Persistent.Clear();
                game.Say("Whatever clung to you lets go.");
                break;
            case "rune":
                foreach (var item in game.Hero.Pack.Equipped)
                    game.Belongings.LearnAll(item);
                game.Say("The stone shows you what you carry, rune by rune.");
                break;
            case "bless":
                game.Effects.ApplyEffect("bless:60");
                break;
            case "heroism":
                game.Effects.ApplyEffect("heroism:60");
                break;
            case "ward":
                foreach (var element in new[] { "Fire", "Cold", "Electricity", "Acid" })
                    game.Effects.ApplyEffect($"resist:{element}:60");
                break;
        }
        game.Belongings.Refit();
    }
}
