namespace Spellbound.Domain;

/// <summary>
/// The hex board (docs/game-design.md, Map and Tiles): a round board, dark at the start, with roughly equal thirds of
/// each tile type spread at random. Everything about it comes from the injected random source, so a seed always deals
/// the same map.
/// </summary>
public sealed class GameMap
{
    private readonly Dictionary<Hex, Tile> tiles;

    private GameMap(Dictionary<Hex, Tile> tiles) => this.tiles = tiles;

    public IReadOnlyCollection<Tile> Tiles => tiles.Values;

    public int Count => tiles.Count;

    /// <summary>Deals a dark map for this many players: the board's size from the rules, and the types shuffled.</summary>
    public static GameMap Create(int players, GameRules rules, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);
        var hexes = Hex.Within(rules.MapRadius(players));
        var types = TypeMix(hexes.Count);
        Shuffle(types, random);
        return new GameMap(hexes.Select((hex, i) => new Tile(hex, types[i])).ToDictionary(t => t.Hex));
    }

    /// <summary>The types for a board of <paramref name="count"/> tiles: equal thirds, any remainder going to Steady,
    /// then Shifting (19 tiles: 7 Steady, 6 Shifting, 6 Wild).</summary>
    public static List<TileType> TypeMix(int count)
    {
        var all = Enum.GetValues<TileType>();
        return [.. all.SelectMany((type, i) => Enumerable.Repeat(type, (count / all.Length) + (i < count % all.Length ? 1 : 0)))];
    }

    public bool Contains(Hex hex) => tiles.ContainsKey(hex);

    public Tile this[Hex hex] => tiles[hex];

    /// <summary>The hexes next to this one that are on the board.</summary>
    public IEnumerable<Hex> NeighboursOf(Hex hex) => hex.Neighbours().Where(tiles.ContainsKey);

    public IEnumerable<Tile> Dark => tiles.Values.Where(t => !t.IsUnveiled);

    /// <summary>Turns a dark tile over and fixes its yield for the rest of the game: one of its type's possible
    /// yields, at random. A tile that is already unveiled, or off the board, is left as it is.</summary>
    public bool Unveil(Hex hex, GameRules rules, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);
        if (!tiles.TryGetValue(hex, out var tile) || tile.IsUnveiled)
        {
            return false;
        }

        var yields = rules.YieldsOf(tile.Type);
        tiles[hex] = tile with { Yield = yields[random.Next(yields.Count)] };
        return true;
    }

    /// <summary>
    /// Splits a set of hexes into connected groups: hexes next to each other are in the same group. Units can only
    /// move within a group (docs/game-design.md, Map). Groups come out in a fixed order (by their first hex).
    /// </summary>
    public static IReadOnlyList<IReadOnlySet<Hex>> Groups(IEnumerable<Hex> hexes)
    {
        var left = new HashSet<Hex>(hexes);
        var groups = new List<IReadOnlySet<Hex>>();
        foreach (var start in left.OrderBy(h => h.Q).ThenBy(h => h.R).ToList())
        {
            if (!left.Remove(start))
            {
                continue;
            }

            var group = new HashSet<Hex> { start };
            var frontier = new Stack<Hex>([start]);
            while (frontier.TryPop(out var hex))
            {
                foreach (var next in hex.Neighbours().Where(left.Remove))
                {
                    group.Add(next);
                    frontier.Push(next);
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    private static void Shuffle<T>(IList<T> items, IRandomSource random)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
