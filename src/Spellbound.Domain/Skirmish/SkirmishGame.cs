using System.Globalization;

namespace Spellbound.Domain.Skirmish;

/// <summary>A seat's points, by source.</summary>
public sealed record SkirmishScore(int Regions, int RichRegions, int Buildings, int TerrainSet, int Cards, int Battles, int Objectives)
{
    public static SkirmishScore None { get; } = new(0, 0, 0, 0, 0, 0, 0);

    public int Total => Regions + RichRegions + Buildings + TerrainSet + Cards + Battles + Objectives;
}

/// <summary>What one round did: lines everyone sees (the board is open), and each seat's own lines.</summary>
public sealed record SkirmishRoundReport(int Round, IReadOnlyList<string> Public, IReadOnlyList<IReadOnlyList<string>> BySeat);

/// <summary>
/// A Skirmish game: a small shared board, a few seats, and rounds that everyone plans at the same time and that play out
/// together (docs/skirmish-design.md). Pure and deterministic: the clock, the ready-up and the time limit belong to the
/// caller; luck comes only from the <see cref="IRandomSource"/> passed in, so the same seed and orders replay exactly.
/// </summary>
public sealed partial class SkirmishGame
{
    private static readonly HashSet<int> NoClaims = [];

    private readonly List<SkirmishSeat> seats = [];
    private readonly List<DiscoveryCard> deck = [];
    private readonly List<ObjectiveState> objectives = [];

    /// <summary>For deserialization, and for <see cref="Start"/>.</summary>
    private SkirmishGame()
    {
        Rules = SkirmishRules.Default;
        Board = null!;
    }

    public SkirmishRules Rules { get; private set; }

    public SkirmishBoard Board { get; private set; }

    public IReadOnlyList<SkirmishSeat> Seats => seats;

    /// <summary>
    /// The cards left, in the order they'll be drawn: for replays and logs only. Pages show <see cref="DeckCounts"/>,
    /// never this, or players would know the next card.
    /// </summary>
    public IReadOnlyList<DiscoveryCard> Deck => deck;

    /// <summary>The public objectives drawn for this game, and who reached each.</summary>
    public IReadOnlyList<ObjectiveState> Objectives => objectives;

    /// <summary>Rounds played so far.</summary>
    public int Round { get; private set; }

    public bool Ended { get; private set; }

    /// <summary>
    /// Starts a game: generates the board, gives each seat its home region with the starting buildings, and shuffles the
    /// deck. <paramref name="players"/> are in seat order.
    /// </summary>
    public static SkirmishGame Start(IReadOnlyList<(string Name, MagicSchool School)> players, SkirmishRules rules, IRandomSource random)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players.Count, 2);
        var board = SkirmishBoard.Generate(players.Count, rules, random);
        var seats = players.Select((p, i) => new SkirmishSeat(i, p.Name, p.School, rules)).ToList();
        foreach (var seat in seats)
        {
            var home = board[board.Homes[seat.Index]];
            home.Owner = seat.Index;
            home.Nodes = rules.StartingNodes;
            home.Towns = rules.StartingTowns;
        }

        var game = new SkirmishGame { Rules = rules, Board = board };
        game.seats.AddRange(seats);
        var deck = game.deck;
        foreach (var (card, count) in new[]
                 {
                     (DiscoveryCard.Card1, rules.Card1Count), (DiscoveryCard.Card2, rules.Card2Count),
                     (DiscoveryCard.Card3, rules.Card3Count), (DiscoveryCard.Card4, rules.Card4Count),
                     (DiscoveryCard.Card5, rules.Card5Count),
                 })
        {
            deck.AddRange(Enumerable.Repeat(card, count));
        }

        SkirmishBoard.Shuffle(deck, random);
        var pool = Enum.GetValues<SkirmishObjective>().ToList();
        SkirmishBoard.Shuffle(pool, random);
        game.objectives.AddRange(pool.Take(rules.Objectives).Select(o => new ObjectiveState(o, [])));
        return game;
    }

    public IEnumerable<SkirmishRegion> RegionsOf(int seat) => Board.Regions.Where(r => r.Owner == seat);

    /// <summary>How many of each card are left: what players see before they claim (luck before the decision).</summary>
    public IReadOnlyDictionary<DiscoveryCard, int> DeckCounts =>
        Enum.GetValues<DiscoveryCard>().ToDictionary(c => c, c => deck.Count(d => d == c));

    /// <summary>Free regions next to one the seat holds: what it may claim.</summary>
    public IEnumerable<SkirmishRegion> Claimable(int seat) =>
        Board.Regions.Where(r => r.Owner is null && r.Neighbours.Any(n => Board[n].Owner == seat));

    /// <summary>How many actions the seat has in the next round: the usual, plus or minus what spells and hexes did.</summary>
    public int ActionsFor(int seat) => Math.Max(0, Rules.ActionsPerRound + Seats[seat].ActionBonus);

    /// <summary>
    /// The chance each claimant wins a region claimed by several seats in the same round, by seat. The weight is
    /// 10 plus the seat's attack power (times School B's spell, if running), so an army helps but never decides alone.
    /// </summary>
    public IReadOnlyDictionary<int, double> ClaimOdds(IEnumerable<int> claimants)
    {
        var weights = claimants.Distinct().ToDictionary(s => s, s => (10.0 + Seats[s].AttackPower) * Seats[s].Multiplier(EffectKind.ExploreYield));
        var total = weights.Values.Sum();
        return weights.ToDictionary(w => w.Key, w => w.Value / total);
    }

    public SkirmishScore Score(int seat)
    {
        if (Seats[seat].Out)
        {
            return SkirmishScore.None;
        }

        var held = RegionsOf(seat).ToList();
        var allTerrains = Enum.GetValues<SkirmishTerrain>().All(t => held.Any(r => r.Terrain == t));
        return new SkirmishScore(
            held.Count * Rules.PointsPerRegion,
            held.Count(r => r.Terrain is SkirmishTerrain.T3 or SkirmishTerrain.T4) * Rules.PointsPerRichRegion,
            held.Sum(r => r.Buildings) * Rules.PointsPerBuilding,
            allTerrains ? Rules.PointsForTerrainSet : 0,
            Seats[seat].CardPoints,
            Seats[seat].BattlesWon * Rules.PointsPerBattleWon,
            objectives.Count(o => o.ReachedBy.Contains(seat)) * Rules.PointsPerObjective);
    }

    /// <summary>Seats from first to last: by score, then by gold and mana together, then by seat order.</summary>
    public IReadOnlyList<SkirmishSeat> Standings() =>
        [.. Seats.OrderByDescending(s => Score(s.Index).Total).ThenByDescending(s => s.Gold + s.Mana).ThenBy(s => s.Index)];

    /// <summary>
    /// Why a seat's orders can't be accepted as they stand, or null if they can. Checked when a player saves orders;
    /// the round checks each again when it plays, since other players' orders may have changed the board by then.
    /// </summary>
    public string? CheckOrders(int seat, IReadOnlyList<SkirmishOrder> orders)
    {
        if (Ended)
        {
            return "The game is over.";
        }

        if (Seats[seat].Out)
        {
            return "You're out of this game.";
        }

        var cost = orders.Sum(o => o.Cost(Rules));
        if (cost > ActionsFor(seat))
        {
            return $"Those orders need {cost} actions; you have {ActionsFor(seat)} this round.";
        }

        // As the round would play them if nobody else got in the way: claims first (so building in a region claimed
        // the same round is fine), then builds and recruits against a running gold total.
        var claimed = new HashSet<int>();
        foreach (var claim in orders.OfType<ClaimOrder>())
        {
            if (Problem(seat, claim, claimed) is { } problem)
            {
                return problem;
            }

            claimed.Add(claim.RegionId);
        }

        var gold = Seats[seat].Gold;
        var mana = Seats[seat].Mana;
        var used = new Dictionary<int, int>();
        foreach (var order in orders.Where(o => o is not ClaimOrder))
        {
            if (Problem(seat, order, NoClaims, claimed, gold, used, mana) is { } problem)
            {
                return problem;
            }

            gold -= GoldCost(seat, order);
            mana -= ManaCost(order);
            if (order is BuildOrder build)
            {
                used[build.RegionId] = used.GetValueOrDefault(build.RegionId) + build.Count;
            }
        }

        return null;
    }

    /// <summary>
    /// Plays one round: every seat's orders at once, in a fixed order (casts, hexes, attacks, claims, then builds and
    /// recruits), then income and upkeep, objectives, and whether the game has ended. An order the round made impossible
    /// (someone else got the region first) is spent and reported; nothing else changes for it. Orders past a seat's
    /// actions are ignored. <paramref name="ready"/> are the seats whose players readied up (null: everyone); the rest
    /// still play the orders they saved, but missing rounds makes them away, then out.
    /// </summary>
    public SkirmishRoundReport PlayRound(
        IReadOnlyDictionary<int, IReadOnlyList<SkirmishOrder>> orders, IRandomSource random, IReadOnlySet<int>? ready = null)
    {
        if (Ended)
        {
            throw new InvalidOperationException("The game is over.");
        }

        Round++;
        var shared = new List<string>();
        var mine = Seats.Select(_ => new List<string>()).ToList();
        PlayAttendance(ready, shared);
        var accepted = Seats.Select(s => s.Out ? [] : Budget(s.Index, orders.GetValueOrDefault(s.Index) ?? [], mine[s.Index])).ToList();
        foreach (var seat in Seats)
        {
            seat.ActionBonus = 0; // spent; this round's spells and hexes set the next one's
        }

        PlayCasts(accepted, shared, mine, random);
        PlayAttacks(accepted, shared, mine, random);
        PlayClaims(accepted, shared, mine, random);
        foreach (var seat in Seats)
        {
            foreach (var order in accepted[seat.Index].Where(o => o is BuildOrder or RecruitOrder))
            {
                mine[seat.Index].Add(Problem(seat.Index, order, claimedThisRound: NoClaims) is { } problem
                    ? $"{Describe(order)}: not done. {problem}"
                    : Apply(seat, order));
            }
        }

        foreach (var seat in Seats.Where(s => !s.Out))
        {
            mine[seat.Index].AddRange(Income(seat));
        }

        TickEffects();
        PlayObjectives(shared);
        var leader = Seats.FirstOrDefault(s => !s.Out && RegionsOf(s.Index).Count() >= Rules.BoardShareToWin * Board.Regions.Count);
        if (Round >= Rules.Rounds || leader is not null)
        {
            Ended = true;
            var winner = Standings()[0];
            shared.Add(leader is not null
                ? $"{leader.Name} holds {Percent(Rules.BoardShareToWin)} of the board: the game ends. {winner.Name} wins with {Score(winner.Index).Total} points."
                : $"The last round is over. {winner.Name} wins with {Score(winner.Index).Total} points.");
        }

        return new SkirmishRoundReport(Round, shared, [.. mine]);
    }

    /// <summary>Whether the seat has reached the objective.</summary>
    public bool Reached(int seat, SkirmishObjective objective)
    {
        var held = RegionsOf(seat).ToList();
        return objective switch
        {
            SkirmishObjective.Regions => held.Count >= Rules.ObjectiveRegions,
            SkirmishObjective.Buildings => held.Sum(r => r.Buildings) >= Rules.ObjectiveBuildings,
            SkirmishObjective.Battles => Seats[seat].BattlesWon >= Rules.ObjectiveBattles,
            SkirmishObjective.Nodes => held.Sum(r => r.Nodes) >= Rules.ObjectiveNodes,
            SkirmishObjective.Units => Seats[seat].Units >= Rules.ObjectiveUnits,
            SkirmishObjective.TerrainSet => Enum.GetValues<SkirmishTerrain>().All(t => held.Any(r => r.Terrain == t)),
            _ => false,
        };
    }

    /// <summary>What the objective asks, for the page and the report.</summary>
    public string Describe(SkirmishObjective objective) => objective switch
    {
        SkirmishObjective.Regions => $"Hold {Rules.ObjectiveRegions} regions",
        SkirmishObjective.Buildings => $"Have {Rules.ObjectiveBuildings} buildings",
        SkirmishObjective.Battles => $"Win {Rules.ObjectiveBattles} battles",
        SkirmishObjective.Nodes => $"Have {Rules.ObjectiveNodes} nodes",
        SkirmishObjective.Units => $"Have {Rules.ObjectiveUnits} units",
        SkirmishObjective.TerrainSet => "Hold one region of every terrain",
        _ => objective.ToString(),
    };

    /// <summary>Open objectives go to every seat that reached them this round.</summary>
    private void PlayObjectives(List<string> shared)
    {
        for (var i = 0; i < objectives.Count; i++)
        {
            if (objectives[i].ReachedBy.Count > 0)
            {
                continue;
            }

            var reached = Seats.Where(s => !s.Out && Reached(s.Index, objectives[i].Objective)).Select(s => s.Index).ToList();
            if (reached.Count > 0)
            {
                objectives[i] = objectives[i] with { ReachedBy = reached };
                shared.Add($"{string.Join(" and ", reached.Select(s => Seats[s].Name))} reached \"{Describe(objectives[i].Objective)}\": +{Rules.PointsPerObjective} points.");
            }
        }
    }

    /// <summary>The orders that fit in the seat's actions, in the order given.</summary>
    private List<SkirmishOrder> Budget(int seat, IReadOnlyList<SkirmishOrder> orders, List<string> lines)
    {
        var left = ActionsFor(seat);
        var accepted = new List<SkirmishOrder>();
        foreach (var order in orders)
        {
            if (order.Cost(Rules) > left)
            {
                lines.Add($"{Describe(order)}: not done. You had no actions left.");
                continue;
            }

            left -= order.Cost(Rules);
            accepted.Add(order);
        }

        return accepted;
    }

    /// <summary>
    /// Claims, all at once: a region claimed by one seat goes to it; claimed by several, a weighted roll decides
    /// (<see cref="ClaimOdds"/>). Whether a region may be claimed is judged on the board as the round began.
    /// </summary>
    private void PlayClaims(List<List<SkirmishOrder>> accepted, List<string> shared, List<List<string>> mine, IRandomSource random)
    {
        var valid = new Dictionary<int, List<int>>();
        foreach (var seat in Seats)
        {
            var claimed = new HashSet<int>();
            foreach (var claim in accepted[seat.Index].OfType<ClaimOrder>())
            {
                if (Problem(seat.Index, claim, claimed) is { } problem)
                {
                    mine[seat.Index].Add($"{Describe(claim)}: not done. {problem}");
                    continue;
                }

                claimed.Add(claim.RegionId);
                (valid.TryGetValue(claim.RegionId, out var list) ? list : valid[claim.RegionId] = []).Add(seat.Index);
            }
        }

        foreach (var (regionId, claimants) in valid.OrderBy(v => v.Key))
        {
            var region = Board[regionId];
            var winner = claimants[0];
            if (claimants.Count > 1)
            {
                var odds = ClaimOdds(claimants);
                var roll = random.NextDouble();
                foreach (var seat in claimants)
                {
                    winner = seat;
                    roll -= odds[seat];
                    if (roll < 0)
                    {
                        break;
                    }
                }

                var names = string.Join(", ", claimants.Select(s => $"{Seats[s].Name} ({Percent(odds[s])})"));
                shared.Add($"{region.Name} was claimed by {names}. {Seats[winner].Name} got it.");
                foreach (var loser in claimants.Where(s => s != winner))
                {
                    mine[loser].Add($"You lost the contest for {region.Name}; the action is spent.");
                }
            }

            region.Owner = winner;
            shared.Add($"{Seats[winner].Name} claimed {region.Name} ({region.Terrain.DisplayName()}).");
            mine[winner].Add(Discover(Seats[winner], region));
        }
    }

    private string Discover(SkirmishSeat seat, SkirmishRegion region)
    {
        if (!Rules.Discoveries || deck.Count == 0)
        {
            return $"You claimed {region.Name}.";
        }

        var card = deck[0];
        deck.RemoveAt(0);
        var found = card switch
        {
            DiscoveryCard.Card2 => Gain(() => seat.Gold += Rules.Card2Gold, $"+{N(Rules.Card2Gold)} gold"),
            DiscoveryCard.Card3 => Gain(() => seat.Mana += Rules.Card3Mana, $"+{N(Rules.Card3Mana)} mana"),
            DiscoveryCard.Card4 => Gain(() => seat.CardPoints += Rules.Card4Points, $"+{Rules.Card4Points} points"),
            DiscoveryCard.Card5 => seat.Units == 0
                ? "it would have cost you units, but you have none"
                : $"you lost {N(seat.LoseUnits(Rules.Card5ArmyLossPercent, roundUp: true))} units",
            _ => "nothing",
        };
        return $"You claimed {region.Name} and drew {card.DisplayName()}: {found}.";

        static string Gain(Action apply, string text)
        {
            apply();
            return text;
        }
    }

    /// <summary>
    /// Why the order can't happen on the board as it is now, or null if it can. For checking a batch before the round,
    /// <paramref name="willHold"/> are regions the batch claims, <paramref name="gold"/> what's left after its earlier
    /// orders, and <paramref name="slotsUsed"/> slots its earlier builds take.
    /// </summary>
    private string? Problem(
        int seat,
        SkirmishOrder order,
        IReadOnlySet<int> claimedThisRound,
        IReadOnlySet<int>? willHold = null,
        int? gold = null,
        IReadOnlyDictionary<int, int>? slotsUsed = null,
        int? mana = null)
    {
        var who = Seats[seat];
        var available = gold ?? who.Gold;
        var manaLeft = mana ?? who.Mana;
        switch (order)
        {
            case ClaimOrder claim:
                if (claim.RegionId < 0 || claim.RegionId >= Board.Regions.Count)
                {
                    return "There is no such region.";
                }

                var region = Board[claim.RegionId];
                if (region.Owner is not null)
                {
                    return $"{region.Name} is already held.";
                }

                if (!region.Neighbours.Any(n => Board[n].Owner == seat))
                {
                    return $"{region.Name} isn't next to one of your regions.";
                }

                return claimedThisRound.Contains(claim.RegionId) ? $"You already claim {region.Name} this round." : null;

            case BuildOrder build:
                if (build.RegionId < 0 || build.RegionId >= Board.Regions.Count
                    || (Board[build.RegionId].Owner != seat && willHold?.Contains(build.RegionId) != true))
                {
                    return "You can only build in a region you hold.";
                }

                if (build.Count < 1 || build.Count > Rules.BuildingsPerOrder)
                {
                    return $"Build 1–{Rules.BuildingsPerOrder} at a time.";
                }

                var room = Board[build.RegionId].FreeSlots(Rules) - (slotsUsed?.GetValueOrDefault(build.RegionId) ?? 0);
                if (room < build.Count)
                {
                    return $"{Board[build.RegionId].Name} has room for {room} more.";
                }

                var cost = GoldCost(seat, build);
                return available < cost ? $"It costs {N(cost)} gold, but you'd have {N(available)}." : null;

            case RecruitOrder recruit:
                if (UnitBook.Find(recruit.UnitId) is not { } unit)
                {
                    return "There is no such unit.";
                }

                if (recruit.Count < 1 || recruit.Count > Rules.UnitsPerOrder)
                {
                    return $"Recruit 1–{Rules.UnitsPerOrder} at a time.";
                }

                var price = GoldCost(seat, recruit);
                return available < price ? $"It costs {N(price)} gold, but you'd have {N(available)}." : null;

            case AttackOrder attack:
                if (!Rules.Attacks)
                {
                    return "Attacks are off in this game.";
                }

                if (attack.RegionId < 0 || attack.RegionId >= Board.Regions.Count
                    || Board[attack.RegionId].Owner is not { } defender || defender == seat)
                {
                    return "You can only attack a region another mage holds.";
                }

                if (!Board[attack.RegionId].Neighbours.Any(n => Board[n].Owner == seat))
                {
                    return $"{Board[attack.RegionId].Name} isn't next to one of your regions.";
                }

                if (Rules.HomeIsSafe && Board.Homes[defender] == attack.RegionId)
                {
                    return $"{Board[attack.RegionId].Name} is {Seats[defender].Name}'s home: homes can't be attacked.";
                }

                if (RegionsOf(defender).Count() * 100 < RegionsOf(seat).Count() * Rules.AttackRangePercent)
                {
                    return $"{Seats[defender].Name} is too small to attack: they need at least {Rules.AttackRangePercent}% of your regions.";
                }

                return who.AttackPower == 0 ? "You have no army to attack with." : null;

            case CastOrder cast:
                if (SpellBook.Find(cast.SpellId) is not { } spell || (spell.School is { } school && school != who.School))
                {
                    return "You don't know that spell.";
                }

                var spellCost = SkirmishSpells.ManaCost(spell, Rules);
                return manaLeft < spellCost ? $"It costs {N(spellCost)} mana, but you'd have {N(manaLeft)}." : null;

            case HexOrder hex:
                if (!Rules.Hexes)
                {
                    return "Hexes are off in this game.";
                }

                if (SpellBook.FindHex(hex.HexId) is not { } known || known.School != who.School)
                {
                    return "You don't know that hex.";
                }

                if (hex.TargetSeat < 0 || hex.TargetSeat >= Seats.Count || hex.TargetSeat == seat || Seats[hex.TargetSeat].Out)
                {
                    return "Choose another mage still in the game.";
                }

                return manaLeft < Rules.HexMana ? $"It costs {N(Rules.HexMana)} mana, but you'd have {N(manaLeft)}." : null;

            default:
                return "That order isn't known.";
        }
    }

    private int ManaCost(SkirmishOrder order) => order switch
    {
        CastOrder c when SpellBook.Find(c.SpellId) is { } spell => SkirmishSpells.ManaCost(spell, Rules),
        HexOrder => Rules.HexMana,
        _ => 0,
    };

    private int GoldCost(int seat, SkirmishOrder order) => order switch
    {
        BuildOrder b => (int)Math.Ceiling(b.Count * Rules.BuildingGoldCost * Seats[seat].Multiplier(EffectKind.BuildCost)),
        RecruitOrder r when UnitBook.Find(r.UnitId) is { } unit => r.Count * unit.GoldCost,
        _ => 0,
    };

    private string Apply(SkirmishSeat seat, SkirmishOrder order)
    {
        switch (order)
        {
            case BuildOrder build:
                var region = Board[build.RegionId];
                var cost = GoldCost(seat.Index, build);
                seat.Gold -= cost;
                if (build.Type == BuildingType.Node)
                {
                    region.Nodes += build.Count;
                }
                else
                {
                    region.Towns += build.Count;
                }

                return $"You built {build.Count} {(build.Type == BuildingType.Node ? "node" : "town")}{(build.Count == 1 ? "" : "s")} in {region.Name} for {N(cost)} gold.";

            case RecruitOrder recruit:
                var unit = UnitBook.Find(recruit.UnitId)!;
                seat.Gold -= recruit.Count * unit.GoldCost;
                seat.Army[unit.Id] = seat.Army.GetValueOrDefault(unit.Id) + recruit.Count;
                return $"You recruited {N(recruit.Count)} {unit.Name} for {N(recruit.Count * unit.GoldCost)} gold.";

            default:
                throw new InvalidOperationException($"{order} is played elsewhere.");
        }
    }

    /// <summary>Gold and mana from the seat's regions, then unit upkeep. Unpaid upkeep costs all gold and some units.</summary>
    private List<string> Income(SkirmishSeat seat)
    {
        var lines = new List<string>();
        var held = RegionsOf(seat.Index).ToList();
        var gold = (int)(held.Sum(r => Rules.GoldPerRegion
                                       + (r.Towns * Rules.GoldPerTown * (r.Terrain == SkirmishTerrain.T2 ? Rules.Terrain2TownGold : 1)))
                         * seat.Multiplier(EffectKind.GoldIncome));
        var mana = (int)(held.Sum(r => r.Nodes * Rules.ManaPerNode * (r.Terrain == SkirmishTerrain.T3 ? Rules.Terrain3NodeMana : 1))
                         * seat.Multiplier(EffectKind.ManaIncome));
        seat.Gold += gold;
        seat.Mana += mana;
        lines.Add($"Your regions brought in {N(gold)} gold and {N(mana)} mana.");

        var upkeep = seat.UpkeepPerRound(Rules);
        if (upkeep > 0 && seat.Gold >= upkeep)
        {
            seat.Gold -= upkeep;
            lines.Add($"Your army's upkeep was {N(upkeep)} gold.");
        }
        else if (upkeep > 0)
        {
            seat.Gold = 0;
            var deserted = seat.LoseUnits(Rules.DesertionPercent, roundUp: true);
            lines.Add($"You couldn't pay your army's upkeep of {N(upkeep)} gold: {N(deserted)} units deserted.");
        }

        return lines;
    }

    private string Describe(SkirmishOrder order) => order switch
    {
        ClaimOrder c when c.RegionId >= 0 && c.RegionId < Board.Regions.Count => $"Claim {Board[c.RegionId].Name}",
        ClaimOrder => "Claim",
        BuildOrder b => $"Build {b.Count} {(b.Type == BuildingType.Node ? "node" : "town")}{(b.Count == 1 ? "" : "s")}",
        RecruitOrder r => $"Recruit {r.Count} {UnitBook.Find(r.UnitId)?.Name ?? r.UnitId}",
        AttackOrder a when a.RegionId >= 0 && a.RegionId < Board.Regions.Count => $"Attack {Board[a.RegionId].Name}",
        AttackOrder => "Attack",
        CastOrder c => $"Cast {SpellBook.Find(c.SpellId)?.Name ?? c.SpellId}",
        HexOrder h => $"Cast {SpellBook.FindHex(h.HexId)?.Name ?? h.HexId}",
        _ => order.GetType().Name,
    };

    private static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Percent(double share) => share.ToString("0%", CultureInfo.InvariantCulture);
}
