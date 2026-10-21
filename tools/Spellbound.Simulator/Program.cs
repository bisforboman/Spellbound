using Spellbound.Domain;

// Plays many seeded bot-against-bot matches under the default rules and prints a Markdown report on the paper
// prototype's "watch for" list (docs/skirmish-paper-prototype.md). The same arguments always give the same report.
//
//   dotnet run --project tools/Spellbound.Simulator [-- --players 3] [--games 1000] [--seed 1] [--plans]
//
// --plans: bots with different plans (Warlord, Builder, Spellcaster, Expander, Hoarder, and the everyday Balanced)
// play each other, and the report adds each plan's win rate and where its points come from.
var players = 3;
var games = 1000;
var seed = 1;
var plans = args.Contains("--plans");
foreach (var (option, text) in args.Where(a => a != "--plans").Chunk(2).Select(p => (p[0], p.Length > 1 ? p[1] : "")))
{
    var value = int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    switch (option)
    {
        case "--players": players = value; break;
        case "--games": games = value; break;
        case "--seed": seed = value; break;
        default: Console.Error.WriteLine($"Unknown option {option}"); return 1;
    }
}

Console.Write((plans ? Simulation.RunPlans(players, games, seed, GameRules.Default) : Simulation.Run(players, games, seed, GameRules.Default)).ToMarkdown());
return 0;
