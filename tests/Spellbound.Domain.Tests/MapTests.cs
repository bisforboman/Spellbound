namespace Spellbound.Domain.Tests;

// The hex board and its tiles (docs/game-design.md, Map, Tiles and Unveil).
public class MapTests
{
    private static readonly GameRules Rules = GameRules.Default;

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 19)]
    [InlineData(3, 37)]
    public void A_round_board_has_the_hexes_within_its_radius(int radius, int count)
    {
        var hexes = Hex.Within(radius);

        Assert.Equal(count, hexes.Count);
        Assert.Equal(count, hexes.Distinct().Count());
        Assert.All(hexes, h => Assert.True(h.DistanceTo(Hex.Centre) <= radius));
    }

    [Fact]
    public void A_hex_has_six_neighbours_each_one_step_away()
    {
        var hex = new Hex(1, -1);

        var neighbours = hex.Neighbours().ToList();

        Assert.Equal(6, neighbours.Distinct().Count());
        Assert.All(neighbours, n => Assert.True(hex.IsNextTo(n)));
        Assert.False(hex.IsNextTo(hex));
        Assert.Equal(3, new Hex(-2, 0).DistanceTo(new Hex(1, 0)));
    }

    [Theory]
    [InlineData(2, 19)]
    [InlineData(3, 19)]
    [InlineData(4, 37)]
    [InlineData(5, 37)]
    public void The_board_size_follows_the_player_count(int players, int tiles)
    {
        Assert.Equal(tiles, GameMap.Create(players, Rules, new SeededRandom(1)).Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void Two_to_five_players(int players)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameMap.Create(players, Rules, new SeededRandom(1)));
    }

    [Theory]
    [InlineData(19, 7, 6, 6)]
    [InlineData(37, 13, 12, 12)]
    public void The_types_are_roughly_equal_thirds(int count, int steady, int shifting, int wild)
    {
        var mix = GameMap.TypeMix(count);

        Assert.Equal(steady, mix.Count(t => t == TileType.Steady));
        Assert.Equal(shifting, mix.Count(t => t == TileType.Shifting));
        Assert.Equal(wild, mix.Count(t => t == TileType.Wild));
    }

    [Fact]
    public void A_new_map_is_dark_and_has_the_mix()
    {
        var map = GameMap.Create(3, Rules, new SeededRandom(7));

        Assert.All(map.Tiles, t => Assert.False(t.IsUnveiled));
        Assert.Equal(19, map.Dark.Count());
        Assert.Equal(7, map.Tiles.Count(t => t.Type == TileType.Steady));
    }

    [Fact]
    public void The_same_seed_deals_the_same_map_and_the_same_yields()
    {
        static IEnumerable<(Hex, TileType, int?)> Deal(int seed)
        {
            var random = new SeededRandom(seed);
            var map = GameMap.Create(3, Rules, random);
            foreach (var hex in Hex.Within(2))
            {
                map.Unveil(hex, Rules, random);
            }

            return map.Tiles.OrderBy(t => t.Hex.Q).ThenBy(t => t.Hex.R).Select(t => (t.Hex, t.Type, t.Yield)).ToList();
        }

        Assert.Equal(Deal(42), Deal(42));
        Assert.NotEqual(Deal(42), Deal(43));
    }

    [Fact]
    public void Unveiling_fixes_a_yield_from_the_tiles_type()
    {
        var random = new SeededRandom(3);
        var map = GameMap.Create(3, Rules, random);

        foreach (var tile in map.Tiles.ToList())
        {
            Assert.True(map.Unveil(tile.Hex, Rules, random));
        }

        Assert.All(map.Tiles, t => Assert.Contains(t.Yield!.Value, Rules.YieldsOf(t.Type)));
        Assert.All(map.Tiles.Where(t => t.Type == TileType.Steady), t => Assert.Equal(2, t.Yield));
        Assert.Empty(map.Dark);
    }

    [Fact]
    public void Unveiling_a_tile_again_or_off_the_board_changes_nothing()
    {
        var random = new SeededRandom(3);
        var map = GameMap.Create(3, Rules, random);
        map.Unveil(Hex.Centre, Rules, random);
        var before = map[Hex.Centre];

        Assert.False(map.Unveil(Hex.Centre, Rules, random));
        Assert.False(map.Unveil(new Hex(5, 0), Rules, random));
        Assert.Equal(before, map[Hex.Centre]);
        Assert.Equal(18, map.Dark.Count());
    }

    [Fact]
    public void Yields_come_from_the_games_rules()
    {
        var rules = Rules with { SteadyYields = [5], ShiftingYields = [5], WildYields = [5] };
        var random = new SeededRandom(3);
        var map = GameMap.Create(2, rules, random);

        map.Unveil(Hex.Centre, rules, random);

        Assert.Equal(5, map[Hex.Centre].Yield);
    }

    [Fact]
    public void Scattered_hexes_form_groups_and_a_hex_between_them_joins_them()
    {
        var west = new Hex(-2, 0);
        var east = new Hex(2, 0);
        var middle = new[] { new Hex(-1, 0), Hex.Centre, new Hex(1, 0) };

        var apart = GameMap.Groups([west, east, middle[0]]);
        var joined = GameMap.Groups([west, east, .. middle]);

        Assert.Equal(2, apart.Count);
        Assert.Equal([west, middle[0]], apart[0].OrderBy(h => h.Q));
        Assert.Single(joined);
        Assert.Equal(5, joined[0].Count);
        Assert.Empty(GameMap.Groups([]));
    }

    [Fact]
    public void The_neighbours_on_the_board_stop_at_its_edge()
    {
        var map = GameMap.Create(3, Rules, new SeededRandom(1));

        Assert.Equal(6, map.NeighboursOf(Hex.Centre).Count());
        Assert.Equal(3, map.NeighboursOf(new Hex(2, 0)).Count()); // a corner of the radius-2 board
    }
}
