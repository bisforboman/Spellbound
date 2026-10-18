using Spellbound.Domain;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Data;

/// <summary>Spellbound's tables in the host's database (Data/Sql), migrated after the host's.</summary>
public sealed class SpellboundSchema : IGameSchema
{
    public SchemaModule Schema { get; } =
        SchemaModule.FromResources("spellbound", typeof(SpellboundSchema).Assembly, "Spellbound.Web.Data.Sql.");
}

/// <summary>
/// Spellbound's rows. A mage is one row per player per game (each Realm round is a game): the game and owner id are the
/// key (so a second mage can't be stored, and a new round starts everyone over), and the mage itself is a jsonb snapshot (<see cref="DomainJson"/>).
/// News and halls of fame work the same way.
/// </summary>
public static class GameData
{
    /// <summary>Every mage of a game, read-only.</summary>
    public static async Task<IReadOnlyList<Mage>> LoadGameMagesAsync(this DbSession db, long gameId) =>
        await db.QueryAsync("SELECT data FROM mages WHERE game_id = $1", r => DomainJson.Deserialize<Mage>(r.GetString(0)), gameId);

    /// <summary>The player's mage in a game. With <paramref name="forUpdate"/>, locks its row until the transaction ends.</summary>
    public static Task<Mage?> LoadMageAsync(this DbSession db, long gameId, string ownerId, bool forUpdate = false) =>
        db.SingleAsync(
            "SELECT data FROM mages WHERE game_id = $1 AND owner_id = $2" + (forUpdate ? " FOR UPDATE" : ""),
            r => DomainJson.Deserialize<Mage>(r.GetString(0)),
            gameId, ownerId);

    /// <summary>A mage in a game by its public id (never the owner id, which is an account key).</summary>
    public static Task<Mage?> LoadMageByIdAsync(this DbSession db, long gameId, Guid mageId) =>
        db.SingleAsync(
            "SELECT data FROM mages WHERE game_id = $1 AND mage_id = $2",
            r => DomainJson.Deserialize<Mage>(r.GetString(0)),
            gameId, mageId);

    /// <summary>
    /// Locks the player's mage and a target (by public id) in one statement, in owner order, so two attacks between
    /// the same mages lock in the same order and can't deadlock. Returns the rows found (the target may be missing).
    /// </summary>
    public static Task<List<Mage>> LockMagesAsync(this DbSession db, long gameId, string ownerId, Guid targetId) =>
        db.QueryAsync(
            "SELECT data FROM mages WHERE game_id = $1 AND (owner_id = $2 OR mage_id = $3) ORDER BY owner_id FOR UPDATE",
            r => DomainJson.Deserialize<Mage>(r.GetString(0)),
            gameId, ownerId, targetId);

    /// <summary>Stores a new mage. False if the player already has one in this game.</summary>
    public static async Task<bool> InsertMageAsync(this DbSession db, long gameId, Mage mage) =>
        await db.ExecuteAsync(
            "INSERT INTO mages (game_id, owner_id, mage_id, data) VALUES ($1, $2, $3, $4) ON CONFLICT (game_id, owner_id) DO NOTHING",
            gameId, mage.OwnerId, mage.Id, new Jsonb(DomainJson.Serialize(mage))) == 1;

    public static Task SaveMageAsync(this DbSession db, long gameId, Mage mage) =>
        db.ExecuteAsync(
            "UPDATE mages SET data = $3 WHERE game_id = $1 AND owner_id = $2",
            gameId, mage.OwnerId, new Jsonb(DomainJson.Serialize(mage)));

    public static async Task<NewsLog> LoadNewsAsync(this DbSession db, long gameId, string ownerId) =>
        await db.SingleAsync(
            "SELECT entries, unseen FROM news WHERE game_id = $1 AND owner_id = $2",
            r => new NewsLog { Entries = DomainJson.Deserialize<List<NewsEntry>>(r.GetString(0)), Unseen = r.GetInt32(1) },
            gameId, ownerId)
        ?? new NewsLog();

    /// <summary>
    /// Adds an entry to the player's news, creating the row if needed. Locks the row first, so news written by two
    /// actions at once is never lost. Call it for players in owner order when one action writes several players' news.
    /// </summary>
    public static async Task AddNewsAsync(this DbSession db, long gameId, string ownerId, NewsEntry entry, int keep)
    {
        await db.ExecuteAsync(
            "INSERT INTO news (game_id, owner_id, entries, unseen) VALUES ($1, $2, '[]', 0) ON CONFLICT (game_id, owner_id) DO NOTHING",
            gameId, ownerId);
        var log = (await db.SingleAsync(
            "SELECT entries, unseen FROM news WHERE game_id = $1 AND owner_id = $2 FOR UPDATE",
            r => new NewsLog { Entries = DomainJson.Deserialize<List<NewsEntry>>(r.GetString(0)), Unseen = r.GetInt32(1) },
            gameId, ownerId))!;
        log.Add(entry, keep);
        await db.ExecuteAsync(
            "UPDATE news SET entries = $3, unseen = $4 WHERE game_id = $1 AND owner_id = $2",
            gameId, ownerId, new Jsonb(DomainJson.Serialize(log.Entries)), log.Unseen);
    }
}
