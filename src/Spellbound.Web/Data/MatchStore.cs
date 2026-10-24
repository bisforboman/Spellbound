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

    /// <summary>The seats whose person missed two deadlines in a row, or the host handed to a bot: a bot plays them
    /// now, and the person can still watch.</summary>
    public IReadOnlySet<int> Away { get; init; } = new HashSet<int>();

    /// <summary>How many deadlines in a row each person's seat has missed (seats with none are left out).</summary>
    public IReadOnlyDictionary<int, int> Missed { get; init; } = new Dictionary<int, int>();

    /// <summary>When the current step resolves without whoever hasn't committed; null when nobody waits on anyone.</summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>The account that opened the match's invite and may hand a seat to a bot; null for a match against bots.</summary>
    public string? HostId { get; init; }

    /// <summary>The seats played by people (the rest are bots, including the seats of people who are away).</summary>
    public IEnumerable<int> People => Owners.Select((owner, seat) => (owner, seat))
        .Where(s => s.owner is not null && !Away.Contains(s.seat)).Select(s => s.seat);

    /// <summary>The people the current step waits on: in the opening, those still bidding in this Unveil (whoever has
    /// won a tile from it sits out the rest), and in the rounds, everyone.</summary>
    public IEnumerable<int> InStep => People.Where(s => Match.Phase != MatchPhase.Opening || Match.Bidders.Contains(s));

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
/// on the same match take turns. Notifications (<see cref="MatchNotices"/>) go to <c>push</c> once a change has
/// committed.
/// </summary>
public sealed class MatchStore(HostDatabase database, PushSink? push = null)
{
    /// <summary>The host's mode for a match.</summary>
    public const string Mode = "match";

    /// <summary>The host keeps a clock for every game. A match's own deadlines are per step (<see cref="StepDeadline"/>),
    /// so this is only how long it may stay open: 11 steps of a day each fit well inside it.</summary>
    public static readonly TimeSpan OpenFor = TimeSpan.FromDays(28);

    /// <summary>How long people have to commit to a step before it resolves without them.</summary>
    public static readonly TimeSpan StepDeadline = TimeSpan.FromHours(24);

    /// <summary>Deadlines missed in a row after which a bot takes the seat.</summary>
    public const int MissesBeforeBot = 2;

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

        await SetDeadlineAsync(db, game.Id, now);
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
    /// person's seat. When every person has committed, the step resolves (<see cref="MatchSteps"/>) and is saved as the
    /// last report. Returns what happened and whether the step resolved; null if there is no such match.
    /// </summary>
    public async Task<(Report Report, bool Resolved)?> CommitAsync(long id, string ownerId, DateTimeOffset now, string pending,
        Func<LoadedMatch, int, Report> check)
    {
        ArgumentNullException.ThrowIfNull(check);
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

        if (loaded.Match.Away.Contains(seat))
        {
            return (Report.Fail("A bot plays your seat now."), false);
        }

        var checkedReport = check(loaded.Match, seat);
        if (!checkedReport.Succeeded)
        {
            return (checkedReport, false);
        }

        await db.ExecuteAsync("UPDATE match_players SET pending = $3::jsonb, pending_for = $4 WHERE game_id = $1 AND seat = $2",
            id, seat, pending, StepKey(loaded.Match.Match));
        var waiting = loaded.Match.InStep.Where(s => s != seat && !loaded.Match.Committed.Contains(s)).ToList();
        if (waiting.Count > 0)
        {
            await db.CommitAsync();
            // Only on a first commitment: changing your mind doesn't ping the last one again.
            if (waiting.Count == 1 && !loaded.Match.Committed.Contains(seat) && loaded.Match.Owners[waiting[0]] is { } last)
            {
                Send([MatchNotices.WaitingOnYou(id, last, MatchNotices.StepName(loaded.Match.Match))]);
            }

            return (new Report(true, [.. checkedReport.Lines,
                $"Committed. Waiting for {string.Join(" and ", waiting.Select(s => $"Seat {s + 1}"))}; you can take it back until then."]), false);
        }

        var (report, notices) = await ResolveAsync(db, id, loaded.Seed, loaded.Match, new HashSet<int>(), now, actor: seat);
        if (!report.Succeeded)
        {
            return (report, false); // nothing is saved
        }

        await db.CommitAsync();
        Send(notices);
        return (report, true);
    }

    /// <summary>
    /// Resolves every match whose deadline has passed (the host's GameKeeper calls this every minute, through
    /// <see cref="SpellboundGame.OnTickAsync"/>): whoever hasn't committed holds. Each match in its own transaction, its
    /// row locked, so a commit landing at the same moment waits or wins. Returns the ids of the matches it resolved.
    /// </summary>
    public async Task<IReadOnlyList<long>> HoldOverdueAsync(DateTimeOffset now)
    {
        List<long> due;
        await using (var db = await database.BeginAsync())
        {
            due = await db.QueryAsync("SELECT game_id FROM match_deadlines WHERE due_at <= $1 ORDER BY due_at", r => r.GetInt64(0), now);
        }

        var resolved = new List<long>();
        foreach (var id in due)
        {
            await using var db = await database.BeginAsync();
            if (await LoadAsync(db, id, forUpdate: true) is not { } loaded || loaded.Match.DueAt is not { } dueAt || dueAt > now)
            {
                continue; // resolved by a commit meanwhile, or gone
            }

            if (!loaded.IsRunning)
            {
                await db.ExecuteAsync("DELETE FROM match_deadlines WHERE game_id = $1", id);
                await db.CommitAsync();
                continue;
            }

            var holding = loaded.Match.InStep.Where(s => !loaded.Match.Committed.Contains(s)).ToHashSet();
            var (report, notices) = await ResolveAsync(db, id, loaded.Seed, loaded.Match, holding, now, actor: null);
            if (report.Succeeded)
            {
                await db.CommitAsync();
                Send(notices);
                resolved.Add(id);
            }
        }

        return resolved;
    }

    /// <summary>
    /// The host hands another person's seat to a bot (the same as missing two deadlines): the person can still watch.
    /// Only a seat that hasn't committed this step: someone who has is still playing.
    /// If everyone else has committed, the step resolves now. Changes nothing for anyone but the host.
    /// </summary>
    public async Task<Report> HandToBotAsync(long id, string ownerId, int seat, DateTimeOffset now)
    {
        await using var db = await database.BeginAsync();
        if (await LoadAsync(db, id, forUpdate: true) is not { IsRunning: true } loaded)
        {
            return Report.Fail("This match isn't running.");
        }

        var match = loaded.Match;
        if (match.HostId != ownerId || match.SeatOf(ownerId) is not { } host || match.Away.Contains(host))
        {
            return Report.Fail("Only the match's host can hand a seat to a bot.");
        }

        if (seat == host || !match.People.Contains(seat))
        {
            return Report.Fail($"Seat {seat + 1} isn't a person's seat to hand over.");
        }

        if (match.Committed.Contains(seat))
        {
            return Report.Fail($"Seat {seat + 1} has committed this step: they are still playing.");
        }

        await db.ExecuteAsync(
            "UPDATE match_players SET bot_since = $3, pending = NULL, pending_for = NULL WHERE game_id = $1 AND seat = $2",
            id, seat, now);
        var lines = new List<string> { $"A bot plays seat {seat + 1} from now on." };
        List<PushMessage> notices = [MatchNotices.HandedOver(id, match.Owners[seat]!)];
        if (match.InStep.Where(s => s != seat).All(match.Committed.Contains))
        {
            var (report, resolvedNotices) = await ResolveAsync(db, id, loaded.Seed, match with { Away = new HashSet<int>(match.Away) { seat } },
                new HashSet<int>(), now, actor: host);
            if (!report.Succeeded)
            {
                return report;
            }

            lines.AddRange(report.Lines);
            notices.AddRange(resolvedNotices);
        }
        else
        {
            await SetDeadlineAsync(db, id, now, keepDue: true);
        }

        await db.CommitAsync();
        Send(notices);
        return new Report(true, lines);
    }

    /// <summary>
    /// Resolves the current step in the caller's transaction (the match locked): the people's commitments, holds for
    /// <paramref name="holding"/>, bots for the rest. If it succeeds it is saved as the last report, the commitments
    /// are cleared, misses are counted (a bot takes a seat at <see cref="MissesBeforeBot"/>), and the next step gets its
    /// deadline. In the opening, the tiles that only bots are still bidding on then go too. Nothing is written if it
    /// fails. Returns the notifications to send once the caller has committed.
    /// </summary>
    private static async Task<(Report Report, IReadOnlyList<PushMessage> Notices)> ResolveAsync(DbSession db, long id, int seed,
        LoadedMatch loaded, IReadOnlySet<int> holding, DateTimeOffset now, int? actor)
    {
        var step = MatchNotices.StepName(loaded.Match);
        var nowAway = holding.Where(s => loaded.Missed.GetValueOrDefault(s) + 1 >= MissesBeforeBot).ToHashSet();
        var committed = (await db.QueryAsync(
                "SELECT seat, pending::text FROM match_players WHERE game_id = $1 AND pending_for = $2 AND bot_since IS NULL",
                r => (Seat: r.GetInt32(0), Pending: r.GetString(1)), id, StepKey(loaded.Match)))
            .ToDictionary(p => p.Seat, p => p.Pending);
        var random = StepRandom(seed, loaded.Match);
        var (report, attacked) = MatchSteps.Resolve(loaded.Match, committed, holding, random);
        if (!report.Succeeded)
        {
            return (report, []);
        }

        // An opening tile only bots are still bidding on goes now: no person has anything to commit for it.
        var after = loaded with { Away = loaded.Away.Union(nowAway).ToHashSet() };
        while (after.Match.Phase == MatchPhase.Opening && !after.InStep.Any())
        {
            var (more, _) = MatchSteps.Resolve(after.Match, new Dictionary<int, string>(), new HashSet<int>(), StepRandom(seed, after.Match));
            if (!more.Succeeded)
            {
                break;
            }

            report = new Report(true, [.. report.Lines, .. more.Lines],
                [.. report.Lines.Select((_, i) => report.TagOf(i)), .. more.Lines.Select((_, i) => more.TagOf(i))]);
        }

        await db.SaveGameStateAsync(id, MatchJson.Serialize(new StoredMatch(seed, loaded.Match.Save(), report.Lines, report.Tags)));
        await db.ExecuteAsync(
            """
            UPDATE match_players SET pending = NULL, pending_for = NULL,
                missed = CASE WHEN seat = ANY($2) THEN missed + 1 ELSE 0 END
            WHERE game_id = $1 AND bot_since IS NULL
            """,
            id, holding.ToArray());
        await db.ExecuteAsync(
            "UPDATE match_players SET bot_since = $2 WHERE game_id = $1 AND bot_since IS NULL AND missed >= $3",
            id, now, MissesBeforeBot);
        if (loaded.Match.Phase == MatchPhase.Ended)
        {
            await db.EndGameAsync(id, now);
            await db.ExecuteAsync("DELETE FROM match_deadlines WHERE game_id = $1", id);
        }
        else
        {
            await SetDeadlineAsync(db, id, now);
        }

        return (report, [.. MatchNotices.Resolved(loaded, step, attacked, nowAway, actor)]);
    }

    private void Send(IEnumerable<PushMessage> notices)
    {
        foreach (var notice in notices)
        {
            push?.Send(notice);
        }
    }

    /// <summary>
    /// Gives the current step a deadline <see cref="StepDeadline"/> from now when two or more people play the match
    /// (with one, nobody waits on anyone), or removes it. <paramref name="keepDue"/> keeps a deadline already set.
    /// </summary>
    private static async Task SetDeadlineAsync(DbSession db, long id, DateTimeOffset now, bool keepDue = false)
    {
        var people = await db.ScalarAsync<long>("SELECT count(*) FROM match_players WHERE game_id = $1 AND bot_since IS NULL", id);
        if (people < 2)
        {
            await db.ExecuteAsync("DELETE FROM match_deadlines WHERE game_id = $1", id);
        }
        else if (!keepDue)
        {
            await db.ExecuteAsync(
                "INSERT INTO match_deadlines (game_id, due_at) VALUES ($1, $2) ON CONFLICT (game_id) DO UPDATE SET due_at = $2",
                id, now + StepDeadline);
        }
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
            "UPDATE match_players SET pending = NULL, pending_for = NULL WHERE game_id = $1 AND seat = $2 AND pending_for = $3 AND bot_since IS NULL",
            id, seat, StepKey(loaded.Match.Match));
        await db.CommitAsync();
        return changed > 0;
    }

    /// <summary>Which step of a match a commitment is for: one tile of an opening auction, or a round.</summary>
    public static string StepKey(Match match) =>
        match.Phase == MatchPhase.Opening ? $"opening-{match.OpeningUnveil}-{match.AuctionTile}" : $"round-{match.CurrentRound}";

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
        (_, MatchPhase.Opening) => $"Opening auction {match.OpeningUnveil} of {match.Rules.OpeningUnveils}, tile {match.AuctionTile} of {match.Seats.Count}",
        _ => $"Round {match.CurrentRound} of {match.Rules.Rounds}",
    };

    /// <summary>The randomness for a match's next step, from its seed and where it is: each opening auction and each
    /// round gets its own, so a step never depends on how often the match was saved before it. Not HashCode, which
    /// changes from one run of the server to the next.</summary>
    public static IRandomSource StepRandom(int seed, Match match) => new SeededRandom(unchecked(match.Phase switch
    {
        MatchPhase.Opening => seed + (10 * match.OpeningUnveil) + match.AuctionTile,
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
        var away = new HashSet<int>();
        var missed = new Dictionary<int, int>();
        foreach (var (seat, owner, pendingFor, misses, isAway) in await db.QueryAsync(
                     "SELECT seat, owner_id, pending_for, missed, bot_since IS NOT NULL FROM match_players WHERE game_id = $1",
                     r => (r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetInt32(3), r.GetBoolean(4)), id))
        {
            owners[seat] = owner;
            if (isAway)
            {
                away.Add(seat);
            }
            else if (pendingFor == step)
            {
                committed.Add(seat);
            }

            if (misses > 0)
            {
                missed[seat] = misses;
            }
        }

        var dueAt = await db.QueryAsync("SELECT due_at FROM match_deadlines WHERE game_id = $1", r => r.GetFieldValue<DateTimeOffset>(0), id);
        var hostId = await db.QueryAsync("SELECT host_id FROM match_lobbies WHERE game_id = $1", r => r.GetString(0), id);
        return (stored.Seed, new LoadedMatch(id, owners, match)
        {
            LastReport = stored.LastReport ?? [], LastReportTags = stored.LastReportTags, Committed = committed,
            Away = away, Missed = missed, DueAt = dueAt.Count > 0 ? dueAt[0] : null, HostId = hostId.FirstOrDefault(),
        }, game.IsRunning);
    }
}
