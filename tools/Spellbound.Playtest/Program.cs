using System.Diagnostics;
using System.Text;
using Microsoft.Playwright;
using Npgsql;
using Spellbound.Playtest;

// Plays Spellbound end to end in a real browser at phone size, the way a player would: starts the app on a fresh
// database with fast rules, signs in three players, visits every page and takes every kind of action, then checks the
// admin page's Problems. Every page is screenshotted and checked (no sideways scrolling, no error bar, no console
// errors or failed requests; tap targets under 44 px are listed). Writes report.md next to the screenshots and exits
// with 1 if anything failed. See .claude/skills/playtest/SKILL.md.
//
//   dotnet run --project tools/Spellbound.Playtest [-- --url http://127.0.0.1:5199] [--out <dir>] [--no-app] [--headed]
var options = Options.Parse(args);
Directory.CreateDirectory(options.Out);
foreach (var old in Directory.EnumerateFiles(options.Out))
{
    File.Delete(old);
}

Console.WriteLine($"Playtest: {options.Url}, output in {options.Out}");
Process? app = null;
try
{
    if (options.StartApp)
    {
        await RecreateDatabaseAsync(options);
        app = StartApp(options);
    }

    await WaitForHealthAsync(options.Url, app);

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
        Headless = !options.Headed,
        ExecutablePath = Options.ChromiumPath(),
    });

    var run = new Playtest(browser, options);
    await run.PlayAsync();

    var appErrors = app is null ? [] : AppLog.Errors(Path.Combine(options.Out, "app.log"));
    var report = run.Report(appErrors);
    await File.WriteAllTextAsync(Path.Combine(options.Out, "report.md"), report);
    Console.WriteLine(report);
    return run.Failed || appErrors.Count > 0 ? 1 : 0;
}
finally
{
    if (app is { HasExited: false })
    {
        app.Kill(entireProcessTree: true);
        await app.WaitForExitAsync();
    }
}

static async Task RecreateDatabaseAsync(Options options)
{
    await using var connection = new NpgsqlConnection(options.Postgres);
    await connection.OpenAsync();
    foreach (var sql in new[] { $"DROP DATABASE IF EXISTS {Options.Database} WITH (FORCE)", $"CREATE DATABASE {Options.Database}" })
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

static Process StartApp(Options options)
{
    var start = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = options.RepoRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    foreach (var arg in new[] { "run", "--project", "src/Spellbound.Web", "--no-launch-profile" })
    {
        start.ArgumentList.Add(arg);
    }

    var database = new NpgsqlConnectionStringBuilder(options.Postgres) { Database = Options.Database }.ConnectionString;
    var environment = new Dictionary<string, string>
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Development",
        ["ASPNETCORE_URLS"] = options.Url,
        ["ConnectionStrings__Spellbound"] = database,
        ["Admins__0"] = Playtest.AdminEmail,
        // Three players from one address would trip the per-address limits.
        ["RateLimits__AccountPosts"] = "1000",
        ["RateLimits__ConnectionRequestsPerMinute"] = "10000",
        ["RateLimits__RequestsPerMinute"] = "10000",
        // The draft plays back quickly here, so steps don't wait on it.
        ["Match__ReplayDelayMs"] = "300",
        ["Logging__LogLevel__Default"] = "Information",
    };
    foreach (var (key, value) in environment)
    {
        start.Environment[key] = value;
    }

    var log = new StreamWriter(Path.Combine(options.Out, "app.log")) { AutoFlush = true };
    var process = new Process { StartInfo = start };
    process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (log) { log.WriteLine(e.Data); } } };
    process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (log) { log.WriteLine(e.Data); } } };
    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    Console.WriteLine("Started the app (log: app.log).");
    return process;
}

static async Task WaitForHealthAsync(string url, Process? app)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    var until = DateTime.UtcNow.AddMinutes(3);
    while (DateTime.UtcNow < until)
    {
        if (app is { HasExited: true })
        {
            throw new InvalidOperationException($"The app stopped while starting (exit code {app.ExitCode}); see app.log.");
        }

        try
        {
            if ((await http.GetAsync(new Uri($"{url}/healthz"))).IsSuccessStatusCode)
            {
                return;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // not up yet
        }

        await Task.Delay(500);
    }

    throw new TimeoutException($"{url}/healthz didn't answer within 3 minutes; see app.log.");
}

namespace Spellbound.Playtest
{
    internal sealed record Options(string Url, string Out, string Postgres, bool StartApp, bool Headed, string RepoRoot)
    {
        public const string Database = "spellbound_playtest";

        public static Options Parse(string[] args)
        {
            var root = RepoRootFrom(Directory.GetCurrentDirectory());
            string? Value(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            return new Options(
                (Value("--url") ?? "http://127.0.0.1:5199").TrimEnd('/'),
                Path.GetFullPath(Value("--out") ?? Path.Combine(root, "tools", "Spellbound.Playtest", "out")),
                Environment.GetEnvironmentVariable("SPELLBOUND_TEST_POSTGRES") is { Length: > 0 } pg
                    ? pg
                    : "Host=localhost;Port=5432;Username=spellbound;Password=spellbound;Database=postgres",
                !args.Contains("--no-app"),
                args.Contains("--headed"),
                root);
        }

        /// <summary>
        /// The browser to use: PLAYTEST_CHROMIUM, else the cloud sandbox's preinstalled Chromium, else the one
        /// Playwright installed (on your own machine: pwsh tools/Spellbound.Playtest/bin/Debug/net10.0/playwright.ps1 install chromium).
        /// </summary>
        public static string? ChromiumPath() =>
            Environment.GetEnvironmentVariable("PLAYTEST_CHROMIUM") is { Length: > 0 } path ? path
            : File.Exists("/opt/pw-browsers/chromium") ? "/opt/pw-browsers/chromium"
            : null;

        private static string RepoRootFrom(string directory)
        {
            for (var dir = new DirectoryInfo(directory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Spellbound.slnx")))
                {
                    return dir.FullName;
                }
            }

            throw new InvalidOperationException("Run the playtest from inside the Spellbound repository.");
        }
    }

    internal static class AppLog
    {
        /// <summary>Error and critical entries in the app's console log ("fail:" and "crit:").</summary>
        public static List<string> Errors(string path)
        {
            if (!File.Exists(path))
            {
                return [];
            }

            var lines = File.ReadAllLines(path);
            return
            [
                .. lines.Select((line, i) => (line, i))
                    .Where(l => l.line.StartsWith("fail:", StringComparison.Ordinal) || l.line.StartsWith("crit:", StringComparison.Ordinal))
                    .Select(l => l.i + 1 < lines.Length ? $"{l.line.Trim()} {lines[l.i + 1].Trim()}" : l.line.Trim()),
            ];
        }
    }

    /// <summary>One player's browser: a phone-sized context with its own sign-in cookie.</summary>
    internal sealed record Player(string Name, string Email, IBrowserContext Context, IPage Page)
    {
        public List<string> Console { get; } = [];

        public List<string> FailedRequests { get; } = [];
    }

    internal sealed record StepResult(int Number, string Player, string Name, bool Passed, string Screenshot, List<string> Problems, List<string> Warnings);

    internal sealed class Playtest(IBrowser browser, Options options)
    {
        public const string AdminEmail = "ada@playtest.local";

        private readonly List<StepResult> steps = [];

        public bool Failed => steps.Any(s => !s.Passed);

        public async Task PlayAsync()
        {
            var ada = await PlayerAsync("Ada", AdminEmail);
            var merlin = await PlayerAsync("Merlin", "merlin@playtest.local");
            var morgana = await PlayerAsync("Morgana", "morgana@playtest.local");
            string? bigMap = null; // the 5-player match, for the looks

            foreach (var player in new[] { ada, merlin, morgana })
            {
                await StepAsync(player, "sign-in", async page =>
                {
                    await GoAsync(page, "/Account/Login");
                    await page.Locator(".top-bar a[href='privacy']").WaitForAsync(); // signed out, the top bar links the notice
                    await page.FillAsync("#email", player.Email);
                    await page.ClickAsync("form[action='Account/DevSignIn'] button[type=submit]");
                    await page.WaitForURLAsync($"{options.Url}/");
                    await WaitForCircuitAsync(page);
                    await Expect(page, "Your matches");
                    await Expect(page, "in a match yet");
                });
            }

            await StepAsync(merlin, "account", async page =>
            {
                await GoAsync(page, "/Account/Manage");
                var data = await page.Context.APIRequest.GetAsync($"{options.Url}/Account/Data");
                if (!data.Ok || !(await data.TextAsync()).Contains(merlin.Email, StringComparison.Ordinal))
                {
                    throw new PlaytestException($"Download my data answered {data.Status} without Merlin's account.");
                }
            });
            await StepAsync(merlin, "notifications", async page =>
            {
                // The sandbox has no push service, so turning them on can only end in a clear message, never a hang.
                await page.Context.GrantPermissionsAsync(["notifications"]);
                await GoAsync(page, "/Account/Manage");
                await page.ClickAsync("button[data-push='on']");
                await page.WaitForFunctionAsync(
                    "() => !/Checking/.test(document.querySelector('[data-push-status]').textContent)", null,
                    new PageWaitForFunctionOptions { Timeout = 15_000 });
                await page.ClickAsync("button:has-text('Send me a test notification')");
                await Expect(page, "A test notification is on its way");
            });
            await StepAsync(merlin, "new-match", async page =>
            {
                await GoAsync(page, "/");
                await page.ClickAsync("a:has-text('New match')");
                await Expect(page, "Play bots");
                await page.ClickAsync("button:has-text('Play bots')");
                await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/match/\\d+$"));
                await Expect(page, "Welcome to match");
                await Expect(page, "against Bot 2 and Bot 3");
            });
            await StepAsync(merlin, "trait", async page =>
            {
                await page.ClickAsync("button:has-text('Next: choose your trait')");
                await Expect(page, "Choose your trait");
                if (await page.Locator(".hex-map").CountAsync() > 0)
                {
                    throw new PlaytestException("The map shows before the trait is chosen.");
                }

                await page.Locator("input[name='trait']").Nth(1).CheckAsync();
                await page.ClickAsync("button:has-text('Choose '):not([disabled])");
                await Expect(page, "Opening draft 1 of 3");
                await Expect(page, "The map is revealed");
                await Expect(page, "Draft 1/3"); // the top line: colour, where the match is, mana
            });
            await StepAsync(merlin, "pick", async page =>
            {
                await Expect(page, "Your pick");
                await page.Locator(".hex-map .hex.marked").Nth(1).ClickAsync(); // a tile to pick, chosen on the map
                await Expect(page, "It costs nothing");
                if (await page.Locator(".match-top .mana-icon").CountAsync() < 1)
                {
                    throw new PlaytestException("The mana icon is missing from the top line.");
                }
                await page.ClickAsync("button:has-text('Pick '):not([disabled])");
                // The bots pick at once, until it is Merlin's turn again; the draft plays back a pick at a time.
                await page.Locator("#playback").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
                await Expect(page, "picks the");
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.Out, "draft-playback-screen.png"), FullPage = true });
                await Expect(page, "Opening draft 3 of 3");
                await page.Locator(".match-tabs a.unread:has-text('Report')").WaitForAsync(); // the report is a tab away, marked new
            });
            await StepAsync(merlin, "pick-to-the-rounds", async page =>
            {
                // Nobody scores in the opening, so the standings wait for the rounds.
                if (await page.Locator("#standings, .match-tabs button:has-text('Standings')").CountAsync() > 0)
                {
                    throw new PlaytestException("The standings show during the opening.");
                }

                // Pick the first tile on every turn until the rounds begin (between turns the draft plays back).
                for (var wait = 0; wait < 100 && !(await page.InnerTextAsync(".match-top")).Contains("Round 1/8", StringComparison.Ordinal); wait++)
                {
                    if (await page.Locator("input[name='pick']").CountAsync() > 0)
                    {
                        await page.Locator("label.choice:has(input[name='pick'])").First.ClickAsync();
                        await page.ClickAsync("button:has-text('Pick '):not([disabled])");
                    }

                    await page.WaitForTimeoutAsync(300);
                }

                await Expect(page, "Round 1 of 8");
                await page.ClickAsync(".match-tabs a:has-text('Report')");
                await Expect(page, "The rounds begin");
                // The fog of war: a rival's income is its mana, so nobody else reads it.
                await page.ClickAsync("button.report-toggle:has-text('Everything that happened')");
                await Expect(page, "Only what concerns you");
                if (await page.Locator("text=/Bot \\d collects/").CountAsync() > 0)
                {
                    throw new InvalidOperationException("A rival's income shows through the fog.");
                }

                await page.ClickAsync(".match-tabs a:has-text('Board')");
            });
            await StepAsync(merlin, "standings", async page =>
            {
                // A view of their own, opened by the bottom tab.
                if (await page.Locator("table.standings").CountAsync() > 0)
                {
                    throw new PlaytestException("The standings are open before anyone asked for them.");
                }

                await page.ClickAsync(".match-tabs a:has-text('Standings')");
                await page.Locator("table.standings").WaitForAsync();
                await Expect(page, "Rivals' traits are secret until the end");
                await page.WaitForFunctionAsync(
                    "() => { const r = document.querySelector('table.standings').getBoundingClientRect(); return r.top >= 0 && r.top < innerHeight; }",
                    null, new PageWaitForFunctionOptions { Timeout = 5_000 });
            });
            await StepAsync(merlin, "help", async page =>
            {
                // The Help tab: the trait's goal, the tile types and the rules, off the main screen.
                await page.ClickAsync(".match-tabs a:has-text('Help')");
                await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("#help$"));
                await Expect(page, "How to play");
                await Expect(page, "Your trait:");
                await Expect(page, "Every trait");
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.Out, "help-page.png"), FullPage = true });
                await page.ClickAsync(".match-tabs a:has-text('Board')");
                await Expect(page, "Round 1 of 8");
            });
            await StepAsync(merlin, "summon", async page =>
            {
                await page.ClickAsync("label:has-text('Summon:')");
                await Expect(page, "Tap one of your tiles to summon on");
                // Tiles are chosen on the map only: a tile the stance can't use just shows what it is.
                var other = page.Locator(".hex-map .hex:not(.marked):not(.dark)");
                if (await other.CountAsync() > 0)
                {
                    await other.First.ClickAsync();
                    await page.Locator(".tile-panel").WaitForAsync();
                }

                await page.Locator(".hex-map .hex.marked").First.ClickAsync();
                await page.FillAsync("#count", "3");
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.Out, "stances-screen.png"), FullPage = true });
                await Expect(page, "Summon 3 Imps");
                await page.ClickAsync("button:has-text('Commit: Summon')");
                await Expect(page, "Round 2 of 8");
                await Expect(page, " off)"); // nobody builds in round 1, so Build is cheaper now
                // A round that begins while the page is open plays once: a banner, and "+N" rising from every held tile.
                await page.Locator(".round-banner").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
                if (await page.Locator(".hex-map .hex-gain").CountAsync() < 3)
                {
                    throw new PlaytestException("The new round doesn't show what the tiles paid.");
                }
                await page.Locator(".hex-map").ScrollIntoViewIfNeededAsync();
                await page.WaitForTimeoutAsync(900); // mid-animation: the banner is up and the gains are rising
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.Out, "new-round-screen.png") });
            });
            await StepAsync(merlin, "views", async page =>
            {
                // The round's report came in on the board: the Report tab is marked until it is opened.
                await page.Locator(".match-tabs a.unread:has-text('Report')").WaitForAsync();
                await page.ClickAsync(".match-tabs a:has-text('Report')");
                await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("#report$"));
                await Expect(page, "You summon 3 Imps");
                await page.WaitForFunctionAsync(
                    "() => { const r = document.querySelector('#report h2').getBoundingClientRect(); return r.top >= 0 && r.bottom <= window.innerHeight; }",
                    null, new PageWaitForFunctionOptions { Timeout = 5_000 });
                if (await page.Locator(".match-tabs a.unread").CountAsync() > 0 || await page.Locator(".hex-map").IsVisibleAsync())
                {
                    throw new PlaytestException("The report view still shows the map, or its tab still says new.");
                }

                var barTop = await page.EvaluateAsync<double>("() => document.querySelector('.top-bar').getBoundingClientRect().top");
                if (Math.Abs(barTop) > 1)
                {
                    throw new PlaytestException($"The top bar scrolled away (its top is at {barTop} px).");
                }

                // What a phone shows, bars and all (the step's own screenshot is the whole page).
                await page.WaitForTimeoutAsync(500);
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.Out, "report-view-screen.png") });
                if (await page.Locator("button:has-text('Everything that happened')").CountAsync() > 0)
                {
                    await page.ClickAsync("button:has-text('Everything that happened')");
                    await Expect(page, "Only what concerns you");
                }

                // The view is in the URL: back returns to it, and a summon half given on the board survives the trip.
                await page.ClickAsync(".match-tabs a:has-text('Standings')");
                await page.Locator("table.standings").WaitForAsync();
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.Out, "standings-view-screen.png") });
                await page.GoBackAsync();
                await Expect(page, "What just happened");
                await page.ClickAsync(".match-tabs a:has-text('Board')");
                await page.Locator(".hex-map").WaitForAsync();
                if (await page.Locator("#report").IsVisibleAsync())
                {
                    throw new PlaytestException("The board still shows the report.");
                }

                await page.ClickAsync("label:has-text('Summon:')");
                await page.FillAsync("#count", "2");
                await page.ClickAsync(".match-tabs a:has-text('Report')");
                await page.ClickAsync(".match-tabs a:has-text('Board')");
                if (await page.InputValueAsync("#count") != "2")
                {
                    throw new PlaytestException("A look at the report lost the orders given on the board.");
                }

                await page.WaitForTimeoutAsync(500);
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.Out, "board-view-screen.png") });
            });
            await StepAsync(merlin, "move", async page =>
            {
                // A move if two of Merlin's tiles touch (the match isn't seeded, so not every run can).
                await page.ClickAsync("button:has-text('Moves (optional)')");
                await Expect(page, "Units step to a neighbouring tile");
                if (await page.Locator("button:has-text('Add a move')").CountAsync() > 0)
                {
                    await page.ClickAsync("button:has-text('Add a move')");
                    await Expect(page, "Tap one of your tiles with units");
                    await page.Locator(".hex-map .hex.marked").First.ClickAsync(); // from
                    await Expect(page, "Tap a neighbouring tile of yours");
                    await page.Locator(".hex-map .hex.marked").First.ClickAsync(); // to
                    await page.ClickAsync("button:has-text('Add this move')");
                    await Expect(page, "Remove");
                }
            });
            await StepAsync(merlin, "attune", async page =>
            {
                await page.ClickAsync("label:has-text('Attune:')");
                // The stance's own choices, not the last one's (the map isn't seeded, so there may be no free tile to tap).
                await page.Locator("text=/Tap a free tile next to your land|No free tile next to your land/").First.WaitForAsync();
                var free = page.Locator(".hex-map .hex.marked");
                if (await free.CountAsync() == 0)
                {
                    await page.ClickAsync("label:has-text('Hold:')");
                }
                else
                {
                    await free.First.ClickAsync();
                }

                await Expect(page, "mana");
                await page.ClickAsync("button:has-text('Commit:')");
                await Expect(page, "Round 3 of 8");
            });
            // An attack if the map offers one (a rival or monsters next to the Imps), else a hold: the match isn't seeded,
            // so which it is varies from run to run.
            await StepAsync(merlin, "attack-pick", async page =>
            {
                await page.ClickAsync("label:has-text('Attack:')");
                await Expect(page, "Tap a rival's or a monster's tile");
                var targets = page.Locator(".hex-map .hex.marked");
                if (await targets.CountAsync() > 0)
                {
                    await targets.First.ClickAsync();
                    await Expect(page, "Send from");
                    // Reinforcements guard home while the army is away: a second tap, on one of Merlin's tiles.
                    await page.ClickAsync("button:has-text('Guard home')");
                    await Expect(page, "Tap one of your tiles to guard");
                    await page.Locator(".hex-map .hex.marked").First.ClickAsync();
                    await page.WaitForSelectorAsync("#reinforce-imps");
                    await page.WaitForSelectorAsync(".ledger"); // the preview, or the problem if mana is short
                }
            });
            await StepAsync(merlin, "attack-commit", async page =>
            {
                if (await page.Locator("button:has-text('Commit:'):not([disabled])").CountAsync() == 0)
                {
                    await page.ClickAsync("label:has-text('Hold:')");
                }

                await page.ClickAsync("button:has-text('Commit:')");
                await Expect(page, "Round 4 of 8");
            });
            // A Ward on one of Merlin's tiles alongside a hold: every seat has a tile, and Ward is among the cheapest spells.
            await StepAsync(merlin, "spell-pick", async page =>
            {
                await page.ClickAsync("label:has-text('Hold:')");
                await page.ClickAsync("button:has-text('Spell (optional)')");
                await page.ClickAsync("label:has-text('Rally')"); // goes only with an attack, and says so
                await Expect(page, "Rally goes with an attack");
                await page.ClickAsync("label:has-text('Ward')");
                await Expect(page, "Tap the tile for Ward");
                await page.Locator(".hex-map .hex.marked").First.ClickAsync();
                await Expect(page, "On ");
                await page.WaitForSelectorAsync(".ledger"); // the preview, or the problem if mana is short
            });
            await StepAsync(merlin, "spell-commit", async page =>
            {
                if (await page.Locator("button:has-text('Commit: Hold and Ward'):not([disabled])").CountAsync() == 0)
                {
                    await page.ClickAsync("label:has-text('No spell')"); // too little mana this run
                }

                await page.ClickAsync("button:has-text('Commit:')");
                await Expect(page, "Round 5 of 8");
            });
            await StepAsync(merlin, "build", async page =>
            {
                // A Tower on one of Merlin's tiles, if he can afford it (the match isn't seeded).
                await page.ClickAsync("label:has-text('Build:')");
                await page.ClickAsync("label:has-text('Tower:')");
                var spots = page.Locator(".hex-map .hex.marked");
                if (await spots.CountAsync() > 0)
                {
                    await Expect(page, "Tap one of your tiles without an upgrade");
                    await spots.First.ClickAsync();
                    await Expect(page, "Build a Tower", optional: true);
                }

                if (await page.Locator("button:has-text('Commit: Build'):not([disabled])").CountAsync() == 0)
                {
                    await page.ClickAsync("label:has-text('Hold:')");
                }

                await page.ClickAsync("button:has-text('Commit:')");
                await Expect(page, "Round 6 of 8");
            });
            await StepAsync(merlin, "to-the-end", async page =>
            {
                for (var round = 6; round <= 8; round++)
                {
                    await page.ClickAsync("label:has-text('Hold:')");
                    await page.ClickAsync("button:has-text('Commit: Hold')");
                    await Expect(page, round < 8 ? $"Round {round + 1} of 8" : "Rivals' traits:");
                }

                await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("#standings$"));
                await page.ClickAsync(".match-tabs a:has-text('Board')");
                await Expect(page, "The match is over");
                await page.ClickAsync("a:has-text('See the standings')");
                await page.Locator("table.standings").WaitForAsync(); // open by itself at the end
                await page.Locator(".standings-sum", new PageLocatorOptions { HasTextRegex = new System.Text.RegularExpressions.Regex(" wins?, ") }).WaitForAsync();
            });
            var invite = string.Empty;
            var friendMatch = string.Empty;
            await StepAsync(merlin, "invite", async page =>
            {
                await GoAsync(page, "/match/new");
                await page.ClickAsync("label:has-text('2 players')");
                await page.ClickAsync("button:has-text('Invite friends')");
                await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/join/[a-z0-9]+$"));
                await Expect(page, "Send this link");
                await Expect(page, "Starts now: 1 person and 1 bot");
                invite = new Uri(page.Url).AbsolutePath;
            });
            await StepAsync(morgana, "join", async page =>
            {
                await GoAsync(page, invite);
                await Expect(page, "Take seat 2");
                await page.ClickAsync("button:has-text('Join')");
                await Expect(page, "Waiting for the host to start the match");
            });
            await StepAsync(merlin, "start-with-a-friend", async page =>
            {
                await page.ClickAsync("button:has-text('Refresh')");
                await Expect(page, "Starts now: 2 people and 0 bots");
                await page.ClickAsync("button:has-text('Start the match')");
                await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/match/\\d+$"));
                await Expect(page, "Welcome to match");
                friendMatch = new Uri(page.Url).AbsolutePath;
            });
            await StepAsync(merlin, "pick-and-wait", async page =>
            {
                await page.ClickAsync("button:has-text('Next: choose your trait')");
                await page.Locator("input[name='trait']").First.CheckAsync();
                await page.ClickAsync("button:has-text('Choose '):not([disabled])");
                await Expect(page, "Opening draft 1 of 3");
                await page.Locator("label.choice:has(input[name='pick'])").First.ClickAsync();
                await page.ClickAsync("button:has-text('Pick '):not([disabled])");
                // Morgana got the last tile, and the second draft starts with her.
                await Expect(page, "Seat 2 picks next");
                await Expect(page, "if the pick isn't made by then, a bot makes it");
            });
            await StepAsync(morgana, "pick-in-turn", async page =>
            {
                await GoAsync(page, invite); // the invite sends a player in the match on to it
                await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/match/\\d+$"));
                await page.ClickAsync("button:has-text('Next: choose your trait')");
                await page.Locator("input[name='trait']").First.CheckAsync();
                await page.ClickAsync("button:has-text('Choose '):not([disabled])");
                await Expect(page, "Opening draft 2 of 3");
                await page.Locator("label.choice:has(input[name='pick'])").First.ClickAsync();
                await page.ClickAsync("button:has-text('Pick '):not([disabled])");
                await Expect(page, "Opening draft 3 of 3");
                await Expect(page, "Seat 1 picks next");
            });
            await StepAsync(merlin, "host", async page =>
            {
                await GoAsync(page, friendMatch);
                await Expect(page, "Due in 23 h");
                await page.ClickAsync("button:has-text('Seat 2'):has-text('hand to a bot')");
                await Expect(page, "can't take it back");
            });
            await StepAsync(merlin, "hand-off", async page =>
            {
                await page.ClickAsync("button:has-text('Hand seat 2 to a bot')");
                await Expect(page, "A bot plays seat 2 from now on");
                await page.Locator("label.choice:has(input[name='pick'])").First.ClickAsync();
                await page.ClickAsync("button:has-text('Pick '):not([disabled])");
                await Expect(page, "Round 1 of 8"); // the bot got the last tile
            });
            await StepAsync(morgana, "watching", async page =>
            {
                await GoAsync(page, friendMatch);
                await Expect(page, "A bot plays your seat now");
                if (await page.Locator("button:has-text('Commit')").CountAsync() > 0)
                {
                    throw new PlaytestException("A player whose seat a bot plays can still give orders.");
                }
            });
            await StepAsync(morgana, "big-map", async page =>
            {
                // Five players: the 37-tile map, the one whose hexes are smallest at 360 px.
                await GoAsync(page, "/match/new");
                await page.ClickAsync("label:has-text('5 players')");
                await page.ClickAsync("button:has-text('Play bots')");
                await page.ClickAsync("button:has-text('Next: choose your trait')");
                await page.Locator("input[name='trait']").First.CheckAsync();
                await page.ClickAsync("button:has-text('Choose '):not([disabled])");
                await Expect(page, "Opening draft 1 of 3");
                await page.Locator(".hex-map .hex.marked").First.ClickAsync();
                await Expect(page, "to pick");
                bigMap = new Uri(page.Url).PathAndQuery;
            });
            // Every look (docs/theme.md), picked on the account page, then the 37-tile map in it; the default last, so
            // the steps after these are in it again.
            foreach (var look in new[] { "sigil", "table", "dust", "slate", "grimoire" })
            {
                await StepAsync(morgana, $"look-{look}", async page =>
                {
                    await GoAsync(page, "/Account/Manage");
                    await page.ClickAsync($".look-option[href^='look/{look}?']");
                    await page.WaitForURLAsync(url => url.Contains("/Account/Manage", StringComparison.Ordinal));
                    var set = await page.GetAttributeAsync("html", "data-look");
                    if (set != look)
                    {
                        throw new PlaytestException($"Picking the {look} look left data-look at \"{set}\".");
                    }

                    await Expect(page, "(in use)");
                    await page.Locator(".look-list").ScrollIntoViewIfNeededAsync();
                    await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.Out, $"look-{look}-account.png") });
                    await GoAsync(page, bigMap ?? "/");
                    await page.Locator(".hex-map").First.WaitForAsync();
                    await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.Out, $"look-{look}-screen.png") });
                });
            }
            await StepAsync(morgana, "privacy", async page =>
            {
                // Signed in, the Account page links the notice; it is in folds: one tap opens a section.
                await GoAsync(page, "/Account/Manage");
                await page.ClickAsync("a:has-text('privacy notice')");
                await page.ClickAsync("summary:has-text('Cookies')");
                await Expect(page, "No ads, no analytics");
            });
            await StepAsync(morgana, "not-found", async page =>
            {
                // The page an unknown address shows (opened directly: an unknown address answers 404, which the browser
                // logs as an error), and a match nobody seats this player in.
                await GoAsync(page, "/not-found", interactive: false);
                await Expect(page, "Not Found");
                await GoAsync(page, "/match/999999");
                await Expect(page, "There is no match 999999 that you sit in");
            });
            await StepAsync(morgana, "not-an-admin", async page =>
            {
                await page.GotoAsync($"{options.Url}/admin");
                await Expect(page, "Access denied", optional: true);
                if (page.Url.EndsWith("/admin", StringComparison.Ordinal) && await page.Locator("h2:has-text('Problems')").CountAsync() > 0)
                {
                    throw new PlaytestException("A player who isn't an admin saw the admin page.");
                }
            });
            await StepAsync(ada, "admin", async page =>
            {
                await GoAsync(page, "/admin");
                await Expect(page, "Opening draft 1 of 3, pick 1 of 5, 5 seats");
                await Expect(page, "Problems");
            });
            await StepAsync(ada, "end-game", async page =>
            {
                await GoAsync(page, "/admin");
                await page.Locator("button:has-text('now…')").First.ClickAsync();
                await page.ClickAsync("button:has-text('Yes, end game')");
                await Expect(page, "has ended");
            });
            await StepAsync(morgana, "home-after-end", async page =>
            {
                await GoAsync(page, "/");
                await Expect(page, "Ended early by an admin");
            });
            await StepAsync(ada, "problems", async page =>
            {
                await GoAsync(page, "/admin");
                var problems = await page.EvaluateAsync<string[]>(
                    """
                    () => {
                      const out = [];
                      let el = [...document.querySelectorAll('h2')].find(h => h.textContent.trim() === 'Problems')?.nextElementSibling;
                      for (; el && el.tagName !== 'H2'; el = el.nextElementSibling)
                        if (el.classList.contains('admin-player')) out.push(el.innerText.replace(/\s+/g, ' ').trim());
                      return out;
                    }
                    """);
                if (problems.Length > 0)
                {
                    throw new PlaytestException("The admin page lists problems: " + string.Join(" | ", problems));
                }
            });

            foreach (var player in new[] { ada, merlin, morgana })
            {
                await player.Context.CloseAsync();
            }
        }

        public string Report(IReadOnlyList<string> appErrors)
        {
            var failed = steps.Count(s => !s.Passed);
            var warned = steps.Count(s => s.Warnings.Count > 0);
            var text = new StringBuilder();
            text.AppendLine("# Playtest report").AppendLine();
            text.AppendLine(failed == 0 && appErrors.Count == 0
                ? $"**Passed**: {steps.Count} steps, no errors in the app log."
                : $"**Failed**: {failed} of {steps.Count} steps failed, {appErrors.Count} errors in the app log.");
            text.AppendLine($"{warned} steps have warnings (tap targets under 44 px). Viewport 360×740.").AppendLine();
            text.AppendLine("| # | Player | Step | Result | Screenshot |").AppendLine("|---|---|---|---|---|");
            foreach (var s in steps)
            {
                text.AppendLine($"| {s.Number} | {s.Player} | {s.Name} | {(s.Passed ? "ok" : "**FAILED**")}{(s.Warnings.Count > 0 ? " (warnings)" : "")} | {s.Screenshot} |");
            }

            foreach (var s in steps.Where(s => s.Problems.Count > 0 || s.Warnings.Count > 0))
            {
                text.AppendLine().AppendLine($"## {s.Number}. {s.Player}: {s.Name}");
                foreach (var problem in s.Problems)
                {
                    text.AppendLine($"- **Problem:** {problem}");
                }

                foreach (var warning in s.Warnings)
                {
                    text.AppendLine($"- Warning: {warning}");
                }
            }

            if (appErrors.Count > 0)
            {
                text.AppendLine().AppendLine("## Errors in the app log");
                foreach (var error in appErrors)
                {
                    text.AppendLine($"- {error}");
                }
            }

            return text.ToString();
        }

        private async Task<Player> PlayerAsync(string name, string email)
        {
            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 360, Height = 740 },
                IsMobile = true,
                HasTouch = true,
                DeviceScaleFactor = 2,
            });
            var page = await context.NewPageAsync();
            var player = new Player(name, email, context, page);
            page.Console += (_, message) =>
            {
                // Playwright's windows are private, where Chrome refuses push subscriptions and says so in the console.
                // That's the browser's limit, not the game's: the notifications step checks the page handles it.
                if (message.Text.Contains("does not support the Push API in incognito", StringComparison.Ordinal))
                {
                    return;
                }

                if (message.Type is "error" or "warning")
                {
                    player.Console.Add($"console {message.Type}: {message.Text}");
                }
            };
            page.PageError += (_, error) => player.Console.Add($"page error: {error}");
            page.RequestFailed += (_, request) =>
            {
                if (request.Failure is { } failure && !failure.Contains("ERR_ABORTED", StringComparison.Ordinal))
                {
                    player.FailedRequests.Add($"{request.Method} {request.Url}: {failure}");
                }
            };
            page.Response += (_, response) =>
            {
                if (response.Status >= 500)
                {
                    player.FailedRequests.Add($"{response.Request.Method} {response.Url}: {response.Status}");
                }
            };
            return player;
        }

        /// <summary>Runs one step, then checks the page and screenshots it. A failed step is recorded and the run goes on.</summary>
        private async Task StepAsync(Player player, string name, Func<IPage, Task> action)
        {
            var number = steps.Count + 1;
            var screenshot = $"{number:00}-{player.Name.ToLowerInvariant()}-{name}.png";
            var problems = new List<string>();
            var warnings = new List<string>();
            player.Console.Clear();
            player.FailedRequests.Clear();
            Console.Write($"{number,2}. {player.Name}: {name} ... ");
            try
            {
                await action(player.Page);
            }
            catch (Exception ex)
            {
                problems.Add(ex is PlaytestException or TimeoutException or PlaywrightException
                    ? ex.Message.Split('\n')[0]
                    : ex.ToString());
            }

            try
            {
                var check = PageCheck.From(await player.Page.EvaluateAsync<System.Text.Json.JsonElement>(PageCheck.Script));
                if (check.Overflow > 0)
                {
                    problems.Add($"The page scrolls sideways by {check.Overflow} px at 360 px wide.");
                }

                if (check.Coordinates.Length > 0)
                {
                    problems.Add($"Tile coordinates on the page, where a name belongs: {string.Join(", ", check.Coordinates)}.");
                }

                if (check.ErrorBar)
                {
                    problems.Add("Blazor's error bar is showing (an unhandled error in the page's connection).");
                }

                warnings.AddRange(check.SmallTargets.Select(t => $"tap target under 44 px: {t}"));
                await player.Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(options.Out, screenshot), FullPage = true });
            }
            catch (Exception ex)
            {
                problems.Add($"Couldn't check or screenshot the page: {ex.Message.Split('\n')[0]}");
            }

            problems.AddRange(player.Console.Where(c => !c.StartsWith("console warning", StringComparison.Ordinal)));
            warnings.AddRange(player.Console.Where(c => c.StartsWith("console warning", StringComparison.Ordinal)));
            problems.AddRange(player.FailedRequests.Select(r => $"request failed: {r}"));
            steps.Add(new StepResult(number, player.Name, name, problems.Count == 0, screenshot, problems, warnings));
            Console.WriteLine(problems.Count == 0 ? "ok" : "FAILED: " + problems[0]);
        }

        /// <summary>
        /// Opens a page and, if it's <paramref name="interactive"/> (has a live connection; the account pages and the
        /// privacy notice are plain pages), waits until the connection is up, so clicks aren't lost.
        /// </summary>
        private async Task GoAsync(IPage page, string path, bool interactive = true)
        {
            if (!interactive || path.StartsWith("/Account/", StringComparison.Ordinal))
            {
                await page.GotoAsync($"{options.Url}{path}");
                return;
            }

            var circuit = page.WaitForWebSocketAsync(new PageWaitForWebSocketOptions
            {
                Predicate = ws => ws.Url.Contains("_blazor", StringComparison.Ordinal),
                Timeout = 15_000,
            });
            await page.GotoAsync($"{options.Url}{path}");
            await circuit;
            await WaitForCircuitAsync(page);
        }

        /// <summary>
        /// Waits for the page to become interactive: Blazor replaces the prerendered page once its connection is up.
        /// There's no event for that, so wait for the connection's first messages to settle.
        /// </summary>
        private static async Task WaitForCircuitAsync(IPage page)
        {
            await page.WaitForFunctionAsync("() => window.Blazor !== undefined", null, new PageWaitForFunctionOptions { Timeout = 15_000 });
            await page.WaitForTimeoutAsync(800);
        }

        private static async Task Expect(IPage page, string text, bool optional = false)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < until)
            {
                var content = await page.InnerTextAsync("body");
                if (content.Contains(text, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                await page.WaitForTimeoutAsync(250);
            }

            if (!optional)
            {
                throw new PlaytestException($"Expected \"{text}\" on {page.Url}.");
            }
        }
    }

    internal sealed class PlaytestException(string message) : Exception(message);

    /// <summary>What the page check script returns.</summary>
    internal sealed record PageCheck(int Overflow, bool ErrorBar, string[] SmallTargets, string[] Coordinates)
    {
        public static PageCheck From(System.Text.Json.JsonElement json) => new(
            json.GetProperty("overflow").GetInt32(),
            json.GetProperty("errorBar").GetBoolean(),
            [.. json.GetProperty("smallTargets").EnumerateArray().Select(t => t.GetString() ?? "")],
            [.. json.GetProperty("coordinates").EnumerateArray().Select(t => t.GetString() ?? "")]);

        public const string Script =
            """
            () => {
              const d = document.documentElement;
              const targets = [...document.querySelectorAll(
                'a[href], button, label.choice, input:not([type=hidden]):not([type=radio]), select, textarea')]
                .filter(e => e.offsetParent !== null && !e.closest('#blazor-error-ui'))
                .map(e => ({ e, r: e.getBoundingClientRect() }))
                .filter(({ r }) => r.width > 0 && (r.height < 44 || r.width < 44))
                .map(({ e, r }) => `"${(e.innerText || e.value || e.getAttribute('aria-label') || e.tagName).trim().replace(/\s+/g, ' ').slice(0, 40)}" ${Math.round(r.width)}×${Math.round(r.height)}`);
              const bar = document.getElementById('blazor-error-ui');
              // Players see tiles by name ("C3"), never as coordinates ("(0, 1)"): in the text, the dropdowns and the map's labels.
              const text = [document.body.innerText,
                ...[...document.querySelectorAll('option')].map(o => o.textContent),
                ...[...document.querySelectorAll('[aria-label]')].map(e => e.getAttribute('aria-label'))].join('\n');
              return {
                overflow: Math.max(0, d.scrollWidth - d.clientWidth),
                errorBar: !!bar && getComputedStyle(bar).display !== 'none',
                smallTargets: [...new Set(targets)],
                coordinates: [...new Set(text.match(/\(-?\d+, -?\d+\)/g) || [])].slice(0, 5),
              };
            }
            """;
    }
}
