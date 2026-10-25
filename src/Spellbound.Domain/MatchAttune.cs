namespace Spellbound.Domain;

/// <summary>An Attune's rolled price, waiting for its seat to pay it or decline.</summary>
public sealed record AttuneOffer(int Seat, Hex Tile, int Roll, int Price);

/// <summary>
/// Attuning (docs/game-design.md, Taking unowned tiles; owner's choice, 25 Oct 2026): any free, unveiled, unguarded tile,
/// for a base price (the realm's size, plus a little for each step from its nearest tile) and a die. Taking the stance
/// rolls the die and shows the price; the seat then pays it, taking the tile, or declines. Either ends its turn, so a
/// price is rolled once.
/// </summary>
public sealed partial class Match
{
    private AttuneOffer? offer;

    /// <summary>The rolled Attune price waiting to be paid or declined, if any.</summary>
    public AttuneOffer? Offer => offer;

    /// <summary>A tile's Attune price before the die: the realm's price by its size, plus
    /// <see cref="GameRules.AttuneDistanceCost"/> for each step beyond the first from the seat's nearest tile.</summary>
    public int AttuneBase(int seat, Hex tile)
    {
        var mine = seats[seat].Tiles;
        var steps = mine.Count == 0 ? 1 : mine.Min(h => h.DistanceTo(tile));
        return Rules.AttunePrice(mine.Count) + Rules.AttuneDistanceCost * Math.Max(0, steps - 1);
    }

    /// <summary>
    /// The seat whose Attune price was rolled pays it, taking the tile, or declines, paying nothing. Either way its turn
    /// is over (and, after the round's last turn, the round ends). Fails, changing nothing, unless the seat has a price
    /// waiting and, to pay, the mana for it.
    /// </summary>
    public Report Settle(int seat, bool pay, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (offer is not { } o || o.Seat != seat)
        {
            return Report.Fail("There's no Attune price waiting for you to pay.");
        }

        var me = seats[seat];
        if (pay && o.Price > me.Mana)
        {
            return Report.Fail($"It costs {o.Price} mana; you have {me.Mana}.");
        }

        var lines = new ReportLines();
        if (pay)
        {
            me.Mana -= o.Price;
            me.Take(o.Tile);
            lines.Add($"Seat {seat + 1} attunes {Describe(o.Tile)} for {o.Price} mana.");
        }
        else
        {
            lines.Add($"Seat {seat + 1} won't pay {o.Price} mana for {Describe(o.Tile)}.");
        }

        offer = null;
        EndTurn(lines, random);
        return lines.ToReport();
    }

    /// <summary>Rolls the die for an Attune and holds the price for its seat to pay or decline.</summary>
    private void RollAttune(Orders order, int discount, IRandomSource random, ReportLines lines)
    {
        var tile = order.Target!.Value;
        var roll = random.Next(Rules.AttuneDieFaces) + 1;
        var price = Math.Max(0, AttuneBase(order.Seat, tile) + roll - discount);
        offer = new AttuneOffer(order.Seat, tile, roll, price);
        lines.Add($"Seat {order.Seat + 1} rolls a {roll} for {Describe(tile)}: it costs {price} mana.");
    }

    private string? AttuneProblem(Orders orders, Hex target, List<string> lines)
    {
        var seat = seats[orders.Seat];
        if (!Map[target].IsUnveiled || OwnerOf(target) is not null)
        {
            return $"Only a free, unveiled tile can be attuned; {target} isn't one.";
        }

        if (Map[target].IsGuarded)
        {
            return $"{target} is guarded by monsters: clear it with an attack.";
        }

        // The mana in hand counts a picker's discount here (see Check), so the cheapest roll is what it must cover.
        var low = AttuneBase(orders.Seat, target) + 1;
        if (low > seat.Mana)
        {
            return $"Attuning {target} costs at least {low} mana; you have {seat.Mana}.";
        }

        var high = low + Rules.AttuneDieFaces - 1;
        lines.Add($"Attune {Describe(target)}: {low - 1} + a die, so {low}–{high} mana. You see the price, then pay or decline.");
        return null;
    }
}
