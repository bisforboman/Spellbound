namespace Spellbound.Domain;

/// <summary>A stance picked this round, and by whom.</summary>
public sealed record StancePick(Stance Stance, int Seat);

/// <summary>
/// Whose turn it is in a round (docs/game-design.md, Rounds and stances; Puerto Rico's roles, the owner's choice,
/// 25 Oct 2026). The first picker moves one seat each round. Each pick is a stance nobody has picked this round; the
/// picker takes it first, then the seats after them in turn, so it goes once round the table (Channel only its
/// picker's). Then the next seat picks, until every seat has picked once.
/// </summary>
public sealed partial class Match
{
    private readonly List<StancePick> picks = [];

    // The seats that have taken the stance picked last, its picker included.
    private int acted;

    /// <summary>The stances picked this round, in order.</summary>
    public IReadOnlyList<StancePick> Picks => picks;

    /// <summary>The stance going round the table now, or null when the next seat is to pick one (or outside the rounds).</summary>
    public Stance? OpenStance => Phase == MatchPhase.Rounds && picks.Count > 0 && acted < TakersOf(picks[^1].Stance) ? picks[^1].Stance : null;

    /// <summary>The seat that picks first this round: the opening's rotation goes on (its Unveils start with seats 1, 2
    /// and 3), so over a match the first picks spread as evenly as they can.</summary>
    public int FirstPicker => (Math.Max(CurrentRound, 1) - 1 + Rules.OpeningUnveils) % seats.Count;

    /// <summary>The seat whose turn it is, to pick a stance or to take the open one; null outside the rounds.</summary>
    public int? Actor => Phase != MatchPhase.Rounds ? null
        : OpenStance is null ? (FirstPicker + picks.Count) % seats.Count
        : (picks[^1].Seat + acted) % seats.Count;

    /// <summary>The stances still free to pick this round.</summary>
    public IReadOnlyList<Stance> Unpicked => [.. Enum.GetValues<Stance>().Where(s => picks.All(p => p.Stance != s))];

    /// <summary>Whether a seat's turn now is to pick a stance (rather than take the one going round).</summary>
    public bool IsPicking(int seat) => OpenStance is null && Actor == seat;

    /// <summary>What a seat does when it lets its turn go by: Channel if it is picking and Channel is free (else the first
    /// stance still free, aimed at nothing), and a pass on the open stance otherwise.</summary>
    public Orders Passing(int seat) =>
        OpenStance is { } open ? Orders.Pass(seat, open)
        : Unpicked.Contains(Stance.Channel) ? Orders.Channel(seat)
        : Orders.Pass(seat, Unpicked[0]);

    private int TakersOf(Stance stance) => stance == Stance.Channel ? 1 : seats.Count;

    /// <summary>Whether these orders are a pick: when no stance is going round, or (for a check off the seat's turn) when
    /// the seat has already had its turn on the one going round. Otherwise they take the open stance.</summary>
    private bool IsPick(Orders orders) =>
        OpenStance is null || (orders.Seat - picks[^1].Seat + seats.Count) % seats.Count < acted;

    /// <summary>The picker's discount on its own action; nothing for the seats that follow.</summary>
    private int DiscountFor(Orders orders) => IsPick(orders) && orders.Stance != Stance.Channel ? Rules.PickerDiscount : 0;

    /// <summary>Why this stance can't be taken now (as the open stance on the seat's turn, else as a pick); null if it can.</summary>
    private string? TurnProblem(Orders orders)
    {
        if (offer is { } waiting)
        {
            return $"Seat {waiting.Seat + 1} has an Attune price to pay or decline first.";
        }

        if (!IsPick(orders))
        {
            var open = OpenStance!.Value;
            if (orders.Stance != open)
            {
                return $"Seat {picks[^1].Seat + 1} picked {open}: take it too, or pass.";
            }

            return orders.Moves.Count > 0 ? "Units move only with your own pick." : null;
        }

        return picks.FirstOrDefault(p => p.Stance == orders.Stance) is { } taken
            ? $"Seat {taken.Seat + 1} has already picked {orders.Stance} this round."
            : null;
    }
}
