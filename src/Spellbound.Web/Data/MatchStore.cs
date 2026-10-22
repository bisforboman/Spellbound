using Spellbound.Domain;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Data;

/// <summary>A match as it is loaded: the game it is, who sits where (null for a bot), and the match itself.</summary>
public sealed record LoadedMatch(long Id, IReadOnlyList<string?> Owners, Match Match)
{
    /// <summary>What the last action said, or empty before the first.</summary>
    public IReadOnlyList<string> LastReport { get; init; } = [];

    /// <summary>Whom each line of <see cref="LastReport"/> concerns, for the fog of war (none: everyone).</summary>
    public IReadOnlyList<LineTag?>? LastReportTags { get; init; }

    /// <summary>The lines of the last report that a seat may read (docs/game-design.md, Fog of war).</summary>
    public IReadOnlyList<string> LastReportFor(int seat) => Match.ReportFor(new Report(true, LastReport, LastReportTags), seat).Lines;

    /// <summary>False once the match is over or an admin ended it.</summary>
    public bool IsRunning { get; init; } = true;

    /// <summary>The seats whose person has committed this step and is waiting for the others (never what they chose).</summary>
    public IReadOnlySet<int> Committed { get; init; } = new HashSet<int>();

    /// <summary>The seats played by people (the rest are bots).</summary>
    public IEnumerable<int> People => Owners.Select((owner, seat) => (owner, seat)).Where(s => s.owner is not null).Select(s => s.seat);

    /// <summary>The seat this account sits in, or null.</summary>
    public int? SeatOf(string ownerId)
    {
        for (var seat = 0; seat < Owners.Count; seat++)
        {
            if (Owners[seat] == ownerId)
            {
                return seat;
            }
        }

        return null;
    }
}

/// <summary>A match a player sits in, for their list: its id, their seat, where it stands, and whether it still runs.</summary>
public sealed record PlayerMatch(long Id, int Seat, string Description, bool IsRunning);

/// <summary>
/// Matches in the database (docs/architecture.md): each is a row in the host's games table, its rules in the row's
/// rules and its state (<see cref="StoredMatch"/>) in the row's state, plus a match_players row per human seat. Every
/// change loads the match with its row locked, calls one domain method and saves, in one transaction, so two actions
/// on the same match take turns.
/// </summary>
public sealed class MatchStore(HostDatabase database)
{
    /// <summary>The host's mode for a match.</summary>
    public const string Mode = "match";

    /// <summary>The host keeps a clock for every game; a match has no deadline yet (M1 plays its rounds at once), so
    /// this is only how long it may stay open.</summary>
    public static readonly TimeSpan OpenFor = TimeSpan.FromDays(28);

    /// <summary>Starts a match with a seat for each owner (null for a bot) and returns its id.</summary>
    public async Task<long> CreateAsync(IReadOnlyList<string?> owners, GameRules rules, DateTimeOffset now, int? seed = null)
    {
        await using var db = await database.BeginAsync();
        var id = await CreateAsync(db, owners, rules, now, seed);
        await db.CommitAsync();
        return id;
    }

    /// <summary>Starts a match inside the caller's transaction (a lobby starts its match and records it together).</summary>
    public static async Task<long> CreateAsync(DbSession db, IReadOnlyList<string?> owners, GameRules rules, DateTimeOffset now, int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(owners);
        var chosen = seed ?? Random.Shared.Next();
        var match = Match.Start(owners.Count, rules, new SeededRandom(chosen));

        await db.LockGamesOfModeAsync(Mode);
        var game = await db.InsertGameAsync(new NewGame(Mode, OpenFor, MatchJson.Serialize(rules)), now);
        await db.SaveGameStateAsync(game.Id, MatchJson.Serialize(new StoredMatch(chosen, match.Save())));
        for (var seat = 0; seat < owners.Count; seat++)
        {
            if (owners[seat] is { } owner)
            {
                await db.ExecuteAsync("INSERT INTO match_players (game_id, seat, owner_id) VALUES ($1, $2, $3)", game.Id, seat, owner);
            }
        }

        return game.Id;
    }

    /// <summary>A match by id, or null if there is none.</summary>
    public async Task<LoadedMatch?> LoadAsync(long id)
    {
        await using var db = await database.BeginAsync();
        return await LoadAsync(db, id, forUpdate: false) is { } loaded ? loaded.Match with { IsRunning = loaded.IsRunning } : null;
    }

    /// <summary>
    /// Runs one action on a running match with its row locked: <paramref name="action"/> gets the match and the
    /// randomness for this step, and the match is saved only if the action succeeds. When the match is over, its game
    /// ends with it. Null if there is no such match; a failed report if it has ended (or an admin ended it).
    /// </summary>
    public async Task<Report?> ApplyAsync(long id, DateTimeOffset now, Func<LoadedMatch, IRandomSource, Report> action, bool isStep = true)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var db = await database.BeginAsync();
        if (await LoadAsync(db, id, forUpdate: true) is not { } loaded)
        {
            return null;
        }

        if (!loaded.IsRunning)
        {
            return Report.Fail("This match has ended.");
        }

        var report = action(loaded.Match, StepRandom(loaded.Seed, loaded.Match.Match));
        if (report.Succeeded)
        {
            // Only a step that resolves (an auction or a round) becomes the match's last report; a trait pick is the
            // picker's alone.
            await db.SaveGameStateAsync(id, MatchJson.Serialize(isStep
                ? new StoredMatch(loaded.Seed, loaded.Match.Match.Save(), report.Lines, report.Tags)
                : new StoredMatch(loaded.Seed, loaded.Match.Match.Save(), loaded.Match.LastReport, loaded.Match.LastReportTags)));
            if (loaded.Match.Match.Phase == MatchPhase.Ended)
            {
                await db.EndGameAsync(id, now);
            }

            await db.CommitAsync();
        }

        return report;
    }

    /// <summary>
    /// A person commits their part of the current step (a bid, or a round's orders) and waits for the others. With the
    /// match locked: <paramref name="check"/> says whether the commitment is valid, and if so it is stored on the
    /// person's seat. When every person has committed, <paramref name="resolve"/> gets all their commitments (by seat)
    /// and the step's randomness, and the step resolves and is saved as the last report. Returns what happened and
    /// whether the step resolved; null if there is no such match.
    /// </summary>
    public async Task<(Report Report, bool Resolved)?> CommitAsync(long id, string ownerId, DateTimeOffset now, string pending,
        Func<LoadedMatch, int, Report> check, Func<LoadedMatch, IReadOnlyDictionary<int, string>, IRandomSource, Report> resolve)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(resolve);
        await using var db = await database.BeginAsync();
        if (await LoadAsync(db, id, forUpdate: true) is not { } loaded)
        {
            return null;
        }

        if (!loaded.IsRunning)
        {
            return (Report.Fail("This match has ended."), false);
        }

        if (loaded.Match.SeatOf(ownerId) is not { } seat)
        {
            return (Report.Fail("You don't sit in this match."), false);
        }

        var checkedReport = check(loaded.Match, seat);
        if (!checkedReport.Succeeded)
        {
            return (checkedReport, false);
        }

        var step = StepKey(loaded.Match.Match);
        await db.ExecuteAsync("UPDATE match_players SET pending = $3::jsonb, pending_for = $4 WHERE game_id = $1 AND seat = $2",
            id, seat, pending, step);
        var all = (await db.QueryAsync(
                "SELECT seat, pending::text FROM match_players WHERE game_id = $1 AND pending_for = $2",
                r => (Seat: r.GetInt32(0), Pending: r.GetString(1)), id, step))
            .ToDictionary(p => p.Seat, p => p.Pending);
        var waiting = loaded.Match.People.Where(s => !all.ContainsKey(s)).ToList();
        if (waiting.Count > 0)
        {
            await db.CommitAsync();
            return (new Report(true, [.. checkedReport.Lines,
                $"Committed. Waiting for {string.Join(" and ", waiting.Select(s => $"Seat {s + 1}"))}; you can take it back until then."]), false);
        }

        var report = resolve(loaded.Match, all, StepRandom(loaded.Seed, loaded.Match.Match));
        if (!report.Succeeded)
        {
            return (report, false); // nothing is saved
        }

        await db.SaveGameStateAsync(id, MatchJson.Serialize(new StoredMatch(loaded.Seed, loaded.Match.Match.Save(), report.Lines, report.Tags)));
        await db.ExecuteAsync("UPDATE match_players SET pending = NULL, pending_for = NULL WHERE game_id = $1", id);
        if (loaded.Match.Match.Phase == MatchPhase.Ended)
        {
            await db.EndGameAsync(id, now);
        }

        await db.CommitAsync();
        return (report, true);
    }

    /// <summary>A person takes back what they committed this step, if it hasn't resolved. Changes nothing otherwise.</summary>
    public async Task<bool> WithdrawAsync(long id, string ownerId)
    {
        await using var db = await database.BeginAsync();
        if (await LoadAsync(db, id, forUpdate: true) is not { IsRunning: true } loaded || loaded.Match.SeatOf(ownerId) is not { } seat)
        {
            return false;
        }

        var changed = await db.ExecuteAsync(
            "UPDATE match_players SET pending = NULL, pending_for = NULL WHERE game_id = $1 AND seat = $2 AND pending_for = $3",
            id, seat, StepKey(loaded.Match.Match));
        await db.CommitAsync();
        return changed > 0;
    }

    /// <summary>Which step of a match a commitment is for: an opening auction or a round.</summary>
    public static string StepKey(Match match) =>
        match.Phase == MatchPhase.Opening ? $"opening-{match.OpeningUnveil}" : $"round-{match.CurrentRound}";

    /// <summary>The matches a player sits in, newest first.</summary>
    public async Task<IReadOnlyList<PlayerMatch>> ListForAsync(string ownerId)
    {
        await using var db = await database.BeginAsync();
        var seats = await db.QueryAsync(
            "SELECT game_id, seat FROM match_players WHERE owner_id = $1 ORDER BY game_id DESC",
            r => (Id: r.GetInt64(0), Seat: r.GetInt32(1)),
            ownerId);
        var list = new List<PlayerMatch>();
        foreach (var (id, seat) in seats)
        {
            if (await LoadAsync(db, id, forUpdate: false) is { } loaded)
            {
                list.Add(new PlayerMatch(id, seat, Describe(loaded.Match.Match, loaded.IsRunning), loaded.IsRunning));
            }
        }

        return list;
    }

    /// <summary>Where a match stands, in a few words.</summary>
    public static string Describe(Match match, bool isRunning) => (isRunning, match.Phase) switch
    {
        (_, MatchPhase.Ended) => $"Over after {match.Rules.Rounds} rounds",
        (false, _) => "Ended early by an admin",
        (_, MatchPhase.Opening) => $"Opening auction {match.OpeningUnveil} of {match.Rules.OpeningUnveils}",
        _ => $"Round {match.CurrentRound} of {match.Rules.Rounds}",
    };

    /// <summary>The randomness for a match's next step, from its seed and where it is: each opening auction and each
    /// round gets its own, so a step never depends on how often the match was saved before it. Not HashCode, which
    /// changes from one run of the server to the next.</summary>
    public static IRandomSource StepRandom(int seed, Match match) => new SeededRandom(unchecked(match.Phase switch
    {
        MatchPhase.Opening => seed + match.OpeningUnveil,
        _ => seed + 100 + match.CurrentRound,
    }));

    /// <summary>A match in an open transaction, with its seed and whether its game still runs; null if the game isn't
    /// a match.</summary>
    internal static async Task<(int Seed, LoadedMatch Match, bool IsRunning)?> LoadAsync(DbSession db, long id, bool forUpdate)
    {
        if (await db.LoadGameAsync(id, forUpdate) is not { Mode: Mode } game || await db.LoadGameStateAsync(id) is not { } json)
        {
            return null;
        }

        var stored = MatchJson.Deserialize<StoredMatch>(json);
        var owners = new string?[stored.Match.Seats.Count];
        var match = Match.Load(stored.Match, MatchJson.Deserialize<GameRules>(game.RulesJson));
        var step = StepKey(match);
        var committed = new HashSet<int>();
        foreach (var (seat, owner, pendingFor) in await db.QueryAsync(
                     "SELECT seat, owner_id, pending_for FROM match_players WHERE game_id = $1",
                     r => (r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)), id))
        {
            owners[seat] = owner;
            if (pendingFor == step)
            {
                committed.Add(seat);
            }
        }

        return (stored.Seed, new LoadedMatch(id, owners, match)
        {
            LastReport = stored.LastReport ?? [], LastReportTags = stored.LastReportTags, Committed = committed,
        }, game.IsRunning);
    }
}
