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
// Names are placeholders until they're decided (docs/game-design.md, "Names to decide"); ids are stored, so they're neutral.
public static class UnitBook
{
    public static IReadOnlyList<UnitDefinition> All { get; } =
    [
        new("unit-1", "Unit 1", 1, 3, 60, 1, "Cheap, and better at defense."),
        new("unit-2", "Unit 2", 3, 3, 150, 2, "Even attack and defense."),
        new("unit-3", "Unit 3", 6, 2, 250, 3, "Strong attack, weak defense."),
    ];

    public static UnitDefinition? Find(string unitId) =>
        All.FirstOrDefault(u => string.Equals(u.Id, unitId, StringComparison.OrdinalIgnoreCase));
}
