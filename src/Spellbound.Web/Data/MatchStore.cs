using Spellbound.Domain;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Data;

/// <summary>A match as it is loaded: the game it is, who sits where (null for a bot), and the match itself.</summary>
public sealed record LoadedMatch(long Id, IReadOnlyList<string?> Owners, Match Match);

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
        ArgumentNullException.ThrowIfNull(owners);
        var chosen = seed ?? Random.Shared.Next();
        var match = Match.Start(owners.Count, rules, new SeededRandom(chosen));

        await using var db = await database.BeginAsync();
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

        await db.CommitAsync();
        return game.Id;
    }

    /// <summary>A match by id, or null if there is none.</summary>
    public async Task<LoadedMatch?> LoadAsync(long id)
    {
        await using var db = await database.BeginAsync();
        return await LoadAsync(db, id, forUpdate: false) is { } loaded ? loaded.Match : null;
    }

    /// <summary>
    /// Runs one action on a match with its row locked: <paramref name="action"/> gets the match and the randomness
    /// for this step, and the match is saved only if the action succeeds. Null if there is no such match.
    /// </summary>
    public async Task<Report?> ApplyAsync(long id, Func<LoadedMatch, IRandomSource, Report> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var db = await database.BeginAsync();
        if (await LoadAsync(db, id, forUpdate: true) is not { } loaded)
        {
            return null;
        }

        var report = action(loaded.Match, StepRandom(loaded.Seed, loaded.Match.Match));
        if (report.Succeeded)
        {
            await db.SaveGameStateAsync(id, MatchJson.Serialize(new StoredMatch(loaded.Seed, loaded.Match.Match.Save())));
            await db.CommitAsync();
        }

        return report;
    }

    /// <summary>The randomness for a match's next step, from its seed and where it is: each opening auction and each
    /// round gets its own, so a step never depends on how often the match was saved before it. Not HashCode, which
    /// changes from one run of the server to the next.</summary>
    public static IRandomSource StepRandom(int seed, Match match) => new SeededRandom(unchecked(match.Phase switch
    {
        MatchPhase.Opening => seed + match.OpeningUnveil,
        _ => seed + 100 + match.CurrentRound,
    }));

    private static async Task<(int Seed, LoadedMatch Match)?> LoadAsync(DbSession db, long id, bool forUpdate)
    {
        if (await db.LoadGameAsync(id, forUpdate) is not { Mode: Mode } game || await db.LoadGameStateAsync(id) is not { } json)
        {
            return null;
        }

        var stored = MatchJson.Deserialize<StoredMatch>(json);
        var owners = new string?[stored.Match.Seats.Count];
        foreach (var (seat, owner) in await db.QueryAsync(
                     "SELECT seat, owner_id FROM match_players WHERE game_id = $1", r => (r.GetInt32(0), r.GetString(1)), id))
        {
            owners[seat] = owner;
        }

        var match = Match.Load(stored.Match, MatchJson.Deserialize<GameRules>(game.RulesJson));
        return (stored.Seed, new LoadedMatch(id, owners, match));
    }
}
