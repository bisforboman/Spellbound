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

    private static void Play(Match match, IRandomSource random, SimulationReport report)
    {
        while (match.Phase == MatchPhase.Opening)
        {
            var bids = match.Seats.Select(s => Bot.ChooseBid(match, s.Index, random)).ToList();
            report.Bids.AddRange(bids.Where(b => b.Tile is not null).Select(b => b.Amount));
            report.Passes += bids.Count(b => b.Tile is null);
            var lines = Resolved(match.Auction(bids, random));
            report.OpeningTies += lines.Count(l => l.StartsWith("A tie", StringComparison.Ordinal));
        }

        report.ManaAfterOpening.AddRange(match.Seats.Select(s => s.Mana));
        while (match.Phase == MatchPhase.Rounds)
        {
            var round = report.Rounds[match.CurrentRound - 1];
            round.FreeTiles += match.Map.Tiles.Count(t => t.IsUnveiled && !t.IsGuarded && match.OwnerOf(t.Hex) is null);
            var orders = match.Seats.Select(s => Bot.ChooseOrders(match, s.Index, random)).ToList();
            foreach (var order in orders)
            {
                round.Stances[order.Target is null ? "Hold" : order.Stance.ToString()]++;
                if (order.Cast is { } cast)
                {
                    round.Spells++;
                    report.Casts[cast.Spell.ToString()] = report.Casts.GetValueOrDefault(cast.Spell.ToString()) + 1;
                }

                if (order.Stance == Stance.Summon && order.Target is not null)
                {
                    (order.Kind == UnitKind.Golem ? report.GolemsSummoned : report.ImpsSummoned).Add(order.Count);
                }
                if (order.Stance != Stance.Attune && PricedOut(match, order.Seat))
                {
                    round.PricedOut++;
                }
            }

            var costs = orders.Select(o => (o.Seat, o.Stance, o.Target, Cost: match.StanceCostOf(o))).ToList();
            foreach (var order in orders)
            {
                report.ManaSpent["Spell"] = report.ManaSpent.GetValueOrDefault("Spell") + match.SpellCostOf(order);
            }

            var owners = match.Map.Tiles.ToDictionary(t => t.Hex, t => (Owner: match.OwnerOf(t.Hex), t.IsGuarded));
            var lines = Resolved(match.Resolve(orders, random));
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

            round.AttuneClashes += lines.Count(l => l.Contains(" attune ", StringComparison.Ordinal) && l.EndsWith("nobody gets it.", StringComparison.Ordinal));
            foreach (var (seatIndex, stance, target, cost) in costs.Where(c => c.Cost > 0))
            {
                // An Attune that clashed was free.
                if (stance != Stance.Attune || match.OwnerOf(target!.Value) == seatIndex)
                {
                    report.ManaSpent[stance.ToString()] = report.ManaSpent.GetValueOrDefault(stance.ToString()) + cost;
                }
            }

            foreach (System.Text.RegularExpressions.Match banked in BankedLine.Matches(string.Join('\n', lines)))
            {
                report.LandPoints += int.Parse(banked.Groups[1].Value, CultureInfo.InvariantCulture);
                report.ManaPoints += int.Parse(banked.Groups[2].Value, CultureInfo.InvariantCulture);
                report.ConquestPoints += banked.Groups[3].Success ? int.Parse(banked.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
            }

            round.LastTileSaves += lines.Count(l => l.Contains("last tile", StringComparison.Ordinal));
        }

        report.MostImpsOnATile.Add(match.Map.Tiles.Max(t => match.UnitsOn(t.Hex).Imps));
        report.ManaLeft.AddRange(match.Seats.Select(s => s.Mana));
        report.FinalScores.AddRange(match.Seats.Select(s => s.Score));
        report.Spreads.Add(match.Standings[0].Score - match.Standings[^1].Score);
        foreach (var winner in match.Winners)
        {
            report.WinsBySeat[winner.Index]++;
        }
    }

    /// <summary>True when the seat has a free tile next to its land but can't afford to attune it.</summary>
    private static bool PricedOut(Match match, int seat)
    {
        var free = match.Seats[seat].Tiles.SelectMany(match.Map.NeighboursOf)
            .FirstOrDefault(h => match.Map[h].IsUnveiled && !match.Map[h].IsGuarded && match.OwnerOf(h) is null, new Hex(int.MinValue, 0));
        return match.Map.Contains(free) && match.Check(Orders.Attune(seat, free)).Lines is [var line]
               && line.StartsWith("Attuning costs", StringComparison.Ordinal);
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
        public int AttuneClashes { get; internal set; }
        public int Conquests { get; internal set; }
        public int MonstersCleared { get; internal set; }
        public int LastTileSaves { get; internal set; }
        public Dictionary<string, int> Stances { get; } = new() { ["Attack"] = 0, ["Attune"] = 0, ["Summon"] = 0, ["Hold"] = 0 };
        public int Spells { get; internal set; }
    }

    public int Players => players;
    public int Games => games;
    public List<int> Bids { get; } = [];
    public int Passes { get; internal set; }
    public int LandPoints { get; internal set; }
    public List<int> ManaLeft { get; } = [];
    public Dictionary<string, int> ManaSpent { get; } = [];
    public Dictionary<string, int> Casts { get; } = [];
    public List<int> ImpsSummoned { get; } = [];
    public List<int> GolemsSummoned { get; } = [];
    public int ManaPoints { get; internal set; }
    public int ConquestPoints { get; internal set; }
    public int OpeningTies { get; internal set; }
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
        md.AppendLine(c, $"- Bids: {Bids.Count}, average {Avg(Bids)}, highest {(Bids.Count > 0 ? Bids.Max() : 0)}; passes: {Passes}; ties: {OpeningTies}.");
        md.AppendLine(c, $"- Bids by amount: {string.Join(", ", Bids.GroupBy(b => b).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}"))}.");
        md.AppendLine(c, $"- Mana left after the opening: average {Avg(ManaAfterOpening)}, lowest {ManaAfterOpening.DefaultIfEmpty().Min()}, highest {ManaAfterOpening.DefaultIfEmpty().Max()}.");
        md.AppendLine();
        md.AppendLine("## The rounds (per game)");
        md.AppendLine();
        md.AppendLine("| Round | Free tiles | Attack | Attune | Summon | Hold | Spells | Attuned | Clashes | Priced out | Conquests | Monsters cleared | Last tile saved |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        for (var r = 0; r < Rounds.Count; r++)
        {
            var s = Rounds[r];
            md.AppendLine(c, $"| {r + 1} | {PerGame(s.FreeTiles)} | {PerGame(s.Stances["Attack"])} | {PerGame(s.Stances["Attune"])} | {PerGame(s.Stances["Summon"])} | {PerGame(s.Stances["Hold"])} | {PerGame(s.Spells)} | {PerGame(s.Attunes)} | {PerGame(s.AttuneClashes)} | {PerGame(s.PricedOut)} | {PerGame(s.Conquests)} | {PerGame(s.MonstersCleared)} | {PerGame(s.LastTileSaves)} |");
        }

        md.AppendLine();
        md.AppendLine("Free tiles: free, unguarded, unveiled tiles at the start of the round. Clashes: tiles two or more seats attuned at once, so nobody got them. Priced out: seats with a free tile next to them that couldn't afford to attune it.");
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
        md.AppendLine(c, $"- Spent per seat over a match: Attune {PerSeatSpent("Attune")}, Summon {PerSeatSpent("Summon")}, spells {PerSeatSpent("Spell")}.");
        md.AppendLine(c, $"- Spells cast per game: {(Casts.Count == 0 ? "none" : string.Join(", ", Casts.OrderBy(k => k.Key).Select(k => $"{k.Key} {PerGame(k.Value)}")))}.");
        md.AppendLine(c, $"- Summoned per game: {PerGame(ImpsSummoned.Sum())} Imps, {PerGame(GolemsSummoned.Sum())} Golems.");
        return md.ToString();
    }
}
