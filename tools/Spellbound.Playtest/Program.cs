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
        // Fast rules: a new round copies the configured rules, so the playtest round runs on these.
        ["GameRules__TurnInterval"] = "00:00:01",
        ["GameRules__ProtectionTurns"] = "0",
        ["GameRules__ProtectionDuration"] = "00:00:00",
        ["GameRules__StartingGold"] = "50000",
        ["GameRules__StartingMana"] = "8000",
        // Three players from one address would trip the per-address limits.
        ["RateLimits__AccountPosts"] = "1000",
        ["RateLimits__ConnectionRequestsPerMinute"] = "10000",
        ["RateLimits__RequestsPerMinute"] = "10000",
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
    internal sealed record Player(string Name, string Email, string School, IBrowserContext Context, IPage Page)
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
            var ada = await PlayerAsync("Ada", AdminEmail, "Dawn");
            var merlin = await PlayerAsync("Merlin", "merlin@playtest.local", "Grove");
            var morgana = await PlayerAsync("Morgana", "morgana@playtest.local", "Ember");

            foreach (var player in new[] { ada, merlin, morgana })
            {
                await StepAsync(player, "sign-in", async page =>
                {
                    await GoAsync(page, "/Account/Login");
                    await page.FillAsync("#email", player.Email);
                    await page.ClickAsync("form[action='Account/DevSignIn'] button[type=submit]");
                    await page.WaitForURLAsync($"{options.Url}/");
                    await WaitForCircuitAsync(page);
                    await Expect(page, "Your games");
                    await Expect(page, "You have no mage this round yet");
                });
                await StepAsync(player, "create-mage", async page =>
                {
                    await page.ClickAsync("a[href='create']");
                    await page.WaitForURLAsync("**/create");
                    await WaitForCircuitAsync(page);
                    await page.FillAsync("#name", player.Name);
                    await page.ClickAsync($"label.choice:has(b:text-is('{player.School}'))");
                    await page.ClickAsync("button[type=submit]:has-text('Begin')");
                    await page.WaitForURLAsync("**/realm");
                    await WaitForCircuitAsync(page);
                    await page.Locator("h1", new PageLocatorOptions { HasTextString = player.Name }).WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
                    await Expect(page, $"{player.School} mage");
                    await ExpectElement(page, ".mage-head .sigil svg");
                    await ExpectElement(page, ".turns .moon");
                    await ExpectElement(page, ".vial[role=meter]");
                });
            }

            await StepAsync(ada, "games-with-mage", async page =>
            {
                await GoAsync(page, "/");
                await Expect(page, "Ada, Dawn mage");
                await Expect(page, "Sanctuary");
            });
            await StepAsync(ada, "explore", page => ActAsync(page, "/explore", async () =>
            {
                await page.FillAsync("#turns", "5");
                await ExpectElement(page, ".ledger .ledger-line");
                await page.ClickAsync("button[type=submit]:has-text('Explore')");
            }));
            await StepAsync(ada, "build", page => ActAsync(page, "/build", async () =>
            {
                await page.FillAsync("#count", "10");
                await ExpectElement(page, ".ledger .ledger-line");
                await page.ClickAsync("button[type=submit]:has-text('Build')");
            }));
            await StepAsync(ada, "meditate", page => ActAsync(page, "/magic", async () =>
            {
                await ExpectElement(page, ".vial[role=meter]");
                await page.FillAsync("#turns", "3");
                await ExpectElement(page, ".ledger .ledger-line");
                await page.ClickAsync("button[type=submit]:has-text('Meditate')");
            }));
            await StepAsync(ada, "cast", page => ActAsync(page, "/magic", () =>
                page.Locator("button.btn-primary:not([type=submit])").First.ClickAsync()));
            await StepAsync(ada, "recruit", page => ActAsync(page, "/army", async () =>
            {
                await page.ClickAsync("label.choice:has(b:text-is('Unit 3'))");
                await page.FillAsync("#count", "40");
                await ExpectElement(page, ".ledger .ledger-line");
                await page.ClickAsync("button[type=submit]:has-text('Recruit')");
            }));
            await StepAsync(ada, "rivals", async page =>
            {
                await GoAsync(page, "/rivals");
                await Expect(page, "Merlin");
                await Expect(page, "Net power counts");
            });
            await StepAsync(ada, "attack", async page =>
            {
                await GoAsync(page, "/rivals");
                await page.ClickAsync("a.rank-row:has-text('Merlin')");
                await page.WaitForURLAsync("**/rivals/*");
                await WaitForCircuitAsync(page);
                await ExpectElement(page, ".mage-head .sigil svg");
                await ClickAndReportAsync(page, () => page.ClickAsync("button.btn-danger:has-text('Attack')"));
                await ExpectElement(page, ".alert[role=status] .stamp"); // luck decides which verdict
                await page.WaitForTimeoutAsync(500); // let the stamp land before the screenshot
            });
            await StepAsync(ada, "hex", async page =>
            {
                await page.ReloadAsync();
                await WaitForCircuitAsync(page);
                await ClickAndReportAsync(page, () => page.Locator("button.btn-danger").Last.ClickAsync());
            });
            await StepAsync(merlin, "attacked-banner", async page =>
            {
                await GoAsync(page, "/realm");
                await Expect(page, "You were targeted");
            });
            await StepAsync(merlin, "chronicle", async page =>
            {
                await GoAsync(page, "/chronicle");
                await Expect(page, "Ada (Dawn) cast Dawn hex at Merlin (Grove)");
            });
            await StepAsync(merlin, "hall-of-fame", page => GoAsync(page, "/halloffame"));
            await StepAsync(merlin, "account", async page =>
            {
                await GoAsync(page, "/Account/Manage");
                var data = await page.Context.APIRequest.GetAsync($"{options.Url}/Account/Data");
                if (!data.Ok || !(await data.TextAsync()).Contains("Merlin", StringComparison.Ordinal))
                {
                    throw new PlaytestException($"Download my data answered {data.Status} without Merlin's mage.");
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
            await StepAsync(morgana, "privacy", page => GoAsync(page, "/privacy", interactive: false));
            await StepAsync(morgana, "not-an-admin", async page =>
            {
                await page.GotoAsync($"{options.Url}/admin");
                await Expect(page, "Access denied", optional: true);
                if (page.Url.EndsWith("/admin", StringComparison.Ordinal) && await page.Locator("h2:has-text('Problems')").CountAsync() > 0)
                {
                    throw new PlaytestException("A player who isn't an admin saw the admin page.");
                }
            });
            await StepAsync(morgana, "vigil", async page =>
            {
                await GoAsync(page, "/realm");
                await page.FillAsync("#vigil-hours", "1");
                await ExpectElement(page, ".ledger .ledger-line");
                await ClickAndReportAsync(page, () => page.ClickAsync("button[type=submit]:has-text('Keep a Vigil')"));
                await Expect(page, "You are keeping a Vigil");
                await Expect(page, "frozen by your Vigil");
            });
            await StepAsync(ada, "rival-in-vigil", async page =>
            {
                await GoAsync(page, "/rivals");
                await Expect(page, "keeping a Vigil");
                await page.ClickAsync("a.rank-row:has-text('Morgana')");
                await page.WaitForURLAsync("**/rivals/*");
                await WaitForCircuitAsync(page);
                await Expect(page, "Morgana is keeping a Vigil and can't be struck");
            });
            await StepAsync(morgana, "end-vigil", async page =>
            {
                await GoAsync(page, "/realm");
                await ClickAndReportAsync(page, () => page.ClickAsync("button:has-text('End the Vigil now')"));
                await Expect(page, "You end your Vigil");
            });
            await StepAsync(ada, "admin", async page =>
            {
                await GoAsync(page, "/admin");
                await Expect(page, "Problems");
            });
            await StepAsync(ada, "end-round", async page =>
            {
                await GoAsync(page, "/admin");
                await page.ClickAsync("button:has-text('now…')");
                await page.ClickAsync("button:has-text('Yes, end round')");
                await Expect(page, "round 2 has started");
            });
            await StepAsync(ada, "games-after-reset", async page =>
            {
                await GoAsync(page, "/");
                await Expect(page, "Round 2");
                await Expect(page, "You have no mage this round yet");
                await GoAsync(page, "/halloffame");
                await Expect(page, "Round 1");
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

        private async Task<Player> PlayerAsync(string name, string email, string school)
        {
            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 360, Height = 740 },
                IsMobile = true,
                HasTouch = true,
                DeviceScaleFactor = 2,
            });
            var page = await context.NewPageAsync();
            var player = new Player(name, email, school, context, page);
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

        private async Task ActAsync(IPage page, string path, Func<Task> act)
        {
            await GoAsync(page, path);
            await ClickAndReportAsync(page, act);
        }

        /// <summary>Does an action and waits for its report; a report saying it failed fails the step.</summary>
        private static async Task ClickAndReportAsync(IPage page, Func<Task> act)
        {
            await act();
            var report = page.Locator(".alert[role=status]").Last;
            await report.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
            if (await report.GetAttributeAsync("class") is { } css && css.Contains("alert-danger", StringComparison.Ordinal))
            {
                throw new PlaytestException("The action failed: " + (await report.InnerTextAsync()).Replace('\n', ' '));
            }
        }

        private static Task ExpectElement(IPage page, string selector) =>
            page.Locator(selector).First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

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
    internal sealed record PageCheck(int Overflow, bool ErrorBar, string[] SmallTargets)
    {
        public static PageCheck From(System.Text.Json.JsonElement json) => new(
            json.GetProperty("overflow").GetInt32(),
            json.GetProperty("errorBar").GetBoolean(),
            [.. json.GetProperty("smallTargets").EnumerateArray().Select(t => t.GetString() ?? "")]);

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
              return {
                overflow: Math.max(0, d.scrollWidth - d.clientWidth),
                errorBar: !!bar && getComputedStyle(bar).display !== 'none',
                smallTargets: [...new Set(targets)],
              };
            }
            """;
    }
}
