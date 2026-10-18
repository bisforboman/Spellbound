using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web;

/// <summary>Spellbound's answers to the hosting code's questions (see Hosting/GameHooks.cs).</summary>
public sealed class SpellboundHost(GameRules rules) : IRoundLifecycle, IPlayerSummaries, IAccountLifecycle
{
    /// <summary>The Realm's mode name: each Realm round is a game of this mode, and the server always runs one.</summary>
    public const string RealmMode = "realm";

    public string AlwaysOnMode => RealmMode;

    /// <summary>The next Realm round, under the rules configured now.</summary>
    public NewGame NextAlwaysOn() => new(RealmMode, rules.RoundLength, DomainJson.Serialize(rules));

    /// <summary>Records the round's hall of fame, leaving banned players out.</summary>
    public async Task OnGameEndedAsync(DbSession db, HostedGame ended, BanList bans, CancellationToken cancellationToken)
    {
        var mages = (await db.LoadGameMagesAsync(ended.Id)).Where(m => !bans.IsBanned(m.OwnerId)).ToList();
        var fame = HallOfFame.Record(ended.Round, mages, rules);
        await db.ExecuteAsync(
            "INSERT INTO hall_of_fame (game_id, data) VALUES ($1, $2)", ended.Id, new Jsonb(DomainJson.Serialize(fame)));
    }

    /// <summary>
    /// Deletes the player's mage, news and logged actions from every round, so a deleted account leaves no mage in
    /// Rivals to farm. Other players' logged attacks on them stay, without their account id. Halls of fame keep their
    /// names: they're a record of finished rounds.
    /// </summary>
    public async Task OnAccountDeletedAsync(DbSession db, string ownerId)
    {
        await db.ExecuteAsync("DELETE FROM mages WHERE owner_id = $1", ownerId);
        await db.ExecuteAsync("DELETE FROM news WHERE owner_id = $1", ownerId);
        await db.ExecuteAsync("DELETE FROM actions WHERE owner_id = $1", ownerId); // their own actions go
        await db.ExecuteAsync("UPDATE actions SET target_owner_id = NULL WHERE target_owner_id = $1", ownerId); // others' stay
    }

    /// <summary>The player's mages (as stored, every round), news and logged actions, by round number.</summary>
    public async Task<object> ExportAsync(DbSession db, string ownerId) => new
    {
        mages = await db.QueryAsync(
            "SELECT g.number, m.data::text FROM mages m JOIN games g ON g.id = m.game_id WHERE m.owner_id = $1 ORDER BY g.id",
            r => new { round = r.GetInt32(0), mage = Json(r.GetString(1)) },
            ownerId),
        news = await db.QueryAsync(
            "SELECT g.number, n.entries::text FROM news n JOIN games g ON g.id = n.game_id WHERE n.owner_id = $1 ORDER BY g.id",
            r => new { round = r.GetInt32(0), entries = Json(r.GetString(1)) },
            ownerId),
        actions = await db.QueryAsync(
            """
            SELECT g.number, a.at, a.action, a.inputs::text, a.result::text, a.state::text
            FROM actions a JOIN games g ON g.id = a.game_id WHERE a.owner_id = $1 ORDER BY a.id
            """,
            r => new
            {
                round = r.GetInt32(0),
                at = r.GetFieldValue<DateTimeOffset>(1),
                action = r.GetString(2),
                inputs = Json(r.GetString(3)),
                result = Json(r.GetString(4)),
                state = Json(r.GetString(5)),
            },
            ownerId),
    };

    public async Task<IReadOnlyDictionary<string, PlayerSummary>> GetAsync(DbSession db, HostedGame game) =>
        (await db.LoadGameMagesAsync(game.Id)).ToDictionary(
            m => m.OwnerId,
            m => new PlayerSummary(
                m.Name,
                $"{m.School} · {m.Land.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} acres",
                m.Land));

    private static System.Text.Json.JsonElement Json(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
}
