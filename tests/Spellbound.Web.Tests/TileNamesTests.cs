using Spellbound.Domain;
using Spellbound.Web.Components.Game;

namespace Spellbound.Web.Tests;

public class TileNamesTests
{
    private static readonly GameMap Map = Match.Start(3, GameRules.Default, new SeededRandom(1)).Map;

    [Fact]
    public void Rows_are_letters_from_the_top_and_places_count_from_the_left()
    {
        var top = Map.Tiles.Min(t => t.Hex.R);
        var topLeft = Map.Tiles.Where(t => t.Hex.R == top).MinBy(t => t.Hex.Q)!.Hex;
        var secondRow = Map.Tiles.Where(t => t.Hex.R == top + 1).OrderBy(t => t.Hex.Q).Select(t => t.Hex).ToList();

        Assert.Equal("A1", TileNames.Of(Map, topLeft));
        Assert.Equal("B2", TileNames.Of(Map, secondRow[1]));
        Assert.Equal(Map.Tiles.Count, Map.Tiles.Select(t => TileNames.Of(Map, t.Hex)).Distinct().Count());
    }

    [Fact]
    public void A_line_names_its_tiles_and_leaves_other_numbers_alone()
    {
        var hex = Map.Tiles.First().Hex;
        var name = TileNames.Of(Map, hex);

        Assert.Equal($"You win the Steady tile {name} (yields 2) for 2 mana.",
            TileNames.In(Map, $"You win the Steady tile at {hex} (yields 2) for 2 mana."));
        Assert.Equal($"Seat 1 takes {name} with 1 Imp.", TileNames.In(Map, $"Seat 1 takes {hex} with 1 Imp."));
        Assert.Equal("Seat 1 takes (40, 40).", TileNames.In(Map, "Seat 1 takes (40, 40).")); // not on this map
    }

    [Fact]
    public void Without_names_a_line_leaves_the_place_out()
    {
        var hex = Map.Tiles.First().Hex;

        Assert.Equal("Pick the Steady tile (yields 2).", TileNames.In(Map, $"Pick the Steady tile at {hex} (yields 2).", named: false));
        Assert.Equal("Seat 1 takes (40, 40).", TileNames.In(Map, "Seat 1 takes (40, 40).", named: false)); // not on this map
    }
}
