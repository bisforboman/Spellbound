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
