using System.Globalization;

namespace WorldOfAngBand.Game.Flare;

/// <summary>Helpers for Flare's plain-text data files: <c>[section]</c> headers, <c>key=value</c> lines, <c>#</c> comments.</summary>
internal static class FlareText
{
    public static IEnumerable<(string? Section, string Key, string Value)> Entries(string text)
    {
        string? section = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq > 0)
                yield return (section, line[..eq], line[(eq + 1)..]);
        }
    }

    public static int[] Ints(string value, char separator = ',') =>
        value.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();

    /// <summary>"800ms" or "1s" in milliseconds.</summary>
    public static int Duration(string value)
    {
        value = value.Trim();
        if (value.EndsWith("ms", StringComparison.Ordinal))
            return int.Parse(value[..^2], CultureInfo.InvariantCulture);
        if (value.EndsWith('s'))
            return (int)(double.Parse(value[..^1], CultureInfo.InvariantCulture) * 1000);
        return int.Parse(value, CultureInfo.InvariantCulture);
    }
}
