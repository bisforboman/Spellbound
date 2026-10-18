namespace Spellbound.Domain;

/// <summary>A mage's numbers at one moment, for a <see cref="Ledger"/>.</summary>
public sealed record MageSnapshot(int Land, int Nodes, int Towns, int Gold, int Mana, int Turns, int Units, int UpkeepPerTurn);

/// <summary>
/// The projected result of an action, shown before the player confirms (docs/mageborn-design.md, "Verbs"): the action
/// run on a copy of the mage, with its numbers before and after and the report it would give. The mage itself is
/// never changed. A failed preview's report says why, and its numbers don't move.
/// </summary>
public sealed record Ledger(ActionReport Report, MageSnapshot Before, MageSnapshot After);
