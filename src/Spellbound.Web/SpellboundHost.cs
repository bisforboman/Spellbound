using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web;

/// <summary>Spellbound's answers to the hosting code's questions (see Hosting/GameHooks.cs).</summary>
public sealed class SpellboundHost(GameRules rules) : IRoundLifecycle, IPlayerSummaries, IAccountLifecycle
{
    public TimeSpan RoundLength => rules.RoundLength;

    /// <summary>Records the round's hall of fame, leaving banned players out.</summary>
    public async Task OnRoundEndedAsync(DbSession db, Round ended, BanList bans, CancellationToken cancellationToken)
    {
        var mages = (await db.LoadRoundMagesAsync(ended.Number)).Where(m => !bans.IsBanned(m.OwnerId)).ToList();
        var fame = HallOfFame.Record(ended, mages, rules);
        await db.ExecuteAsync(
            "INSERT INTO hall_of_fame (round, data) VALUES ($1, $2)", ended.Number, new Jsonb(DomainJson.Serialize(fame)));
    }

    /// <summary>
    /// Deletes the player's mage and news from every round, so a deleted account leaves no mage in Rivals to farm.
    /// Halls of fame keep their names: they're a record of finished rounds.
    /// </summary>
    public async Task OnAccountDeletedAsync(DbSession db, string ownerId)
    {
        await db.ExecuteAsync("DELETE FROM mages WHERE owner_id = $1", ownerId);
        await db.ExecuteAsync("DELETE FROM news WHERE owner_id = $1", ownerId);
    }

    public async Task<IReadOnlyDictionary<string, PlayerSummary>> GetAsync(DbSession db, Round round) =>
        (await db.LoadRoundMagesAsync(round.Number)).ToDictionary(
            m => m.OwnerId,
            m => new PlayerSummary(
                m.Name,
                $"{m.School} · {m.Land.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} acres",
                m.Land));
}
