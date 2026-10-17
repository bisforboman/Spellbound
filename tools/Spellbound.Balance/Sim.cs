using System.Globalization;
using System.Text;
using Spellbound.Domain;

namespace Spellbound.Balance;

/// <summary>A player in a scenario: a bot, and the hours of day (UTC) it checks in.</summary>
public sealed record Seat(Bot Bot, int[] CheckInHours)
{
    public string Cadence => CheckInHours.Length switch
    {
        0 => "never",
        1 => "once a day",
        _ => $"{CheckInHours.Length}× a day",
    };
}

public sealed record AttackLog(int Day, string Attacker, string Target, int Acres);

public sealed record DayRow(int Day, string Player, int Land, int Networth, int Gold, int Units, int Upkeep, int Income);

/// <summary>Runs one scenario: every seat checks in at its hours, in a seeded order, for a number of days.</summary>
public sealed class Sim
{
    private readonly List<(Seat Seat, Mage Mage)> players = [];
    private readonly List<AttackLog> attacks = [];
    private readonly List<DayRow> days = [];

    public Sim(string scenario, GameRules rules, int seed, DateTimeOffset start, IEnumerable<Seat> seats)
    {
        Scenario = scenario;
        Rules = rules;
        Random = new SeededRandom(seed);
        Start = start;
        Now = start;
        foreach (var seat in seats)
        {
            players.Add((seat, Mage.Create($"bots/{seat.Bot.Name}", seat.Bot.Name, seat.Bot.School, start, rules)));
        }
    }

    public string Scenario { get; }

    public GameRules Rules { get; }

    public SeededRandom Random { get; }

    public DateTimeOffset Start { get; }

    public DateTimeOffset Now { get; private set; }

    public int Day => (int)(Now - Start).TotalDays + 1;

    public IReadOnlyList<AttackLog> Attacks => attacks;

    public IReadOnlyList<DayRow> Days => days;

    public IReadOnlyList<(Seat Seat, Mage Mage)> Players => players;

    public void RecordAttack(Bot attacker, Mage target, int acres) => attacks.Add(new AttackLog(Day, attacker.Name, target.Name, acres));

    public Sim Run(int dayCount)
    {
        for (var day = 0; day < dayCount; day++)
        {
            var midnight = Start.AddDays(day);
            var checkIns = players
                .SelectMany(p => p.Seat.CheckInHours.Select(h => (At: midnight.AddHours(h), Player: p)))
                .OrderBy(c => c.At)
                .ThenBy(_ => Random.NextDouble()) // who goes first at the same hour is luck
                .ToList();
            foreach (var (at, (seat, mage)) in checkIns)
            {
                Now = at;
                mage.AccrueTurns(Now, Rules);
                seat.Bot.Play(mage, [.. players.Where(p => p.Mage != mage).Select(p => p.Mage)], this);
            }

            Now = midnight.AddDays(1);
            foreach (var (seat, mage) in players)
            {
                mage.AccrueTurns(Now, Rules);
                days.Add(new DayRow(day + 1, seat.Bot.Name, mage.Land, mage.Networth(Rules), mage.Gold, mage.TotalUnits,
                    mage.UpkeepPerTurn, (mage.Land * Rules.GoldPerAcrePerTurn) + (mage.Towns * Rules.GoldPerTownPerTurn)));
            }
        }

        return this;
    }

    /// <summary>Turns the clock gave this mage but it never got to use, because it sat at the cap.</summary>
    public int TurnsLost(Mage mage)
    {
        var generated = Rules.StartingTurns + (int)((Now - Start).Ticks / Rules.TurnInterval.Ticks);
        return generated - mage.TurnsSpent - mage.Turns;
    }

    public int TurnsGenerated => Rules.StartingTurns + (int)((Now - Start).Ticks / Rules.TurnInterval.Ticks);

    public string Report(int[] snapshotDays)
    {
        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture, $"## {Scenario}");
        md.AppendLine();
        md.AppendLine("| Player | Strategy | Checks in | Land | Networth | Units | Upkeep / income | Turns lost at the cap |");
        md.AppendLine("|---|---|---|---:|---:|---:|---:|---:|");
        foreach (var (seat, mage) in players.OrderByDescending(p => p.Mage.Networth(Rules)))
        {
            var last = days.Last(d => d.Player == seat.Bot.Name);
            var peakUpkeep = days.Where(d => d.Player == seat.Bot.Name && d.Income > 0).Max(d => (double)d.Upkeep / d.Income);
            var lost = TurnsLost(mage);
            md.AppendLine(CultureInfo.InvariantCulture,
                $"| {seat.Bot.Name} | {seat.Bot.Strategy} | {seat.Cadence} | {N(mage.Land)} | {N(mage.Networth(Rules))} | {N(mage.TotalUnits)} | " +
                $"{last.Upkeep * 100 / Math.Max(1, last.Income)}% (peak {peakUpkeep * 100:F0}%) | {N(lost)} ({lost * 100 / TurnsGenerated}%) |");
        }

        md.AppendLine();
        md.AppendLine("Land by day:");
        md.AppendLine();
        md.AppendLine("| Player | " + string.Join(" | ", snapshotDays.Select(d => $"Day {d}")) + " |");
        md.AppendLine("|---|" + string.Concat(snapshotDays.Select(_ => "---:|")));
        foreach (var (seat, _) in players)
        {
            md.AppendLine($"| {seat.Bot.Name} | " + string.Join(" | ", snapshotDays.Select(d =>
                N(days.FirstOrDefault(r => r.Day == d && r.Player == seat.Bot.Name)?.Land ?? 0))) + " |");
        }

        if (attacks.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("Attacks (all won by the attacker, who only attacks when sure to win):");
            md.AppendLine();
            md.AppendLine("| Attacker | Target | Attacks | Acres taken | Most attacks in one day | First day | Last day |");
            md.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            foreach (var pair in attacks.GroupBy(a => (a.Attacker, a.Target)).OrderByDescending(g => g.Count()))
            {
                md.AppendLine(CultureInfo.InvariantCulture,
                    $"| {pair.Key.Attacker} | {pair.Key.Target} | {pair.Count()} | {N(pair.Sum(a => a.Acres))} | " +
                    $"{pair.GroupBy(a => a.Day).Max(g => g.Count())} | {pair.Min(a => a.Day)} | {pair.Max(a => a.Day)} |");
            }
        }

        md.AppendLine();
        return md.ToString();
    }

    private static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
