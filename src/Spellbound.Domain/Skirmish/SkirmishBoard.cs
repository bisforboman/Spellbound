namespace Spellbound.Domain.Skirmish;

/// <summary>What a region is good for. The members are stored, so they stay neutral; see <see cref="SkirmishTerrainExtensions.DisplayName"/>.</summary>
public enum SkirmishTerrain
{
    /// <summary>An extra building slot.</summary>
    T1,

    /// <summary>More gold from its towns.</summary>
    T2,

    /// <summary>More mana from its nodes, and extra points.</summary>
    T3,

    /// <summary>Stronger defense, and extra points.</summary>
    T4,
}

public static class SkirmishTerrainExtensions
{
    /// <summary>The name players see; a placeholder (docs/game-design.md, "Names to decide").</summary>
    public static string DisplayName(this SkirmishTerrain terrain) => $"Terrain {(int)terrain + 1}";
}

/// <summary>One region of the board: its terrain, its neighbours, who holds it and what's built on it.</summary>
public sealed class SkirmishRegion
{
    public SkirmishRegion(int id, string name, SkirmishTerrain terrain)
    {
        Id = id;
        Name = name;
        Terrain = terrain;
    }

    /// <summary>For deserialization.</summary>
    private SkirmishRegion()
    {
        Name = string.Empty;
    }

    public int Id { get; private set; }

    public string Name { get; private set; }

    public SkirmishTerrain Terrain { get; private set; }

    public List<int> Neighbours { get; } = [];

    /// <summary>The seat holding it, or null while it's free.</summary>
    public int? Owner { get; internal set; }

    public int Nodes { get; internal set; }

    public int Towns { get; internal set; }

    public int Buildings => Nodes + Towns;

    public int Slots(SkirmishRules rules) => rules.SlotsPerRegion + (Terrain == SkirmishTerrain.T1 ? rules.Terrain1ExtraSlots : 0);

    public int FreeSlots(SkirmishRules rules) => Slots(rules) - Buildings;
}

/// <summary>The regions and their links. Generated from the game's seed, so a game can be replayed.</summary>
public sealed class SkirmishBoard
{
    private readonly List<SkirmishRegion> regions = [];
    private readonly List<int> homes = [];

    /// <summary>For deserialization, and for <see cref="Generate"/>.</summary>
    private SkirmishBoard()
    {
    }

    public IReadOnlyList<SkirmishRegion> Regions => regions;

    /// <summary>Each seat's starting region, by seat.</summary>
    public IReadOnlyList<int> Homes => homes;

    public SkirmishRegion this[int id] => regions[id];

    /// <summary>
    /// A new board for <paramref name="players"/>: the regions in a ring (so every region can be reached), plus extra
    /// links at random. Homes are spread evenly around the ring, are never linked to each other and all have the same
    /// terrain (T1). The rest of the terrains are dealt out evenly.
    /// </summary>
    public static SkirmishBoard Generate(int players, SkirmishRules rules, IRandomSource random)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players, 2);
        var count = players * rules.RegionsPerPlayer;
        var board = new SkirmishBoard();
        var homes = board.homes;
        homes.AddRange(Enumerable.Range(0, players).Select(p => p * count / players));

        var others = new List<SkirmishTerrain>();
        for (var i = 0; others.Count < count - players; i++)
        {
            others.Add((SkirmishTerrain)(i % 4));
        }

        Shuffle(others, random);
        var regions = board.regions;
        var next = 0;
        for (var id = 0; id < count; id++)
        {
            regions.Add(new SkirmishRegion(id, $"Region {id + 1}", homes.Contains(id) ? SkirmishTerrain.T1 : others[next++]));
        }

        for (var id = 0; id < count; id++)
        {
            Link(regions, id, (id + 1) % count);
        }

        var extra = (int)Math.Round(count * rules.ExtraLinks);
        for (var tries = 0; extra > 0 && tries < count * 20; tries++)
        {
            var a = (int)(random.NextDouble() * count);
            var b = (int)(random.NextDouble() * count);
            if (a != b && !regions[a].Neighbours.Contains(b) && !(homes.Contains(a) && homes.Contains(b)))
            {
                Link(regions, a, b);
                extra--;
            }
        }

        foreach (var region in regions)
        {
            region.Neighbours.Sort();
        }

        return board;
    }

    /// <summary>Fisher–Yates with the game's random source.</summary>
    internal static void Shuffle<T>(IList<T> items, IRandomSource random)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = (int)(random.NextDouble() * (i + 1));
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private static void Link(List<SkirmishRegion> regions, int a, int b)
    {
        if (!regions[a].Neighbours.Contains(b))
        {
            regions[a].Neighbours.Add(b);
            regions[b].Neighbours.Add(a);
        }
    }
}
