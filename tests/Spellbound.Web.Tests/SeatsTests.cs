using Spellbound.Web.Components.Game;

namespace Spellbound.Web.Tests;

public class SeatsTests
{
    private static readonly string?[] Owners = ["alice", null, null];

    [Theory]
    [InlineData("Seat 1 wins the Steady tile at (0, 1) (yields 2) for 2 mana.", "You win the Steady tile at (0, 1) (yields 2) for 2 mana.")]
    [InlineData("Seat 1 collects 4 mana.", "You collect 4 mana.")]
    [InlineData("Seat 1 attunes the Wild tile at (1, 0) (yields 3) for 4 mana.", "You attune the Wild tile at (1, 0) (yields 3) for 4 mana.")]
    [InlineData("Seat 2 summons 3 Imps at (1, 1).", "Bot 2 summons 3 Imps at (1, 1).")]
    [InlineData("Seat 1 and Seat 3 both attune the tile: nobody gets it.", "You and Bot 3 both attune the tile: nobody gets it.")]
    [InlineData("Seat 1 (3 Imps) attack the tile, Seat 2's, held by 1 Golem: power 3 against 3.", "You (3 Imps) attack the tile, Bot 2's, held by 1 Golem: power 3 against 3.")]
    [InlineData("It's Seat 1's last tile: the defenders die but the tile stays theirs. Seat 2's 2 Imps go back.", "It's your last tile: the defenders die but the tile stays theirs. Bot 2's 2 Imps go back.")]
    [InlineData("The match is over. Seat 1 wins.", "The match is over. You win.")]
    [InlineData("The match is over. Seat 1 and Seat 3 share the win.", "The match is over. You and Bot 3 share the win.")]
    [InlineData("Seat 1 can't pay for 1 Golem: they dissolve.", "You can't pay for 1 Golem: they dissolve.")]
    [InlineData("Seat 10 is not seat 1.", "Seat 10 is not seat 1.")]
    public void A_report_line_reads_in_the_players_words(string line, string phrased)
    {
        Assert.Equal(phrased, Seats.Phrase(line, Owners, 0));
    }

    [Fact]
    public void Another_persons_seat_keeps_its_number()
    {
        Assert.Equal("Seat 2 collects 3 mana.", Seats.Phrase("Seat 2 collects 3 mana.", ["alice", "bob"], 0));
    }
}
