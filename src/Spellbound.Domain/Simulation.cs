using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Match = Spellbound.Domain.Match;

namespace Spellbound.Domain;

/// <summary>
/// Plays many seeded bot-against-bot matches and measures what the paper prototype asks to watch for
/// (docs/skirmish-paper-prototype.md, "Watch for"). Pure and deterministic: the same arguments give the same report.
/// </summary>
public static partial class Simulation
{
    [GeneratedRegex(@"\((\d+) for land, (\d+) for mana(?:, (\d+) for conquest)?\)")]
    private static partial Regex BankedRegex();

    private static Regex BankedLine => BankedRegex();

    [GeneratedRegex(@"^Seat (\d+) banks .*\((\d+) for land, (\d+) for mana(?:, (\d+) for conquest)?\)")]
    private static partial Regex SeatBankedRegex();

    /// <summary>
    /// Bots with different plans against each other (docs/discussions/strategies.md): with 5 players, the five
    /// specialists, one each; with fewer, a draw of plans from all six (the everyday bot included). Plans are dealt to
    /// seats from their own seeded stream, so seat order doesn't decide; traits are drafted at random as usual.
    /// </summary>
    public static SimulationReport RunPlans(int players, int games, int seed, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(games);
        var report = new SimulationReport(players, games, seed, rules.Rounds);
        IRandomSource deal = new SeededRandom(unchecked(seed * 31 + 17));
        var specialists = Enum.GetValues<Plan>().Where(p => p != Plan.Balanced).ToList();
        var everyone = Enum.GetValues<Plan>().ToList();
        for (var game = 0; game < games; game++)
        {
            var pool = (players == specialists.Count ? specialists : everyone).ToList();
            var plans = new Plan[players];
            for (var seat = 0; seat < players; seat++)
            {
                var pick = deal.Next(pool.Count);
                plans[seat] = pool[pick];
                if (pool.Count > 1)
                {
                    pool.RemoveAt(pick);
                }
            }

            Play(Match.Start(players, rules, new SeededRandom(seed + game * 7919)), new SeededRandom(seed - game * 104729), report, plans);
        }

        return report;
    }

    public static SimulationReport Run(int players, int games, int seed, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(games);
        var report = new SimulationReport(players, games, seed, rules.Rounds);
        for (var game = 0; game < games; game++)
        {
            Play(Match.Start(players, rules, new SeededRandom(seed + game * 7919)), new SeededRandom(seed - game * 104729), report);
        }

        return report;
    }

    private static void Play(Match match, IRandomSource random, SimulationReport report, Plan[]? plans = null)
    {
        Plan PlanOf(int seat) => plans?[seat] ?? Plan.Balanced;
        var sources = match.Seats.Select(_ => new int[3]).ToArray(); // land, mana, conquest
        foreach (var seat in match.Seats)
        {
            if (Bot.ChooseTrait(match, seat.Index, random) is { } trait)
            {
                match.ChooseTrait(seat.Index, trait);
            }
        }

        while (match.Phase == MatchPhase.Opening)
        {
            Resolved(match.Draft(Bot.ChoosePick(match, match.Picker!.Value, random)!, random));
        }

        report.ManaAfterOpening.AddRange(match.Seats.Select(s => s.Mana));
        while (match.Phase == MatchPhase.Rounds)
        {
            var number = match.CurrentRound;
            var round = report.Rounds[number - 1];
            round.FreeTiles += match.Map.Tiles.Count(t => t.IsUnveiled && !t.IsGuarded && match.OwnerOf(t.Hex) is null);
            round.PricedOut += match.Seats.Count(s => PricedOut(match, s.Index));
            var lines = new List<string>();
            while (match.Phase == MatchPhase.Rounds && match.CurrentRound == number)
            {
                var actor = match.Actor!.Value;
                var picking = match.IsPicking(actor);
                var order = Bot.ChooseOrders(match, actor, random, PlanOf(actor));
                if (picking)
                {
                    round.Picks[order.Stance.ToString()]++;
                }

                round.Stances[order.Stance == Stance.Channel ? "Channel" : order.Target is null ? "Pass" : order.Stance.ToString()]++;
                if (order is { Stance: Stance.Cast, Target: not null })
                {
                    report.Casts[order.Spell.ToString()] = report.Casts.GetValueOrDefault(order.Spell.ToString()) + 1;
                }

                if (order.Stance == Stance.Summon && order.Target is not null)
                {
                    (order.Kind == UnitKind.Golem ? report.GolemsSummoned : report.ImpsSummoned).Add(order.Count);
                }

                // What the action costs, the picker's discount off its stance's part.
                var spell = match.SpellCostOf(order);
                var stanceCost = match.CostOf(order) - spell;
                var off = picking ? match.Rules.PickerDiscount : 0;
                var spellPaid = Math.Max(0, spell - off);
                var stancePaid = Math.Max(0, stanceCost - Math.Max(0, off - spell));
                var owners = match.Map.Tiles.ToDictionary(t => t.Hex, t => (Owner: match.OwnerOf(t.Hex), t.IsGuarded));
                lines.AddRange(Resolved(match.Take(order, random)));
                if (match.Offer is { } offer)
                {
                    var pays = Bot.Pays(match, actor);
                    round.AttuneDeclines += pays ? 0 : 1;
                    report.ManaSpent["Attune"] = report.ManaSpent.GetValueOrDefault("Attune") + (pays ? offer.Price : 0);
                    lines.AddRange(Resolved(match.Settle(actor, pays, random)));
                }
                report.ManaSpent["Spell"] = report.ManaSpent.GetValueOrDefault("Spell") + spellPaid;
                if (stancePaid > 0 && order.Target is not null && order.Stance != Stance.Attune) // an Attune's price is counted when it's paid
                {
                    report.ManaSpent[order.Stance.ToString()] = report.ManaSpent.GetValueOrDefault(order.Stance.ToString()) + stancePaid;
                }

                foreach (var (hex, before) in owners)
                {
                    var after = match.OwnerOf(hex);
                    if (after is not null && after != before.Owner)
                    {
                        if (before.Owner is not null)
                        {
                            round.Conquests++;
                        }
                        else if (before.IsGuarded)
                        {
                            round.MonstersCleared++;
                        }
                        else
                        {
                            round.Attunes++;
                        }
                    }
                }
            }

            foreach (System.Text.RegularExpressions.Match banked in BankedLine.Matches(string.Join('\n', lines)))
            {
                report.LandPoints += int.Parse(banked.Groups[1].Value, CultureInfo.InvariantCulture);
                report.ManaPoints += int.Parse(banked.Groups[2].Value, CultureInfo.InvariantCulture);
                report.ConquestPoints += banked.Groups[3].Success ? int.Parse(banked.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
            }

            round.LastTileSaves += lines.Count(l => l.Contains("last tile", StringComparison.Ordinal));
            foreach (var line in lines)
            {
                if (SeatBankedRegex().Match(line) is { Success: true } b)
                {
                    var src = sources[int.Parse(b.Groups[1].Value, CultureInfo.InvariantCulture) - 1];
                    src[0] += int.Parse(b.Groups[2].Value, CultureInfo.InvariantCulture);
                    src[1] += int.Parse(b.Groups[3].Value, CultureInfo.InvariantCulture);
                    src[2] += b.Groups[4].Success ? int.Parse(b.Groups[4].Value, CultureInfo.InvariantCulture) : 0;
                }
            }
        }

        report.MostImpsOnATile.Add(match.Map.Tiles.Max(t => match.UnitsOn(t.Hex).Imps));
        report.ManaLeft.AddRange(match.Seats.Select(s => s.Mana));
        report.FinalScores.AddRange(match.Seats.Select(s => s.Score));
        report.Spreads.Add(match.Standings[0].Score - match.Standings[^1].Score);
        foreach (var winner in match.Winners)
        {
            report.WinsBySeat[winner.Index]++;
        }

        if (plans is not null)
        {
            foreach (var seat in match.Seats)
            {
                var stats = report.Plans.TryGetValue(plans[seat.Index], out var p) ? p : report.Plans[plans[seat.Index]] = new();
                var src = sources[seat.Index];
                stats.Seats++;
                stats.Wins += match.Winners.Contains(seat) ? 1 : 0;
                stats.Score += seat.Score;
                stats.Land += src[0];
                stats.Mana += src[1];
                stats.Conquest += src[2];
                stats.Trait += seat.TraitPoints;
                stats.Upgrades += match.UpgradesOf(seat) * match.Rules.PointsPerUpgrade;
                stats.ManaLeft += seat.Mana;
            }
        }

        foreach (var seat in match.Seats.Where(s => s.Trait is not null))
        {
            var stats = report.Traits.TryGetValue(seat.Trait!.Value, out var t) ? t : report.Traits[seat.Trait.Value] = new();
            stats.Seats++;
            stats.Wins += match.Winners.Contains(seat) ? 1 : 0;
            stats.GoalPoints += seat.TraitPoints;
            stats.MaxedOut += seat.TraitPoints == match.Rules.TraitGoalCap ? 1 : 0;
        }
    }

    /// <summary>True when the seat has a free tile next to its land but can't afford to attune it.</summary>
    private static bool PricedOut(Match match, int seat)
    {
        var me = match.Seats[seat];
        return me.Tiles.SelectMany(match.Map.NeighboursOf).Any(h => match.Map[h].IsUnveiled && !match.Map[h].IsGuarded && match.OwnerOf(h) is null)
               && match.Rules.AttunePrice(me.Tiles.Count) > me.Mana;
    }

    private static IReadOnlyList<string> Resolved(Report report) =>
        report.Succeeded ? report.Lines : throw new InvalidOperationException($"A bot gave orders that failed: {report.Lines[0]}");
}

/// <summary>What a simulation measured, with a Markdown summary.</summary>
public sealed class SimulationReport(int players, int games, int seed, int rounds)
{
    public sealed class RoundStats
    {
        public int FreeTiles { get; internal set; }
        public int Attunes { get; internal set; }
        public int PricedOut { get; internal set; }

        /// <summary>Attune prices rolled and not paid.</summary>
        public int AttuneDeclines { get; internal set; }
        public int Conquests { get; internal set; }
        public int MonstersCleared { get; internal set; }
        public int LastTileSaves { get; internal set; }
        /// <summary>Actions taken, by stance (Channel, and Pass for a stance taken but aimed at nothing).</summary>
        public Dictionary<string, int> Stances { get; } = new() { ["Attack"] = 0, ["Attune"] = 0, ["Summon"] = 0, ["Build"] = 0, ["Cast"] = 0, ["Channel"] = 0, ["Pass"] = 0 };

        /// <summary>Picks, by stance.</summary>
        public Dictionary<string, int> Picks { get; } = Enum.GetValues<Stance>().ToDictionary(s => s.ToString(), _ => 0);
    }

    public sealed class TraitStats
    {
        public int Seats { get; internal set; }
        public int Wins { get; internal set; }
        public int GoalPoints { get; internal set; }
        public int MaxedOut { get; internal set; }
    }

    public Dictionary<Trait, TraitStats> Traits { get; } = [];


    public sealed class PlanStats
    {
        public int Seats { get; internal set; }
        public int Wins { get; internal set; }
        public int Score { get; internal set; }
        public int Land { get; internal set; }
        public int Mana { get; internal set; }
        public int Conquest { get; internal set; }
        public int Trait { get; internal set; }
        public int Upgrades { get; internal set; }
        public int ManaLeft { get; internal set; }
    }

    public Dictionary<Plan, PlanStats> Plans { get; } = [];

    public int Players => players;
    public int Games => games;
    public int LandPoints { get; internal set; }
    public List<int> ManaLeft { get; } = [];
    public Dictionary<string, int> ManaSpent { get; } = [];
    public Dictionary<string, int> Casts { get; } = [];
    public List<int> ImpsSummoned { get; } = [];
    public List<int> GolemsSummoned { get; } = [];
    public int ManaPoints { get; internal set; }
    public int ConquestPoints { get; internal set; }
    public List<int> ManaAfterOpening { get; } = [];
    public IReadOnlyList<RoundStats> Rounds { get; } = [.. Enumerable.Range(0, rounds).Select(_ => new RoundStats())];
    public List<int> MostImpsOnATile { get; } = [];
    public List<int> FinalScores { get; } = [];
    public List<int> Spreads { get; } = [];
    public int[] WinsBySeat { get; } = new int[players];

    public string ToMarkdown()
    {
        var c = CultureInfo.InvariantCulture;
        string Avg(IEnumerable<int> xs) => xs.Any() ? xs.Average().ToString("0.0", c) : "-";
        string PerGame(int n) => ((double)n / games).ToString("0.0", c);
        var md = new StringBuilder();
        md.AppendLine(c, $"# Simulation: {games} games, {players} bots, seed {seed}");
        md.AppendLine();
        md.AppendLine("## The opening");
        md.AppendLine(c, $"- Mana after the opening (with round 1's income): average {Avg(ManaAfterOpening)}, lowest {ManaAfterOpening.DefaultIfEmpty().Min()}, highest {ManaAfterOpening.DefaultIfEmpty().Max()}.");
        md.AppendLine();
        md.AppendLine("## The rounds (per game)");
        md.AppendLine();
        md.AppendLine("Actions taken (every seat may take each stance picked; Pass: a stance taken but aimed at nothing):");
        md.AppendLine();
        md.AppendLine("| Round | Free tiles | Attack | Attune | Summon | Build | Cast | Channel | Pass | Attuned | Priced out | Conquests | Monsters cleared | Last tile saved | Attune declined |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        for (var r = 0; r < Rounds.Count; r++)
        {
            var s = Rounds[r];
            md.AppendLine(c, $"| {r + 1} | {PerGame(s.FreeTiles)} | {PerGame(s.Stances["Attack"])} | {PerGame(s.Stances["Attune"])} | {PerGame(s.Stances["Summon"])} | {PerGame(s.Stances["Build"])} | {PerGame(s.Stances["Cast"])} | {PerGame(s.Stances["Channel"])} | {PerGame(s.Stances["Pass"])} | {PerGame(s.Attunes)} | {PerGame(s.PricedOut)} | {PerGame(s.Conquests)} | {PerGame(s.MonstersCleared)} | {PerGame(s.LastTileSaves)} | {PerGame(s.AttuneDeclines)} |");
        }

        md.AppendLine();
        md.AppendLine("Free tiles: free, unguarded, unveiled tiles at the start of the round. Priced out: seats with a free tile next to them that couldn't afford to attune it, at the start of the round.");
        md.AppendLine();
        md.AppendLine("Picks, per game, over the match (each seat picks one stance a round):");
        md.AppendLine();
        var stances = Enum.GetValues<Stance>().Select(st => st.ToString()).ToList();
        md.AppendLine($"| {string.Join(" | ", stances)} |");
        md.AppendLine($"|{string.Concat(stances.Select(_ => "---|"))}");
        md.AppendLine($"| {string.Join(" | ", stances.Select(st => PerGame(Rounds.Sum(r => r.Picks[st]))))} |");
        md.AppendLine();
        md.AppendLine("## The end");
        md.AppendLine(c, $"- Most Imps on one tile at the end: average {Avg(MostImpsOnATile)}, highest {MostImpsOnATile.DefaultIfEmpty().Max()}.");
        md.AppendLine(c, $"- Final scores: average {Avg(FinalScores)}; first minus last: average {Avg(Spreads)}.");
        string PerSeat(int points) => FinalScores.Count == 0 ? "-" : ((double)points / FinalScores.Count).ToString("0.0", c);
        md.AppendLine(c, $"- Points per seat by source: land {PerSeat(LandPoints)}, mana {PerSeat(ManaPoints)}, conquest {PerSeat(ConquestPoints)}.");
        md.AppendLine(c, $"- Wins by seat (shared wins count for each): {string.Join(", ", WinsBySeat.Select((w, i) => $"seat {i + 1}: {w}"))}.");
        md.AppendLine();
        md.AppendLine("## Mana");
        md.AppendLine(c, $"- Left unspent at the end: average {Avg(ManaLeft)} a seat, highest {ManaLeft.DefaultIfEmpty().Max()}.");
        string PerSeatSpent(string key) => FinalScores.Count == 0 ? "0" : ((double)ManaSpent.GetValueOrDefault(key) / FinalScores.Count).ToString("0.0", c);
        md.AppendLine(c, $"- Spent per seat over a match: Attune {PerSeatSpent("Attune")}, Summon {PerSeatSpent("Summon")}, Build {PerSeatSpent("Build")}, spells {PerSeatSpent("Spell")}.");
        md.AppendLine(c, $"- Spells cast per game: {(Casts.Count == 0 ? "none" : string.Join(", ", Casts.OrderBy(k => k.Key).Select(k => $"{k.Key} {PerGame(k.Value)}")))}.");
        md.AppendLine(c, $"- Summoned per game: {PerGame(ImpsSummoned.Sum())} Imps, {PerGame(GolemsSummoned.Sum())} Golems.");
        if (Plans.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("## Plans");
            md.AppendLine();
            md.AppendLine(c, $"A seat wins {(100.0 / players).ToString("0", c)}% of the time on average. Points per seat, by source.");
            md.AppendLine();
            md.AppendLine("| Plan | Seats | Win rate | Score | Land | Mana | Conquest | Trait | Upgrades | Mana left |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var (plan, p) in Plans.OrderBy(k => k.Key))
            {
                string Per(int n) => ((double)n / p.Seats).ToString("0.0", c);
                md.AppendLine(c, $"| {plan} | {p.Seats} | {(100.0 * p.Wins / p.Seats).ToString("0", c)}% | {Per(p.Score)} | {Per(p.Land)} | {Per(p.Mana)} | {Per(p.Conquest)} | {Per(p.Trait)} | {Per(p.Upgrades)} | {Per(p.ManaLeft)} |");
            }
        }

        md.AppendLine();
        md.AppendLine("## Traits");
        md.AppendLine();
        md.AppendLine(c, $"A seat wins {(100.0 / players).ToString("0", c)}% of the time on average (a shared win counts for each).");
        md.AppendLine();
        md.AppendLine("| Trait | Seats | Win rate | Goal points | At the cap |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (var (trait, t) in Traits.OrderBy(k => k.Key))
        {
            string Share(int n) => (100.0 * n / t.Seats).ToString("0", c) + "%";
            md.AppendLine(c, $"| {trait} | {t.Seats} | {Share(t.Wins)} | {((double)t.GoalPoints / t.Seats).ToString("0.0", c)} | {Share(t.MaxedOut)} |");
        }


        return md.ToString();
    }
}
