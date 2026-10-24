using System.Globalization;
using Spellbound.Domain;
using Spellbound.Table;

// A match on disk that AI players (Claude Code agents, each with a personality) play from the command line, one seat
// each, against the scripted bots in the other seats. See tools/Spellbound.Table/README.md.
//
//   dotnet run --project tools/Spellbound.Table -- new <file> --players 3 --ai 1,2 --seed 7
//   dotnet run --project tools/Spellbound.Table -- rules
//   dotnet run --project tools/Spellbound.Table -- view  <file> --seat 1
//   dotnet run --project tools/Spellbound.Table -- check <file> --seat 1 '<json>'
//   dotnet run --project tools/Spellbound.Table -- act   <file> --seat 1 '<json>' [--wait 1200]
if (args.Length == 0)
{
    Console.Error.WriteLine("Commands: new, rules, view, check, act.");
    return 1;
}

var options = Options(args);
var file = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? Path.GetFullPath(args[1]) : "";
switch (args[0])
{
    case "rules":
        Console.Write(View.Rules(GameRules.Default));
        return 0;

    case "new":
    {
        var players = Number("players", 3);
        var seed = Number("seed", 1);
        var ai = (options.GetValueOrDefault("ai") ?? "1").Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture) - 1).ToArray();
        var random = new SeededRandom(seed);
        var match = Match.Start(players, GameRules.Default, random);
        var botRandom = new SeededRandom(unchecked(seed * 1000 - 1));
        foreach (var bot in match.Seats.Where(s => !ai.Contains(s.Index)))
        {
            if (Bot.ChooseTrait(match, bot.Index, botRandom) is { } trait)
            {
                match.ChooseTrait(bot.Index, trait);
            }
        }

        TableFile.Create(file, new TableState(seed, 0, ai, match.Save(), [], []));
        Console.WriteLine($"New {players}-player match in {file}; AI seats: {string.Join(", ", ai.Select(s => s + 1))}; seed {seed}.");
        return 0;
    }

    case "view":
        Console.Write(View.Of(TableFile.Read(file), Seat()));
        return 0;

    case "check":
    {
        var (_, lines) = Validate(TableFile.Read(file), Seat(), Json());
        Console.WriteLine(string.Join('\n', lines));
        return 0;
    }

    case "act":
        return Act(Seat(), Json());

    default:
        Console.Error.WriteLine($"Unknown command {args[0]}.");
        return 1;
}

int Act(int seat, string json)
{
    var (stepBefore, outcome) = TableFile.Update(file, table =>
    {
        var (action, lines) = Validate(table, seat, json);
        if (action is null)
        {
            Log(table, seat, json, false, lines);
            return (table, (table.Step, (string[]?)lines));
        }

        if (action is Spellbound.Table.Action.PickTrait pick)
        {
            var match = table.Load();
            var report = match.ChooseTrait(seat, pick.Trait);
            Log(table, seat, json, report.Succeeded, report.Lines);
            return (report.Succeeded ? table with { Match = match.Save() } : table, (table.Step, [.. report.Lines]));
        }

        Log(table, seat, json, true, lines);
        var pending = new Dictionary<int, string>(table.Pending) { [seat] = json };
        var next = table with { Pending = pending };
        var loaded = table.Load();
        var ready = loaded.Phase == MatchPhase.Opening ? pending.ContainsKey(loaded.Picker!.Value) : table.AiSeats.All(pending.ContainsKey);
        return ready ? (Resolve(next), (table.Step, null)) : (next, (table.Step, null));
    });

    if (outcome is { } done)
    {
        Console.WriteLine(string.Join('\n', done));
        return 0;
    }

    // Committed: wait for the step to resolve (the other AI seats may still be thinking).
    var waitUntil = DateTime.UtcNow.AddSeconds(Number("wait", 1200));
    var table = TableFile.Read(file);
    while (table.Step == stepBefore && DateTime.UtcNow < waitUntil)
    {
        Thread.Sleep(500);
        table = TableFile.Read(file);
    }

    if (table.Step == stepBefore)
    {
        Console.WriteLine("Committed. " + View.Status(table, table.Load(), seat) + " Use `view` later to see when it resolves.");
        return 0;
    }

    Console.WriteLine("Committed. The step resolved:");
    foreach (var line in table.LastReportFor(table.Load(), seat))
    {
        Console.WriteLine("  " + line);
    }

    Console.WriteLine(View.Status(table, table.Load(), seat));
    return 0;
}

// Whether an action is allowed now, with the check's lines (cost and result, or the problem).
static (Spellbound.Table.Action? Action, string[] Lines) Validate(TableState table, int seat, string json)
{
    if (!table.AiSeats.Contains(seat))
    {
        return (null, [$"Seat {seat + 1} isn't played by AI at this table."]);
    }

    var (action, problem) = Spellbound.Table.Action.Parse(json, seat);
    if (action is null)
    {
        return (null, [problem!]);
    }

    var match = table.Load();
    if (match.Phase == MatchPhase.Ended)
    {
        return (null, ["The match is over."]);
    }

    if (action is Spellbound.Table.Action.PickTrait)
    {
        return (action, ["Choose this trait."]);
    }

    if (table.Pending.ContainsKey(seat))
    {
        return (null, ["You have already acted this step; wait for the others."]);
    }

    switch (action)
    {
        case Spellbound.Table.Action.PickTile when match.Seats[seat] is { Trait: null, Offer.Count: > 0 }:
            return (null, ["Choose your trait first."]);
        case Spellbound.Table.Action.PickTile pick:
            var pickCheck = match.CheckPick(pick.Pick);
            return (pickCheck.Succeeded ? action : null, [.. pickCheck.Lines]);
        case Spellbound.Table.Action.GiveOrders orders:
            var check = match.Check(orders.Orders);
            return (check.Succeeded ? action : null, [.. check.Lines]);
        default:
            return (null, ["That isn't something to do now."]);
    }
}

// Every AI seat has acted: the scripted bots act too, and the step resolves with its own randomness.
TableState Resolve(TableState table)
{
    var match = table.Load();
    var random = table.StepRandom();
    Report report;
    if (match.Phase == MatchPhase.Opening)
    {
        var picker = match.Picker!.Value;
        report = match.Draft(table.Pending.TryGetValue(picker, out var json)
            ? ((Spellbound.Table.Action.PickTile)Spellbound.Table.Action.Parse(json, picker).Action!).Pick
            : Bot.ChoosePick(match, picker, random)!, random);
        // The scripted bots after the AI seat pick at once, until it is an AI seat's turn again.
        while (report.Succeeded && match.Phase == MatchPhase.Opening && !table.AiSeats.Contains(match.Picker!.Value))
        {
            var more = match.Draft(Bot.ChoosePick(match, match.Picker!.Value, random)!, random);
            report = new Report(more.Succeeded, [.. report.Lines, .. more.Lines], [.. report.Lines.Select((_, i) => report.TagOf(i)), .. more.Lines.Select((_, i) => more.TagOf(i))]);
        }
    }
    else
    {
        report = match.Resolve(
            [.. match.Seats.Select(s => table.Pending.TryGetValue(s.Index, out var json)
                ? ((Spellbound.Table.Action.GiveOrders)Spellbound.Table.Action.Parse(json, s.Index).Action!).Orders
                : Bot.ChooseOrders(match, s.Index, random))],
            random);
    }

    TableFile.Log(file, new { step = table.Step, resolved = report.Succeeded, lines = report.Lines });
    return report.Succeeded
        ? table with { Step = table.Step + 1, Match = match.Save(), Pending = [], LastReport = [.. report.Lines], LastReportTags = [.. report.Tags ?? []] }
        : table with { Pending = [], LastReport = [.. report.Lines], LastReportTags = null };
}

void Log(TableState table, int seat, string json, bool ok, IEnumerable<string> lines) =>
    TableFile.Log(file, new { step = table.Step, seat = seat + 1, action = json, ok, lines });

int Seat() => Number("seat", 1) - 1;

int Number(string name, int otherwise) =>
    options.TryGetValue(name, out var v) && v is not null ? int.Parse(v, CultureInfo.InvariantCulture) : otherwise;

// The action: the last argument that isn't an option or its value.
string Json() => args.Skip(2).Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal)
                                               && !(i > 0 && args.Skip(2).ElementAt(i - 1).StartsWith("--", StringComparison.Ordinal))).LastOrDefault() ?? "";

static Dictionary<string, string?> Options(string[] args)
{
    var found = new Dictionary<string, string?>();
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith("--", StringComparison.Ordinal))
        {
            found[args[i][2..]] = i + 1 < args.Length ? args[i + 1] : null;
        }
    }

    return found;
}
