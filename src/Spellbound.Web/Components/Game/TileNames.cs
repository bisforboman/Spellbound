using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Spellbound.Domain;

namespace Spellbound.Web.Components.Game;

/// <summary>
/// Tiles by name instead of coordinates: a row letter from the top, then the tile's place in its row from the left, so
/// the top-left tile is A1. Names come from the map's shape alone, so they never change during a match. The domain
/// writes coordinates, "(0, 1)"; the pages turn them into names.
/// </summary>
public static partial class TileNames
{
    private static readonly ConditionalWeakTable<GameMap, IReadOnlyDictionary<Hex, string>> Cache = [];

    /// <summary>A tile's name, for example "C3".</summary>
    public static string Of(GameMap map, Hex hex) =>
        All(map).TryGetValue(hex, out var name) ? name : hex.ToString();

    /// <summary>A line with every tile's coordinates replaced by its name: "the Steady tile at (0, 1)" reads "the Steady
    /// tile C3", and "takes (0, 1)" reads "takes C3". Unless <paramref name="named"/>, the place is left out instead
    /// ("the Steady tile"), as in the opening, where the map shows the tile up now and names would only be clutter.</summary>
    public static string In(GameMap map, string line, bool named = true)
    {
        ArgumentNullException.ThrowIfNull(line);
        return Coordinates().Replace(line, m =>
        {
            var hex = new Hex(int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
            return !map.Contains(hex) ? m.Value
                : named ? (m.Groups[1].Success ? "tile " : "") + Of(map, hex)
                : m.Groups[1].Success ? "tile" : "the tile";
        });
    }

    private static IReadOnlyDictionary<Hex, string> All(GameMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return Cache.GetValue(map, m => m.Tiles.Select(t => t.Hex)
            .GroupBy(h => h.R).OrderBy(row => row.Key)
            .SelectMany((row, r) => row.OrderBy(h => h.Q).Select((h, i) => (h, Name: $"{(char)('A' + r)}{i + 1}")))
            .ToDictionary(x => x.h, x => x.Name));
    }

    [GeneratedRegex(@"(tile at )?\((-?\d+), (-?\d+)\)")]
    private static partial Regex Coordinates();
}
