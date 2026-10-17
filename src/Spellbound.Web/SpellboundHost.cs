using Raven.Client.Documents.Session;
using Spellbound.Domain;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

namespace Spellbound.Web;

/// <summary>Spellbound's answers to the hosting code's questions (see Hosting/GameHooks.cs).</summary>
public sealed class SpellboundHost(GameRules rules) : IRoundLifecycle, IPlayerSummaries
{
    public TimeSpan RoundLength => rules.RoundLength;

    /// <summary>Records the round's hall of fame, leaving banned players out.</summary>
    public async Task OnRoundEndedAsync(IAsyncDocumentSession session, Round ended, BanList bans, CancellationToken cancellationToken)
    {
        var mages = (await GameKeys.LoadRoundMagesAsync(session, ended.Number)).Where(m => !bans.IsBanned(m.OwnerId)).ToList();
        var fame = HallOfFame.Record(ended, mages, rules);
        await session.StoreAsync(fame, changeVector: string.Empty, GameKeys.HallOfFameKey(ended.Number), cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, PlayerSummary>> GetAsync(IAsyncDocumentSession session, Round round) =>
        (await GameKeys.LoadRoundMagesAsync(session, round.Number)).ToDictionary(
            m => m.OwnerId,
            m => new PlayerSummary(
                m.Name,
                $"{m.School} · {m.Land.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} acres",
                m.Land));
}
