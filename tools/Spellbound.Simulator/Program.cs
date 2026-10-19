using Spellbound.Domain;

// Plays many seeded bot-against-bot matches under the default rules and prints a Markdown report on the paper
// prototype's "watch for" list (docs/skirmish-paper-prototype.md). The same arguments always give the same report.
//
//   dotnet run --project tools/Spellbound.Simulator [-- --players 3] [--games 1000] [--seed 1]
var players = 3;
var games = 1000;
var seed = 1;
for (var i = 0; i + 1 < args.Length; i += 2)
{
    var value = int.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
    switch (args[i])
    {
        case "--players": players = value; break;
        case "--games": games = value; break;
        case "--seed": seed = value; break;
        default: Console.Error.WriteLine($"Unknown option {args[i]}"); return 1;
    }
}

Console.Write(Simulation.Run(players, games, seed, GameRules.Default).ToMarkdown());
return 0;
