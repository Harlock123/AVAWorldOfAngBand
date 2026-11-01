using WorldOfAngBand.Data;
using WorldOfAngBand.Engine.Definitions;

namespace WorldOfAngBand.Tests;

/// <summary>The game data, loaded once for every test (it is never changed).</summary>
public static class TestData
{
    private static readonly Lazy<GameData> Loaded = new(() => DataLoader.LoadDefault(includeUserMods: false));

    public static GameData Data => Loaded.Value;
}
