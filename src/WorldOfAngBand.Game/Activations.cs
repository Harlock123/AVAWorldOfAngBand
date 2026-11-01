using System.Globalization;
using WorldOfAngBand.Game.Bridge;
using WorldOfAngBand.Rules.Abilities;
using WorldOfAngBand.Rules.Combat;

namespace WorldOfAngBand.Game;

/// <summary>
/// What an artifact's activation does here (the spec's section 6.3: its activation as an ability): Angband's effect
/// string read into the potion and scroll effects the game has (<see cref="ItemEffects"/>), and an attack as a one-off
/// ability. A bolt or beam is a spell attack, a ball a burst on its target, a dispel a burst round the hero; their
/// damage is Angband's, scaled for the artifact's depth as a monster's is. Effects with no counterpart yet (probing,
/// banishment, recharging, stone to mud) are left out; an activation with none left has nothing to do.
/// </summary>
public sealed record Activation(IReadOnlyList<string> Effects, AbilityDefinition? Attack)
{
    public bool IsEmpty => Effects.Count == 0 && Attack is null;

    /// <summary>Reads an Angband activation ("ball:cold:100:2", "heal:40:0; cure:blind") for an artifact of <paramref name="level"/>.</summary>
    public static Activation Parse(string? text, int level, string name)
    {
        var effects = new List<string>();
        AbilityDefinition? attack = null;
        var scale = CreatureBridge.DamageScale(Math.Max(1, level));
        foreach (var part in (text ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var p = part.Split(':');
            switch (p[0])
            {
                case "bolt" or "beam" when p.Length >= 3:
                    attack ??= new AbilityDefinition(name, name, "item", "activation", AbilityKind.SpellBolt, Actions: 1, Range: 10,
                        DamageType: TypeOf(p[1]), Dice: Scaled(p[2], scale), Traits: ["Attack"]);
                    break;
                case "drain_life" when p.Length >= 2:
                    attack ??= new AbilityDefinition(name, name, "item", "activation", AbilityKind.SpellBolt, Actions: 1, Range: 10,
                        DamageType: DamageType.Void, Dice: Scaled(p[1], scale), Drain: 0.5, Traits: ["Attack"]);
                    break;
                case "ball" when p.Length >= 3:
                    attack ??= new AbilityDefinition(name, name, "item", "activation", AbilityKind.Blast, Actions: 1, Range: 10,
                        Radius: p.Length >= 4 && double.TryParse(p[3], CultureInfo.InvariantCulture, out var r) ? Math.Max(1, r) : 2,
                        Save: "Reflex", DamageType: TypeOf(p[1]), Dice: Scaled(p[2], scale));
                    break;
                case "dispel" when p.Length >= 3:
                    attack ??= new AbilityDefinition(name, name, "item", "activation", AbilityKind.Burst, Actions: 1, Radius: 5,
                        Save: "Will", DamageType: DamageType.Spirit, Dice: Scaled(p[2], scale));
                    break;
                case "heal" when p.Length >= 2 && int.TryParse(p[1], out var heal):
                    effects.Add(heal >= 1000 ? "heal:100%" : heal >= 500 ? "heal:50%" : $"heal:{Math.Max(1, (int)Math.Round(heal * scale))}");
                    break;
                case "cure" when p.Length >= 2 && Cure(p[1]) is { } cure:
                    effects.Add($"cure:{cure}");
                    break;
                case "restore_exp":
                    effects.Add("cure:Drained");
                    break;
                case "timed" when p.Length >= 2 && Timed(p[1]) is { } timed:
                    effects.Add(timed);
                    break;
                case "light_area":
                    effects.Add("light");
                    break;
                case "map_area" or "enlightenment":
                    effects.Add("map:30");
                    break;
                case "detect_objects" or "detect_gold":
                    effects.Add("detect_treasure:20");
                    break;
                case "teleport" when p.Length >= 2:
                    effects.Add($"blink:{(int.TryParse(p[1].TrimStart('M'), out var range) ? range : 10)}");
                    break;
                case "recall":
                    effects.Add("recall");
                    break;
            }
        }
        return new Activation(effects.Distinct().ToList(), attack);
    }

    /// <summary>Angband's damage ("9d8", "100") scaled for depth, as dice the Rules read.</summary>
    private static string Scaled(string text, double scale)
    {
        var d = text.IndexOf('d');
        if (d > 0 && int.TryParse(text[..d], out var count))
            return $"{Math.Max(1, (int)Math.Round(count * scale))}d{text[(d + 1)..]}";
        var amount = int.TryParse(text, out var flat) ? Math.Max(1, (int)Math.Round(flat * scale)) : 10;
        return $"1d1+{amount - 1}";
    }

    private static DamageType TypeOf(string element) => element switch
    {
        "fire" => DamageType.Fire,
        "cold" or "ice" => DamageType.Cold,
        "elec" => DamageType.Electricity,
        "acid" => DamageType.Acid,
        "pois" => DamageType.Poison,
        "nether" => DamageType.Void,
        "light" or "holy_orb" => DamageType.Vitality,
        "sound" => DamageType.Sonic,
        _ => DamageType.Force,
    };

    private static string? Cure(string what) => what switch
    {
        "blind" => "Blinded",
        "confused" => "Stupefied",
        "poisoned" => "poison",
        "stun" => "Stunned",
        "afraid" => "Frightened",
        _ => null,
    };

    private static string? Timed(string what) => what switch
    {
        "fast" => "haste:20",
        "berserk" => "berserk:25",
        "blessed" or "prot_evil" => "bless:24",
        "oppose_acid" => "resist:Acid:30",
        "oppose_elec" => "resist:Electricity:30",
        "oppose_fire" => "resist:Fire:30",
        "oppose_cold" => "resist:Cold:30",
        "oppose_pois" => "resist:Poison:30",
        _ => null,
    };
}
