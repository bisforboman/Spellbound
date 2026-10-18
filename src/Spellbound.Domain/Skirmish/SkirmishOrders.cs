namespace Spellbound.Domain.Skirmish;

/// <summary>Something a player orders for the next round. Orders play out together, in a fixed order, when the round plays.</summary>
public abstract record SkirmishOrder
{
    /// <summary>How many of the round's actions it uses.</summary>
    public abstract int Cost(SkirmishRules rules);
}

/// <summary>Take a free region next to one you hold, and draw a discovery card.</summary>
public sealed record ClaimOrder(int RegionId) : SkirmishOrder
{
    public override int Cost(SkirmishRules rules) => rules.ClaimCost;
}

/// <summary>Build nodes or towns in free slots of one of your regions.</summary>
public sealed record BuildOrder(int RegionId, BuildingType Type, int Count) : SkirmishOrder
{
    public override int Cost(SkirmishRules rules) => rules.BuildCost;
}

/// <summary>Recruit units (the Realm's units).</summary>
public sealed record RecruitOrder(string UnitId, int Count) : SkirmishOrder
{
    public override int Cost(SkirmishRules rules) => rules.RecruitCost;
}

/// <summary>Attack a region another mage holds, next to one of yours: a win takes it with its buildings.</summary>
public sealed record AttackOrder(int RegionId) : SkirmishOrder
{
    public override int Cost(SkirmishRules rules) => rules.AttackCost;
}

/// <summary>Cast one of your school's self-spells, or the ward (see <see cref="SkirmishSpells"/>).</summary>
public sealed record CastOrder(string SpellId) : SkirmishOrder
{
    public override int Cost(SkirmishRules rules) => rules.CastCost;
}

/// <summary>Cast your school's hex at another mage.</summary>
public sealed record HexOrder(string HexId, int TargetSeat) : SkirmishOrder
{
    public override int Cost(SkirmishRules rules) => rules.CastCost;
}

/// <summary>A public goal: the first mage(s) to reach it in a round score its points.</summary>
public enum SkirmishObjective
{
    Regions,
    Buildings,
    Battles,
    Nodes,
    Units,
    TerrainSet,
}

/// <summary>A drawn objective and who reached it (empty while it's still open).</summary>
public sealed record ObjectiveState(SkirmishObjective Objective, IReadOnlyList<int> ReachedBy);

/// <summary>A self-spell running on a seat, and how many more rounds it lasts (counting the current one).</summary>
public sealed record SkirmishEffect(string SpellId, int RoundsLeft);

/// <summary>A discovery card, drawn when claiming a region.</summary>
public enum DiscoveryCard
{
    /// <summary>Nothing: just land.</summary>
    Card1,

    /// <summary>Gold.</summary>
    Card2,

    /// <summary>Mana.</summary>
    Card3,

    /// <summary>Points.</summary>
    Card4,

    /// <summary>Part of the army is lost.</summary>
    Card5,
}

public static class DiscoveryCardExtensions
{
    /// <summary>The name players see; a placeholder (docs/game-design.md, "Names to decide").</summary>
    public static string DisplayName(this DiscoveryCard card) => $"Card {(int)card + 1}";
}
