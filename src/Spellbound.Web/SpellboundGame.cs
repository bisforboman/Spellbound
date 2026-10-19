using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web;

/// <summary>
/// Spellbound behind the host's hooks (Hosting/GameHooks.cs): its games are matches (<see cref="MatchStore"/>), started on
/// demand. It answers the host's questions about them; its schema is the game's SQL scripts (Data/Sql).
/// </summary>
public sealed class SpellboundGame : IGameLifecycle, IAccountLifecycle, IPlayerSummaries, IGameSchema
{
    /// <summary>The game's tables: its SQL scripts (Data/Sql), which also drop the old game's.</summary>
    public static SchemaModule GameSchema { get; } =
        SchemaModule.FromResources("spellbound", typeof(SpellboundGame).Assembly, "Spellbound.Web.Data.Sql.");

    public SchemaModule Schema => GameSchema;

    /// <summary>Nothing to wrap up: an ended match keeps its state as it stood, and its game row says it has ended.</summary>
    public Task OnGameEndedAsync(DbSession db, HostedGame ended, BanList bans, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task<string> DescribeAsync(DbSession db, HostedGame game) =>
        await MatchStore.LoadAsync(db, game.Id, forUpdate: false) is { } loaded
            ? $"{MatchStore.Describe(loaded.Match.Match, game.IsRunning)}, {loaded.Match.Owners.Count} seats"
            : game.Mode;

    /// <summary>Nothing to do: the player's seats (match_players) go with the account row, and play on as bots.</summary>
    public Task OnAccountDeletedAsync(DbSession db, string ownerId) => Task.CompletedTask;

    /// <summary>The matches the player sits in, and their seat in each.</summary>
    public async Task<object> ExportAsync(DbSession db, string ownerId) => new
    {
        matches = await db.QueryAsync(
            "SELECT game_id, seat FROM match_players WHERE owner_id = $1 ORDER BY game_id",
            r => new { match = r.GetInt64(0), seat = r.GetInt32(1) + 1 },
            ownerId),
    };

    /// <summary>The people in a match: their seat, their points and land so far.</summary>
    public async Task<IReadOnlyDictionary<string, PlayerSummary>> GetAsync(DbSession db, HostedGame game)
    {
        if (await MatchStore.LoadAsync(db, game.Id, forUpdate: false) is not { } loaded)
        {
            return new Dictionary<string, PlayerSummary>();
        }

        var (owners, match) = (loaded.Match.Owners, loaded.Match.Match);
        return owners.Select((owner, seat) => (owner, seat)).Where(o => o.owner is not null).ToDictionary(
            o => o.owner!,
            o => new PlayerSummary(
                $"Seat {o.seat + 1} in game {game.Id}",
                $"{match.Seats[o.seat].Score} points, {match.Seats[o.seat].Tiles.Count} tiles",
                match.Seats[o.seat].Score));
    }
}
