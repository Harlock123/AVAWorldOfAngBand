using System.Globalization;
using System.Text.RegularExpressions;

namespace WorldOfAngBand.Rules.Rules;

/// <summary>A dice expression such as <c>2d6+3</c>.</summary>
public readonly partial record struct Dice(int Count, int Sides, int Bonus = 0)
{
    public int Min => Count + Bonus;
    public int Max => Count * Sides + Bonus;
    public double Average => Count * (Sides + 1) / 2.0 + Bonus;

    public int Roll(IRandomSource random) => RollDiceOnly(random) + Bonus;

    /// <summary>The sum of the dice alone, without the flat bonus.</summary>
    public int RollDiceOnly(IRandomSource random)
    {
        var total = 0;
        for (var i = 0; i < Count; i++)
            total += random.Roll(1, Sides);
        return total;
    }

    public static Dice Parse(string text)
    {
        var match = DicePattern().Match(text.Replace(" ", ""));
        if (!match.Success)
            throw new FormatException($"'{text}' is not a dice expression like 2d6+3.");
        var count = match.Groups["count"].Success ? int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture) : 1;
        var sides = int.Parse(match.Groups["sides"].Value, CultureInfo.InvariantCulture);
        var bonus = match.Groups["bonus"].Success ? int.Parse(match.Groups["bonus"].Value, CultureInfo.InvariantCulture) : 0;
        return new Dice(count, sides, bonus);
    }

    public override string ToString() =>
        Bonus == 0 ? $"{Count}d{Sides}" : $"{Count}d{Sides}{Bonus:+0;-0}";

    [GeneratedRegex(@"^(?<count>\d+)?d(?<sides>\d+)(?<bonus>[+-]\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DicePattern();
}
