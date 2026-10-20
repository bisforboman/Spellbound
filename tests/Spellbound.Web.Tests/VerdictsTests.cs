using Spellbound.Web.Components.Game;

namespace Spellbound.Web.Tests;

public class VerdictsTests
{
    private const string Attack = "Seat 1 (4 Imps) attack the Steady tile at (0, 1) (yields 2), Seat 2's, held by 1 Golem: power 4 against 3.";

    [Fact]
    public void Taking_a_tile_is_conquered_and_stamped_after_the_last_line_of_its_battle()
    {
        string[] lines = ["Round 3.", Attack, "Seat 1 takes (0, 1) with 1 Imp.", "Seat 1 banks 5 points."];

        var verdicts = Verdicts.Find(lines, you: 0);

        Assert.Equal(new Verdict("Conquered", true), Assert.Single(verdicts, v => v.Key == 2).Value);
        Assert.Single(verdicts);
    }

    [Fact]
    public void A_failed_attack_is_repelled_for_the_attacker_and_held_for_the_defender()
    {
        string[] lines = [Attack.Replace("power 4 against 3", "power 2 against 3", StringComparison.Ordinal), "The attack fails: the attackers are wiped out. The defenders keep 1 Golem."];

        Assert.Equal(new Verdict("Repelled", false), Verdicts.Find(lines, you: 0)[1]);
        Assert.Equal(new Verdict("Held", true), Verdicts.Find(lines, you: 1)[1]);
    }

    [Fact]
    public void A_defender_who_loses_the_tile_has_lost_it()
    {
        string[] lines = [Attack, "Seat 1 takes (0, 1) with 1 Imp."];

        Assert.Equal(new Verdict("Lost", false), Verdicts.Find(lines, you: 1)[1]);
    }

    [Fact]
    public void A_tie_that_takes_nothing_thwarts_the_attacker_and_nobody_takes_is_not_a_capture()
    {
        string[] lines =
        [
            "Seat 1 (3 Imps) and Seat 3 (3 Imps) attack the tile, Seat 2's, held by no units: power 6 against 0.",
            "The attackers fight each other for it: Seat 1 with 3 Imps, Seat 3 with 3 Imps.",
            "The strongest are evenly matched and wipe each other out, with the rest: nobody takes the tile.",
        ];

        Assert.Equal(new Verdict("Thwarted", false), Verdicts.Find(lines, you: 0)[2]);
        Assert.Equal(new Verdict("Held", true), Verdicts.Find(lines, you: 1)[2]);
    }

    [Fact]
    public void Battles_between_others_get_no_stamp()
    {
        Assert.Empty(Verdicts.Find([Attack, "Seat 1 takes (0, 1) with 1 Imp."], you: 2));
    }
}
