using WorldOfAngBand.Game;
using WorldOfAngBand.Game.Bots;

namespace WorldOfAngBand.Tests;

/// <summary>
/// The spec's M5: the balance bots clear it. A hero made ready for a depth (Bots/Campaign) dives three levels from it,
/// and a hero ready for the bottom goes down through Saint Ysmere and the Cinder of Ashur. If a change moves these,
/// look at why (Campaign.Dive and Campaign.Finale over more seeds show the spread) before changing a number here.
/// </summary>
public class BalanceTests
{
    [Theory]
    [InlineData(5, 3)]
    [InlineData(30, 3)]
    [InlineData(60, 3)]
    [InlineData(90, 2)]
    public void A_hero_ready_for_a_depth_lives_through_three_levels_of_it(int depth, int mostOfFour)
    {
        var results = Enumerable.Range(1, 4).Select(seed => Campaign.Dive(TestData.Data, (ulong)seed, depth)).ToList();
        Assert.True(results.Count(r => !r.Died) >= mostOfFour,
            $"From depth {depth}: {string.Join("; ", results.Select(r => r.Died ? $"killed by {r.Killer} at {r.Reached}" : $"lived to {r.Reached}"))}");
        Assert.Contains(results, r => r.Reached >= depth + 3);
    }

    [Fact]
    public void A_hero_ready_for_the_bottom_can_put_out_the_cinder()
    {
        var results = Enumerable.Range(1, 4).Select(seed => Campaign.Finale(TestData.Data, (ulong)seed)).ToList();
        Assert.True(results.Count(r => r.Won) >= 2,
            string.Join("; ", results.Select(r => r.Won ? "won" : r.Died ? $"killed by {r.Killer} at {r.Reached}" : $"still at {r.Reached}")));
        Assert.All(results.Where(r => r.Won), r => Assert.Equal(Story.CinderDepth, r.Reached));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 5)]
    [InlineData(30, 10)]
    [InlineData(60, 16)]
    [InlineData(90, 20)]
    public void The_hero_is_expected_to_be_the_specs_level_at_a_depth(int depth, int level) =>
        Assert.Equal(level, Campaign.ExpectedLevel(depth));

    [Theory]
    [InlineData("drowned_chapel")]
    [InlineData("hollow_woods")]
    [InlineData("wyrms_nest")]
    public void A_hero_of_a_zones_level_walks_it_pack_by_pack(string zone)
    {
        var results = Enumerable.Range(1, 2).Select(seed => Campaign.Province(TestData.Data, (ulong)seed, zone, 2000)).ToList();
        Assert.True(results.Count(r => !r.Died) >= 1, string.Join("; ", results.Select(r => r.Died ? $"killed by {r.Killer}" : $"{r.Reached} packs left")));
        Assert.Contains(results, r => !r.Died && r.Reached <= 3);
    }
}
