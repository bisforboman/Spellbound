namespace Spellbound.Domain;

/// <summary>A unit type any mage can recruit. Upkeep is paid in gold for every turn the owner spends.</summary>
public sealed record UnitDefinition(
    string Id,
    string Name,
    int Attack,
    int Defense,
    int GoldCost,
    int GoldUpkeep,
    string Description);

/// <summary>All unit types in the game. Milestone 2: three units shared by every school.</summary>
public static class UnitBook
{
    public static IReadOnlyList<UnitDefinition> All { get; } =
    [
        new("militia", "Militia", 1, 3, 60, 1, "Cheap levies who hold the walls."),
        new("soldiers", "Soldiers", 3, 3, 150, 2, "Trained troops, good at both."),
        new("knights", "Knights", 6, 2, 250, 3, "Heavy riders built for the charge."),
    ];

    public static UnitDefinition? Find(string unitId) =>
        All.FirstOrDefault(u => string.Equals(u.Id, unitId, StringComparison.OrdinalIgnoreCase));
}
