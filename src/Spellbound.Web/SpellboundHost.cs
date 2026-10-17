using Raven.Client.Documents.Session;
using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web;

/// <summary>Spellbound's answers to the hosting code's questions (see Hosting/GameHooks.cs).</summary>
public sealed class SpellboundHost(GameRules rules) : IRoundLifecycle, IPlayerSummaries, IAccountLifecycle
{
    public TimeSpan RoundLength => rules.RoundLength;

    /// <summary>Records the round's hall of fame, leaving banned players out.</summary>
    public async Task OnRoundEndedAsync(IAsyncDocumentSession session, Round ended, BanList bans, CancellationToken cancellationToken)
    {
        var mages = (await GameKeys.LoadRoundMagesAsync(session, ended.Number)).Where(m => !bans.IsBanned(m.OwnerId)).ToList();
        var fame = HallOfFame.Record(ended, mages, rules);
        await session.StoreAsync(fame, changeVector: string.Empty, GameKeys.HallOfFameKey(ended.Number), cancellationToken);
    }

    /// <summary>
    /// Deletes the player's mage and news from every round, so a deleted account leaves no mage in Rivals to farm.
    /// Halls of fame keep their names: they're a record of finished rounds.
    /// </summary>
    public async Task OnAccountDeletedAsync(IAsyncDocumentSession session, string ownerId)
    {
        var current = (await session.LoadAsync<CurrentRound>(HostStore.CurrentRoundKey))?.Round.Number ?? 0;
        for (var round = 1; round <= current; round++)
        {
            session.Delete(GameKeys.MageKey(round, ownerId));
            session.Delete(GameKeys.NewsKey(round, ownerId));
        }
    }

    public async Task<IReadOnlyDictionary<string, PlayerSummary>> GetAsync(IAsyncDocumentSession session, Round round) =>
        (await GameKeys.LoadRoundMagesAsync(session, round.Number)).ToDictionary(
            m => m.OwnerId,
            m => new PlayerSummary(
                m.Name,
                $"{m.School} · {m.Land.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} acres",
                m.Land));
}
