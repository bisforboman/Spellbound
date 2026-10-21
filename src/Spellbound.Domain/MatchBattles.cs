namespace Spellbound.Domain;

/// <summary>Attacks, battles and the later Unveils (docs/game-design.md, Unveil, Battles, Taking a rival's
/// tiles).</summary>
public sealed partial class Match
{
    /// <summary>Reveals the tiles still dark, split evenly over the later Unveil rounds that are left (an odd one goes
    /// to the earlier round). Each revealed tile is guarded by monsters on a roll of the guard die.</summary>
    private void LaterUnveil(IRandomSource random, ReportLines lines)
    {
        var left = Rules.LaterUnveilRounds.Count(r => r >= CurrentRound);
        if (!Rules.LaterUnveilRounds.Contains(CurrentRound) || left == 0)
        {
            return;
        }

        var dark = Map.Dark.Select(t => t.Hex).OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
        var count = (dark.Count + left - 1) / left;
        var revealed = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var pick = random.Next(dark.Count);
            var hex = dark[pick];
            dark.RemoveAt(pick);
            Map.Unveil(hex, Rules, random);
            if (random.Next(Rules.GuardDieFaces) < Rules.GuardedFaces)
            {
                Map.SetGuard(hex, Rules.MonsterPower);
            }

            revealed.Add(Describe(hex));
        }

        lines.Add($"An Unveil reveals {string.Join("; ", revealed)}.");
    }

    private string? AttackProblem(Orders orders, Hex target, List<string> lines, Dictionary<Hex, Army> positions)
    {
        var seat = seats[orders.Seat];
        var owner = OwnerOf(target);
        if (owner == seat.Index)
        {
            return $"{target} is already yours.";
        }

        if (owner is null && !Map[target].IsGuarded)
        {
            return $"{target} isn't a rival's tile or guarded by monsters; a free tile is attuned, not attacked.";
        }

        if (orders.Attackers.Count == 0)
        {
            return "An attack needs units to send.";
        }

        var sent = Army.None;
        var used = new Dictionary<Hex, Army>();
        foreach (var go in orders.Attackers)
        {
            if (go.To != target || !seat.Tiles.Contains(go.From) || !go.From.IsNextTo(target))
            {
                return $"Attackers must come from your tiles next to {target}.";
            }

            var there = positions.TryGetValue(go.From, out var p) ? p : UnitsOn(go.From);
            var total = (used.TryGetValue(go.From, out var u) ? u : Army.None) + go.Units;
            if (!go.Units.IsValid || go.Units.IsEmpty || !there.Holds(total))
            {
                return $"There aren't {go.Units} at {go.From} to attack with.";
            }

            used[go.From] = total;
            sent += go.Units;
        }

        var rally = orders.Cast is { Spell: Spell.Rally } r && r.Target == target ? Rules.RallyPower : 0;
        var attack = sent.Power(Rules) + rally;
        var defence = DefencePower(target);

        // Your own spell on the target lands before the attack: count it.
        var afterSpell = orders.Cast switch
        {
            { Spell: Spell.Scorch } scorch when scorch.Target == target && owner is not null =>
                (Power: Battle.TakeLosses(UnitsOn(target), scorch.Count, Rules).Power(Rules) + Ward(target) + Tower(target), Note: " after your Scorch"),
            { Spell: Spell.Lure } lure when lure.Target == target && owner is null =>
                (Power: Math.Max(Rules.MinGuard, Map[target].Guard - Rules.LurePower), Note: " after your Lure"),
            _ => (Power: defence, Note: ""),
        };
        defence = afterSpell.Power;
        var outcome = attack > defence
            ? owner is { } o && seats[o].Tiles.Count == 1
                ? $"you win, but it's their last tile: the defenders die and the tile stays theirs, and your {Battle.TakeLosses(sent, Math.Max(0, defence - rally), Rules)} go back"
                : $"you win and take it, keeping {Battle.TakeLosses(sent, Math.Max(0, defence - rally), Rules)}"
            : attack == defence
                ? owner is null ? "a tie: both sides are wiped out and you take the tile" : "a tie: both sides are wiped out and the tile stays theirs"
                : "you lose everything you sent";
        lines.Add($"Attack {DescribePlain(target)} ({DefenceText(target)}, power {defence}{afterSpell.Note}) with {sent} (power {attack}{(rally > 0 ? $", with your Rally" : "")}). "
                  + $"As things stand, {outcome}. Units that attack don't defend their own tiles this round.");
        return null;
    }

    private int DefencePower(Hex target) =>
        OwnerOf(target) is null ? Map[target].Guard : UnitsOn(target).Power(Rules) + Ward(target) + Tower(target);

    private string DefenceText(Hex target) =>
        OwnerOf(target) is { } owner ? $"Seat {owner + 1}'s, held by {UnitsOn(target)}" : $"guarded by monsters, power {Map[target].Guard}";

    /// <summary>
    /// Every attacking unit leaves its tile first, so it doesn't defend it. Then each attacked tile has one battle, in
    /// board order: all its attackers together against the defenders (or the monster guards). If the attackers win,
    /// the strongest surviving attacker takes the tile and loses as much power as the next strongest had; the rest are
    /// wiped out, and a tie at the top leaves the tile with the defender. A rival's last tile can't be taken: its
    /// defenders die and the winner's units go back where they came from (or dissolve if that tile was lost).
    /// </summary>
    private void ResolveAttacks(List<Orders> attacks, ReportLines lines)
    {
        // An attack goes with the units still there: a rival's Scorch may have burned some since the orders were given.
        attacks = [.. attacks.Select(o => o with
        {
            Attackers = [.. o.Attackers.GroupBy(go => go.From).Select(from =>
            {
                var sent = from.Aggregate(Army.None, (a, go) => a + go.Units);
                var there = UnitsOn(from.Key);
                return new Move(from.Key, o.Target!.Value, new Army(Math.Min(sent.Imps, there.Imps), Math.Min(sent.Golems, there.Golems)));
            }).Where(go => !go.Units.IsEmpty)],
        })];
        foreach (var empty in attacks.Where(o => o.Attackers.Count == 0))
        {
            lines.Add($"Seat {empty.Seat + 1}'s attack on {empty.Target} has no units left to send.", empty.Seat, empty.Target!.Value);
        }

        attacks = [.. attacks.Where(o => o.Attackers.Count > 0)];
        foreach (var go in attacks.SelectMany(o => o.Attackers))
        {
            units[go.From] = UnitsOn(go.From) - go.Units;
        }

        foreach (var battle in attacks.GroupBy(o => o.Target!.Value).OrderBy(g => g.Key.Q).ThenBy(g => g.Key.R))
        {
            Fight(battle.Key, [.. battle.OrderBy(o => o.Seat)], lines);
        }
    }

    private void Fight(Hex target, List<Orders> attacks, ReportLines lines)
    {
        var owner = OwnerOf(target);
        var side = attacks.Select(o => (o.Seat, Army: o.Attackers.Aggregate(Army.None, (a, m) => a + m.Units))).ToList();
        var rally = side.Sum(s => rallies.GetValueOrDefault(s.Seat));
        var attack = side.Sum(s => s.Army.Power(Rules)) + rally;
        var defence = DefencePower(target);
        var who = string.Join(" and ", side.Select(s => $"Seat {s.Seat + 1} ({s.Army}{(rallies.ContainsKey(s.Seat) ? ", rallied" : "")})"));
        lines.Add($"{who} attack {DescribePlain(target)}, {DefenceText(target)}: power {attack} against {defence}.", null, target);

        if (attack < defence)
        {
            if (owner is not null)
            {
                // A Ward takes the blows first: the defenders lose only what gets past it.
                // A Ward and a Tower take the blows first: the defenders lose only what gets past them.
                units[target] = Battle.TakeLosses(UnitsOn(target), Math.Max(0, attack - Ward(target) - Tower(target)), Rules);
            }

            lines.Add($"The attack fails: the attackers are wiped out. The defenders keep {(owner is null ? "the tile" : UnitsOn(target).ToString())}.", null, target);
            return;
        }

        // The defenders are wiped out either way.
        ClearDefenders(target, owner);
        if (attack == defence)
        {
            lines.Add("A tie: both sides are wiped out.", null, target);
            if (owner is null)
            {
                // A tie against monsters gives the attacker the tile; among several, the one who sent the most.
                var top = side.GroupBy(s => s.Army.Power(Rules)).MaxBy(g => g.Key)!.ToList();
                if (top is [var taker])
                {
                    Conquer(target, taker.Seat, Army.None, defence > 0, lines);
                }
                else
                {
                    lines.Add("The attackers sent the same power, so nobody takes the tile.", null, target);
                }
            }

            return;
        }

        // A Rally takes the blows first: the attackers lose only what gets past it.
        var survivors = Battle.TakeLosses(side, Math.Max(0, defence - rally), Rules).Where(s => !s.Army.IsEmpty).ToList();
        if (survivors.Count > 1)
        {
            lines.Add($"The attackers fight each other for it: {string.Join(", ", survivors.Select(s => $"Seat {s.Seat + 1} with {s.Army}"))}.", null, target);
            var ranked = survivors.OrderByDescending(s => s.Army.Power(Rules)).ToList();
            var (best, next) = (ranked[0].Army.Power(Rules), ranked[1].Army.Power(Rules));
            if (best == next)
            {
                lines.Add("The strongest are evenly matched and wipe each other out, with the rest: nobody takes the tile.", null, target);
                return;
            }

            survivors = [(ranked[0].Seat, Battle.TakeLosses(ranked[0].Army, next, Rules))];
        }

        var (seat, army) = survivors[0];
        if (owner is { } defender && seats[defender].Tiles.Count == 1)
        {
            var home = attacks.Single(o => o.Seat == seat).Attackers.Select(m => m.From)
                .Where(seats[seat].Tiles.Contains).OrderBy(h => h.Q).ThenBy(h => h.R).Cast<Hex?>().FirstOrDefault();
            if (home is { } back)
            {
                units[back] = UnitsOn(back) + army;
            }

            lines.Add($"It's Seat {defender + 1}'s last tile: the defenders die but the tile stays theirs. Seat {seat + 1}'s {army} "
                      + (home is null ? "have nowhere to go back to and dissolve." : $"go back to {home}."), null, target);
            return;
        }

        Conquer(target, seat, army, defence > 0, lines);
    }

    private void ClearDefenders(Hex target, int? owner)
    {
        if (owner is null)
        {
            Map.SetGuard(target, 0);
        }
        else
        {
            units[target] = Army.None;
        }
    }

    /// <summary>A seat takes a tile; it banks <see cref="GameRules.PointsPerConquest"/> if the tile was defended (units,
    /// a Ward, a Tower or monster guards), else <see cref="GameRules.PointsPerEmptyConquest"/>.</summary>
    private void Conquer(Hex target, int seat, Army army, bool defended, ReportLines lines)
    {
        if (OwnerOf(target) is { } loser)
        {
            seats[loser].Lose(target);
            seats[seat].RivalTilesTaken++;
        }
        else
        {
            seats[seat].MonstersCleared++;
        }

        seats[seat].Take(target);
        seats[seat].ConquestPointsThisRound += defended ? Rules.PointsPerConquest : Rules.PointsPerEmptyConquest;
        units[target] = army;
        lines.Add($"Seat {seat + 1} takes {target}" + (army.IsEmpty ? "" : $" with {army}") + (defended ? "." : ", undefended."), seat, target);
    }
}
