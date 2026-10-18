namespace Spellbound.Domain;

/// <summary>
/// What exploring depends on beyond the mage: the round's phase and the median mage's land (docs/game-design.md,
/// Actions). The caller works them out for the same moment it acts at, so a preview, the action and a replay agree.
/// </summary>
public sealed record ExploreConditions(SeasonPhase Phase, int MedianLand)
{
    public static ExploreConditions At(Round round, DateTimeOffset now, GameRules rules, IEnumerable<Mage> mages) =>
        new(Season.PhaseAt(round, now, rules), Rankings.MedianLand(mages));
}
