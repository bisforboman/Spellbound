namespace Spellbound.Domain;

/// <summary>An upgrade built on a tile (docs/game-design.md, Upgrades). It belongs to the tile: whoever holds the tile
/// has its effect, and it passes with the tile when the tile is taken.</summary>
public enum Upgrade
{
    /// <summary>The tile yields more mana.</summary>
    Shrine,

    /// <summary>The tile defends with more power, even with no units on it.</summary>
    Tower,

    /// <summary>Spells its holder aims at it or next to it cost less.</summary>
    Sanctum,
}

/// <summary>Upgrades: built with the Build stance, at most one per tile and a few per realm, each worth a point at the
/// end.</summary>
public sealed partial class Match
{
    private readonly Dictionary<Hex, Upgrade> upgrades = [];

    /// <summary>The upgrade on a tile, if any.</summary>
    public Upgrade? UpgradeOn(Hex hex) => upgrades.TryGetValue(hex, out var u) ? u : null;

    /// <summary>The upgrades on a seat's tiles (built or taken).</summary>
    public int UpgradesOf(Seat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        return seat.Tiles.Count(upgrades.ContainsKey);
    }

    private string? BuildProblem(Orders orders, Hex target, List<string> lines)
    {
        var seat = seats[orders.Seat];
        if (!seat.Tiles.Contains(target))
        {
            return $"Upgrades can only be built on your own tiles; {target} isn't yours.";
        }

        if (UpgradeOn(target) is { } there)
        {
            return $"{target} already has a {there}.";
        }

        if (UpgradesOf(seat) >= Rules.UpgradeCap)
        {
            return $"You already hold {Rules.UpgradeCap} upgrades, the most a realm can have.";
        }

        if (Rules.UpgradeCost > seat.Mana)
        {
            return $"An upgrade costs {Rules.UpgradeCost} mana; you have {seat.Mana}.";
        }

        lines.Add($"Build a {orders.Upgrade} on {Describe(target)} for {Rules.UpgradeCost} mana, leaving "
                  + $"{seat.Mana - Rules.UpgradeCost}: {UpgradeEffect(orders.Upgrade)}. It is worth {Plural(Rules.PointsPerUpgrade, "point")} "
                  + "at the end to whoever holds the tile.");
        return null;
    }

    /// <summary>What an upgrade does, in words, with this match's numbers.</summary>
    public string UpgradeEffect(Upgrade upgrade) => upgrade switch
    {
        Upgrade.Shrine => $"the tile yields {Rules.ShrineYield} more mana",
        Upgrade.Tower => $"the tile defends with {Rules.TowerPower} more power, even with no units on it",
        Upgrade.Sanctum => $"your spells aimed at it or a tile next to it cost {Rules.SanctumDiscount} less",
        _ => "nothing",
    };

    private void ResolveBuilds(IEnumerable<Orders> orders, ReportLines lines)
    {
        foreach (var order in orders.Where(o => o.Stance == Stance.Build && o.Target is not null))
        {
            var hex = order.Target!.Value;
            seats[order.Seat].Mana -= Rules.UpgradeCost;
            upgrades[hex] = order.Upgrade;
            lines.Add($"Seat {order.Seat + 1} builds a {order.Upgrade} on {hex}.");
        }
    }

    /// <summary>The defence a Tower adds to its tile, for whoever holds it.</summary>
    private int Tower(Hex hex) => UpgradeOn(hex) == Upgrade.Tower && OwnerOf(hex) is not null ? Rules.TowerPower : 0;

    /// <summary>The discount a seat's Sanctum gives a spell aimed at a tile: at the Sanctum or next to it.</summary>
    private int SanctumDiscount(int seat, Hex target) =>
        seats[seat].Tiles.Any(h => UpgradeOn(h) == Upgrade.Sanctum && (h == target || h.IsNextTo(target))) ? Rules.SanctumDiscount : 0;

    /// <summary>At the end, every upgrade a seat holds is worth points.</summary>
    private void ScoreUpgrades(ReportLines lines)
    {
        foreach (var seat in seats.Where(s => UpgradesOf(s) > 0))
        {
            var points = UpgradesOf(seat) * Rules.PointsPerUpgrade;
            seat.Score += points;
            lines.Add($"Seat {seat.Index + 1}'s upgrades ({UpgradesOf(seat)}) are worth {Plural(points, "point")}, {seat.Score} in all.");
        }
    }
}
