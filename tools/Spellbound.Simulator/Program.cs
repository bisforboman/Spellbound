using Spellbound.Domain;

// Plays many seeded bot-against-bot matches under the default rules and prints a Markdown report on the paper
// prototype's "watch for" list (docs/skirmish-paper-prototype.md). The same arguments always give the same report.
//
//   dotnet run --project tools/Spellbound.Simulator [-- --players 3] [--games 1000] [--seed 1] [--plans]
//       [--set AttunePricePerTile=3 ...]
//
// --set: tries a different whole-number rule from GameRules (any number of times), to measure a change before making it.
// --plans: bots with different plans (Warlord, Builder, Spellcaster, Expander, Economist, and the everyday Balanced)
// play each other, and the report adds each plan's win rate and where its points come from.
var players = 3;
var games = 1000;
var seed = 1;
var plans = args.Contains("--plans");
var rules = GameRules.Default;
foreach (var (option, text) in args.Where(a => a != "--plans").Chunk(2).Select(p => (p[0], p.Length > 1 ? p[1] : "")))
{
    if (option == "--set")
    {
        var (name, number) = (text.Split('=')[0], int.Parse(text.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture));
        var property = typeof(GameRules).GetProperty(name);
        if (property?.PropertyType != typeof(int))
        {
            Console.Error.WriteLine($"No whole-number rule {name}");
            return 1;
        }

        rules = rules with { };
        property.SetValue(rules, number);
        continue;
    }

    var value = int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    switch (option)
    {
        case "--players": players = value; break;
        case "--games": games = value; break;
        case "--seed": seed = value; break;
        default: Console.Error.WriteLine($"Unknown option {option}"); return 1;
    }
}

Console.Write((plans ? Simulation.RunPlans(players, games, seed, rules) : Simulation.Run(players, games, seed, rules)).ToMarkdown());
return 0;
