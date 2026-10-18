using Spellbound.Web.Hosting;

namespace Spellbound.Web;

/// <summary>A player's Skirmish record: games finished and games won.</summary>
public sealed record SkirmishRecord(int Played, int Won);

/// <summary>A row of the Skirmish leaderboard, under the name the player used in their latest game.</summary>
public sealed record SkirmishLeader(string OwnerId, string Name, int Played, int Won);

/// <summary>
/// The win record and the leaderboard, read from the results <see cref="SkirmishService"/> writes when a game ends.
/// Banned players are left off the leaderboard as it's read, so a ban after a game still counts.
/// </summary>
public sealed class SkirmishRecordService(HostDatabase database)
{
    public async Task<SkirmishRecord> GetAsync(string ownerId)
    {
        await using var db = await database.BeginAsync();
        return (await db.QueryAsync(
            "SELECT count(*)::int, count(*) FILTER (WHERE won)::int FROM skirmish_results WHERE owner_id = $1",
            r => new SkirmishRecord(r.GetInt32(0), r.GetInt32(1)),
            ownerId)).Single();
    }

    /// <summary>The players with the most wins, then the fewest games played for them, then by name.</summary>
    public async Task<IReadOnlyList<SkirmishLeader>> GetLeadersAsync(int limit = 50)
    {
        await using var db = await database.BeginAsync();
        var bans = await db.LoadBansAsync();
        var leaders = await db.QueryAsync(
            """
            SELECT owner_id,
                   (array_agg(name ORDER BY ended_at DESC, game_id DESC))[1],
                   count(*)::int,
                   count(*) FILTER (WHERE won)::int
            FROM skirmish_results GROUP BY owner_id
            """,
            r => new SkirmishLeader(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3)));
        return
        [
            .. leaders.Where(l => !bans.IsBanned(l.OwnerId))
                .OrderByDescending(l => l.Won).ThenBy(l => l.Played).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit),
        ];
    }
}
