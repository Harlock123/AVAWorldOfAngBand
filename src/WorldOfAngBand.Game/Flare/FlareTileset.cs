namespace WorldOfAngBand.Game.Flare;

/// <summary>
/// One tile in a Flare tileset: its source rectangle in <see cref="Image"/> and the anchor (the diamond's
/// centre) relative to that rectangle. Animated tiles (water) carry their extra frames.
/// </summary>
public sealed record FlareTile(int Id, string Image, int X, int Y, int Width, int Height, int OffsetX, int OffsetY)
{
    public IReadOnlyList<(int X, int Y, int DurationMs)> Animation { get; init; } = [];
}

/// <summary>A Flare <c>tilesetdefs/*.txt</c> file: <c>img=</c> starts a new image, <c>tile=id,x,y,w,h,ox,oy</c> lines follow.</summary>
public sealed class FlareTileset
{
    private FlareTileset(Dictionary<int, FlareTile> tiles) => Tiles = tiles;

    public IReadOnlyDictionary<int, FlareTile> Tiles { get; }

    public IEnumerable<string> Images => Tiles.Values.Select(t => t.Image).Distinct();

    public static FlareTileset Parse(string text)
    {
        var tiles = new Dictionary<int, FlareTile>();
        string? image = null;
        foreach (var (_, key, value) in FlareText.Entries(text))
        {
            switch (key)
            {
                case "img":
                    image = value;
                    break;
                case "tile" when image is not null:
                    var v = FlareText.Ints(value);
                    tiles[v[0]] = new FlareTile(v[0], image, v[1], v[2], v[3], v[4], v[5], v[6]);
                    break;
                case "animation":
                    var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var id = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                    if (!tiles.TryGetValue(id, out var tile))
                        break;
                    var frames = parts.Skip(1).Select(p =>
                    {
                        var f = p.Split(',');
                        return (int.Parse(f[0], System.Globalization.CultureInfo.InvariantCulture),
                            int.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture),
                            FlareText.Duration(f[2]));
                    }).ToList();
                    tiles[id] = tile with { Animation = frames };
                    break;
            }
        }
        return new FlareTileset(tiles);
    }
}
