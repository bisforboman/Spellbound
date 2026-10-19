using Spellbound.Domain;
using Spellbound.Web.Hosting;

namespace Spellbound.Web;

/// <summary>
/// The game's place until M1 builds the new one (docs/game-design.md, docs/roadmap.md): it answers the host's questions
/// (Hosting/GameHooks.cs) so the platform runs, keeps a round going, and stores nothing about players. Its schema is the
/// game's SQL scripts so far, the last of which drops the old game's tables.
/// </summary>
public sealed class PlaceholderGame(HostDatabase database)
    : IRoundLifecycle, IAccountLifecycle, IPlayerSummaries, IGameSchema
{
    /// <summary>The mode of the round the server keeps running.</summary>
    public const string Mode = "placeholder";

    /// <summary>How long each round lasts until the new game sets its own.</summary>
    public static readonly TimeSpan RoundLength = TimeSpan.FromDays(28);

    /// <summary>The game's tables: its SQL scripts so far (Data/Sql), the last of which drops the old game's.</summary>
    public static SchemaModule GameSchema { get; } =
        SchemaModule.FromResources("spellbound", typeof(PlaceholderGame).Assembly, "Spellbound.Web.Data.Sql.");

    public SchemaModule Schema => GameSchema;

    public string AlwaysOnMode => Mode;

    public NewGame NextAlwaysOn() => new(Mode, RoundLength, "{}");

    public Task OnGameEndedAsync(DbSession db, HostedGame ended, BanList bans, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>Nothing to do: the player's seats in matches (match_players) go with the account row, and those seats
    /// play on as bots.</summary>
    public Task OnAccountDeletedAsync(DbSession db, string ownerId) => Task.CompletedTask;

    /// <summary>The matches the player sits in, and their seat in each.</summary>
    public async Task<object> ExportAsync(DbSession db, string ownerId) => new
    {
        matches = await db.QueryAsync(
            "SELECT game_id, seat FROM match_players WHERE owner_id = $1 ORDER BY game_id",
            r => new { match = r.GetInt64(0), seat = r.GetInt32(1) + 1 },
            ownerId),
    };

    public Task<IReadOnlyDictionary<string, PlayerSummary>> GetAsync(DbSession db, HostedGame game) =>
        Task.FromResult<IReadOnlyDictionary<string, PlayerSummary>>(new Dictionary<string, PlayerSummary>());

    /// <summary>The round being run, or null before the first one has started.</summary>
    public async Task<Round?> GetRoundAsync()
    {
        await using var db = await database.BeginAsync();
        return (await db.LoadRunningGameAsync(Mode))?.Round;
    }
}
