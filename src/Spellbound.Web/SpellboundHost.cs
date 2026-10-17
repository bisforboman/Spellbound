using Microsoft.EntityFrameworkCore;
using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web;

/// <summary>Spellbound's answers to the hosting code's questions (see Hosting/GameHooks.cs).</summary>
public sealed class SpellboundHost(GameRules rules) : IRoundLifecycle, IPlayerSummaries, IAccountLifecycle
{
    public TimeSpan RoundLength => rules.RoundLength;

    /// <summary>Records the round's hall of fame, leaving banned players out.</summary>
    public async Task OnRoundEndedAsync(HostDb db, Round ended, BanList bans, CancellationToken cancellationToken)
    {
        var mages = (await db.LoadRoundMagesAsync(ended.Number, cancellationToken)).Where(m => !bans.IsBanned(m.OwnerId)).ToList();
        var fame = HallOfFame.Record(ended, mages, rules);
        db.HallsOfFame().Add(new HallOfFameRecord { Round = ended.Number, Data = DomainJson.Serialize(fame) });
    }

    /// <summary>
    /// Deletes the player's mage and news from every round, so a deleted account leaves no mage in Rivals to farm.
    /// Halls of fame keep their names: they're a record of finished rounds.
    /// </summary>
    public async Task OnAccountDeletedAsync(HostDb db, string ownerId)
    {
        await db.Mages().Where(m => m.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.News().Where(n => n.OwnerId == ownerId).ExecuteDeleteAsync();
    }

    public async Task<IReadOnlyDictionary<string, PlayerSummary>> GetAsync(HostDb db, Round round) =>
        (await db.LoadRoundMagesAsync(round.Number)).ToDictionary(
            m => m.OwnerId,
            m => new PlayerSummary(
                m.Name,
                $"{m.School} · {m.Land.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} acres",
                m.Land));
}
