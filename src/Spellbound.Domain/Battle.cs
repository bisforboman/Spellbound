namespace Spellbound.Domain;

/// <summary>
/// The battle rules (docs/game-design.md, Battles): no dice. The higher total wins; both sides lose power equal to the
/// smaller total, weakest units first, and a side stops losing units before one would take it past that loss.
/// </summary>
public static class Battle
{
    /// <summary>What is left of one or more seats' units after losing this much power, weakest units first (seat
    /// order between equals). A loss at least the side's whole power wipes it out.</summary>
    public static IReadOnlyList<(int Seat, Army Army)> TakeLosses(
        IReadOnlyList<(int Seat, Army Army)> side, int loss, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(rules);
        var left = side.ToList();
        var groups = side.SelectMany((s, i) => new[] { (i, UnitKind.Imp, s.Army.Imps), (i, UnitKind.Golem, s.Army.Golems) })
            .Where(g => g.Item3 > 0)
            .OrderBy(g => rules.PowerOf(g.Item2)).ThenBy(g => side[g.Item1].Seat).ThenBy(g => g.Item2);
        foreach (var (i, kind, count) in groups)
        {
            var power = rules.PowerOf(kind);
            var removed = power == 0 ? count : (int)Math.Min(count, (long)loss / power);
            left[i] = (left[i].Seat, left[i].Army - Army.Of(kind, removed));
            loss -= removed * power;
            if (removed < count)
            {
                break;
            }
        }

        return left;
    }

    /// <summary>One army after losing this much power, weakest units first.</summary>
    public static Army TakeLosses(Army army, int loss, GameRules rules) => TakeLosses([(0, army)], loss, rules)[0].Army;
}
