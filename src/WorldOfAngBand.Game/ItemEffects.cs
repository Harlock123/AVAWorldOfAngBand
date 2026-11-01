using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Engine.Items;
using WorldOfAngBand.Engine.World;
using WorldOfAngBand.Rules.Combat;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.Game;

/// <summary>
/// What drinking a potion or reading a scroll does, as data/bridge/items.json's "use" says: a comma-separated list of
/// effects (heal:2d8+5, cure:Blinded, blink:10, recall…), with Angband's roles in Pathfinder's terms. Using an item makes
/// its kind known, as in Angband.
/// </summary>
internal sealed class ItemEffects(DungeonSession session)
{
    private Hero Hero => session.Hero;

    /// <summary>Drinks or reads one of <paramref name="item"/>. False when it can't be used (and no turn passes).</summary>
    public bool Use(Item item, Item? target = null)
    {
        if (session.ItemBridge.UseOf(item.Kind) is not { } effects)
        {
            session.Say($"You can't use {session.Belongings.Name(item)}.");
            return false;
        }
        var reading = item.Base.Id == "scroll";
        if (reading && Hero.Character.Conditions.Has(ConditionKind.Blinded))
        {
            session.Say("You can't see to read.");
            return false;
        }
        var wasKnown = session.Knowledge.IsAware(item.Kind);
        var used = Hero.Pack.Remove(item, 1, () => session.Factory.NextSerial++);
        session.Say(reading ? $"You read {session.Belongings.Name(used)}." : $"You drink {session.Belongings.Name(used)}.");
        Hero.Pack.Prune();
        session.Knowledge.MarkTried(used.Kind);
        foreach (var effect in effects.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            Apply(effect, target);
        if (!wasKnown)
        {
            session.Knowledge.LearnKind(used.Kind);
            session.Say($"It was {session.Belongings.Name(used)}.");
        }
        session.Belongings.Refit();
        session.Publish(new ItemsChangedEvent());
        return true;
    }

    private void Apply(string effect, Item? target)
    {
        var parts = effect.Split(':');
        var c = Hero.Character;
        switch (parts[0])
        {
            case "heal":
                var amount = parts[1].EndsWith('%')
                    ? c.MaxHitPoints * int.Parse(parts[1][..^1], System.Globalization.CultureInfo.InvariantCulture) / 100
                    : Dice.Parse(parts[1]).Roll(session.Roller);
                c.Heal(amount);
                session.Say(c.HitPoints >= c.MaxHitPoints ? "You feel very good." : "You feel better.");
                break;
            case "cure":
                if (parts[1] == "poison")
                {
                    if (c.Persistent.Types.Contains(DamageType.Poison))
                    {
                        c.Persistent.Remove(DamageType.Poison);
                        session.Say("You are no longer poisoned.");
                    }
                }
                else if (Enum.TryParse<ConditionKind>(parts[1], out var kind) && c.Conditions.Has(kind))
                {
                    c.Conditions.Remove(kind);
                    session.Say($"You are no longer {kind.ToString().ToLowerInvariant()}.");
                }
                break;
            case "haste":
                Timed("haste", int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) + session.Rng.RandInt1(25), "You feel yourself moving faster!");
                break;
            case "heroism":
                c.Heal(10);
                c.Conditions.Remove(ConditionKind.Frightened);
                Timed("heroism", Turns(parts[1]), "You feel like a hero!");
                break;
            case "berserk":
                c.Heal(15);
                c.Conditions.Remove(ConditionKind.Frightened);
                Timed("berserk", Turns(parts[1]), "You feel like a killing machine!");
                break;
            case "bless":
                Timed("bless", Turns(parts[1]), "You feel righteous!");
                break;
            case "resist":
                Timed($"resist:{parts[1]}", Turns(parts[2]), $"You feel resistant to {parts[1].ToLowerInvariant()}!");
                break;
            case "blink":
                session.Teleport(int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
                break;
            case "light":
                session.LightArea(Hero.Position, 3);
                session.Say("You are surrounded by a white light.");
                break;
            case "map":
                session.MapArea(int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
                session.Say("You sense the dungeon around you.");
                break;
            case "detect_treasure":
                session.DetectObjects(int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
                break;
            case "recall":
                if (Hero.RecallIn > 0)
                {
                    Hero.RecallIn = 0;
                    session.Say("A tension leaves the air around you...");
                }
                else
                {
                    Hero.RecallIn = 15 + session.Rng.RandInt1(20);
                    session.Say("The air about you becomes charged...");
                }
                break;
            case "deep_descent":
                Hero.DescentIn = 5;
                session.Say("The floor opens beneath you...");
                break;
            case "teleport_level":
                session.TeleportLevel();
                break;
            case "enchant":
                Enchant(parts[1], target);
                break;
            case "identify":
                Identify(target);
                break;
            case "bad":
                Bad(parts[1]);
                break;
        }
    }

    private int Turns(string text) => int.Parse(text, System.Globalization.CultureInfo.InvariantCulture) + session.Rng.RandInt1(25);

    private void Timed(string effect, int turns, string message)
    {
        Hero.Timed[effect] = Math.Max(Hero.Timed.GetValueOrDefault(effect), turns);
        session.Say(message);
    }

    /// <summary>
    /// Angband's enchant scrolls: one point of to-hit, to-dam or to-AC on the weapon or body armour (or the item asked
    /// for), less likely to take the higher it already is (Angband enchant_table, roughly).
    /// </summary>
    private void Enchant(string what, Item? target)
    {
        var item = target ?? (what == RuneIds.ToAc ? Hero.Pack.InSlot(EquipSlot.Body) : Hero.Pack.Weapon);
        if (item is null)
        {
            session.Say("You have nothing to enchant.");
            return;
        }
        var current = what switch { RuneIds.ToHit => item.ToHit, RuneIds.ToDam => item.ToDam, _ => item.ToAc };
        var chance = current < 0 ? 100 : current >= 15 ? 1 : 100 - current * 6;
        if (session.Rng.RandInt0(100) >= chance)
        {
            session.Say("The enchantment failed.");
            return;
        }
        switch (what)
        {
            case RuneIds.ToHit: item.ToHit++; break;
            case RuneIds.ToDam: item.ToDam++; break;
            default: item.ToAc++; break;
        }
        session.Say($"{session.Belongings.Name(item)} glows brightly!");
    }

    /// <summary>Angband's Identify Rune: one unknown rune of the item asked for (or the first worn item with one).</summary>
    private void Identify(Item? target)
    {
        var item = target ?? Hero.Pack.Equipped.Concat(Hero.Pack.Pack).FirstOrDefault(i => session.Knowledge.UnknownRunes(i).Any());
        if (item is null || session.Knowledge.UnknownRunes(item).FirstOrDefault() is not { } rune)
        {
            session.Say("You have nothing to identify.");
            return;
        }
        session.Belongings.Learn(rune);
    }

    /// <summary>The bad potions and scrolls, which teach the hero what they are the hard way.</summary>
    private void Bad(string what)
    {
        var c = Hero.Character;
        switch (what)
        {
            case "Stunned" when Hero.Gear.Protects("free_act"):
                session.Belongings.Learn(RuneIds.Resist("free_act"));
                session.Say("You yawn, and shake it off.");
                break;
            case "Stunned":
                c.Conditions.Apply(ConditionKind.Stunned, 1, 2);
                session.Say("You fall asleep.");
                break;
            case "Stupefied":
                c.Conditions.Apply(ConditionKind.Stupefied, 1, 6);
                session.Say("You are confused!");
                break;
            case "Blinded":
                c.Conditions.Apply(ConditionKind.Blinded, 1, 4);
                session.Say("You are blind!");
                break;
            case "Slowed":
                c.Conditions.Apply(ConditionKind.Slowed, 1, 10);
                session.Say("You feel yourself moving slower!");
                break;
            case "poison":
                c.Persistent.Add(DamageType.Poison, new Dice(1, 4));
                session.Say("You are poisoned!");
                break;
            case "salt":
                session.Say("The potion makes you vomit!");
                break;
            case "darkness":
                session.DarkenArea(Hero.Position, 3);
                if (!Hero.Gear.Protects("blind"))
                    c.Conditions.Apply(ConditionKind.Blinded, 1, 3);
                session.Say("Darkness surrounds you.");
                break;
            case "aggravate":
                session.Aggravate();
                session.Say("There is a high pitched humming noise.");
                break;
            case "summon":
                session.SummonNear(Hero.Position, 2 + session.Rng.RandInt0(2));
                break;
        }
    }
}
