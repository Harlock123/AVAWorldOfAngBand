using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.World;
using WorldOfAngBand.Game;
using WorldOfAngBand.Rules.Content;

namespace WorldOfAngBand.Tests;

public class ChallengeTests
{
    private static readonly ContentDatabase Content = ContentDatabase.Embedded;

    private static DungeonSession Week(string twist)
    {
        var birth = new Birth(Challenge: new Challenge(Challenges.Weekly, "2026-W44", twist));
        return new DungeonSession(TestData.Data, 91, birth: birth);
    }

    [Fact]
    public void Everyone_gets_the_same_dungeon_and_hero_on_the_same_day()
    {
        var day = new DateOnly(2026, 11, 1);
        var (seed, birth) = Challenges.DailyFor(day, Content, "Vessa");
        var (again, birthAgain) = Challenges.DailyFor(day, Content, "Vessa");
        Assert.Equal(seed, again);
        Assert.Equal(birth.Character!.ClassId, birthAgain.Character!.ClassId);
        Assert.NotEqual(seed, Challenges.DailyFor(day.AddDays(1), Content, "Vessa").Seed);
        Assert.True(Powers.IsPlayable(Content, birth.Character.ClassId));
        Assert.Equal("2026-11-01", birth.Challenge!.Stamp);
    }

    [Fact]
    public void A_twist_never_comes_two_weeks_running_and_all_seven_come_in_seven_weeks()
    {
        var monday = new DateOnly(2026, 1, 5);
        var twists = Enumerable.Range(0, 7).Select(w => Challenges.TwistFor(Challenges.WeekStamp(monday.AddDays(7 * w))).Id).ToList();
        Assert.Equal(7, twists.Distinct().Count());
        for (var i = 1; i < twists.Count; i++)
            Assert.NotEqual(twists[i - 1], twists[i]);
        Assert.Equal("2026-W44", Challenges.WeekStamp(new DateOnly(2026, 11, 1)));
    }

    [Fact]
    public void Glass_cannon_makes_every_blow_land_half_again_as_hard()
    {
        var game = Week("glass");
        var hp = game.Hero.HitPoints;
        game.HurtHero(4, "a test");
        Assert.Equal(hp - 6, game.Hero.HitPoints);
    }

    [Fact]
    public void Thin_flasks_leave_two_healing_flasks()
    {
        Assert.Equal(2, Week("flasks").Hero.Character.Potion.Charges);
        Assert.Equal(3, Week("gold").Hero.Character.Potion.Charges);
    }

    [Fact]
    public void Lean_times_shut_every_shop_but_the_quartermaster()
    {
        var game = Week("lean");
        var shop = game.Stores["alchemist"];
        game.Hero.Gold = 10_000;
        Assert.False(game.Stores.Buy(shop, shop.Stock.First()));
        var general = game.Stores["general"];
        Assert.True(game.Stores.Buy(general, general.Stock.First()));
    }

    [Fact]
    public void Darkness_puts_out_every_light_in_the_deep()
    {
        var game = Week("darkness");
        game.JumpTo(5);
        Assert.DoesNotContain(game.Level.AllLocs(), p => game.Level[p].Has(SquareFlags.Glow) && !game.Level.FeatureAt(p).Has(TerrainFlags.Bright));
    }

    [Fact]
    public void Restless_foes_are_quicker()
    {
        var game = Week("restless");
        Assert.NotNull(game.Scheduler.SpeedBonus);
    }
}
