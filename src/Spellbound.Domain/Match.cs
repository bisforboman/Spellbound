namespace Spellbound.Domain;

/// <summary>Where a match is: the opening's draft, the rounds, or over.</summary>
public enum MatchPhase
{
    Opening,
    Rounds,
    Ended,
}

/// <summary>
/// One game of Spellbound, from the dark map to the end (docs/game-design.md). Everything random comes from the
/// injected source, so the same seed and the same moves always play out the same. Actions either fully succeed or
/// change nothing.
/// </summary>
public sealed partial class Match
{
    private readonly List<Seat> seats;
    private readonly List<Hex> toPick = [];

    private Match(GameRules rules, GameMap map, List<Seat> seats)
    {
        Rules = rules;
        Map = map;
        this.seats = seats;
    }

    public GameRules Rules { get; }

    public GameMap Map { get; }

    public IReadOnlyList<Seat> Seats => seats;

    public MatchPhase Phase { get; private set; }

    /// <summary>The opening Unveil being drafted (1 to <see cref="GameRules.OpeningUnveils"/>), or 0 once the
    /// opening is over.</summary>
    public int OpeningUnveil { get; private set; }

    /// <summary>The tiles of the current opening Unveil still to be picked, by name (row, then place in the row).</summary>
    public IReadOnlyList<Hex> ToPick => toPick;

    /// <summary>The seat holding a tile, or null if it is unowned.</summary>
    public int? OwnerOf(Hex hex) => seats.FirstOrDefault(s => s.Tiles.Contains(hex))?.Index;

    /// <summary>Starts a match: a dark map for this many players, each seat with its starting mana, and the first
    /// opening Unveil on the table.</summary>
    public static Match Start(int players, GameRules rules, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var map = GameMap.Create(players, rules, random);
        var match = new Match(rules, map, [.. Enumerable.Range(0, players).Select(i => new Seat(i, rules.StartingMana))]);
        match.NextOpeningUnveil(random);
        match.DealTraits(random);
        return match;
    }

    /// <summary>
    /// The seat whose turn it is takes one of this Unveil's tiles (docs/game-design.md, "The opening draft"). When one
    /// tile is left, it goes to the last seat to pick. Then the next Unveil is laid out, or after the last one the rounds
    /// begin. Fails, changing nothing, unless it is this seat's turn and the tile is still on the table.
    /// </summary>
    public Report Draft(Pick pick, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(pick);
        ArgumentNullException.ThrowIfNull(random);
        var check = CheckPick(pick);
        if (!check.Succeeded)
        {
            return check;
        }

        var lines = new ReportLines();
        Hand(pick.Tile, pick.Seat, lines, $"Seat {pick.Seat + 1} picks {Describe(pick.Tile)}.");
        if (toPick.Count == 1)
        {
            var last = Picker!.Value;
            Hand(toPick[0], last, lines, $"Seat {last + 1} gets the last tile, {Describe(toPick[0])}.");
        }

        if (toPick.Count > 0)
        {
            return lines.ToReport();
        }

        if (OpeningUnveil < Rules.OpeningUnveils)
        {
            NextOpeningUnveil(random);
        }
        else
        {
            OpeningUnveil = 0;
            Phase = MatchPhase.Rounds;
            lines.Add("The opening is over. The rounds begin.");
            RevealFreeTiles(random, lines);
            SettleTraits(lines);
            lines.Append(StartRound(random));
        }

        return lines.ToReport();
    }

    /// <summary>The seat whose turn it is to pick, or null outside the opening. Each Unveil goes round the table once,
    /// starting one seat later than the one before (1-2-3, 2-3-1, 3-1-2), like the rounds' rotating first seat.</summary>
    public int? Picker => Phase == MatchPhase.Opening ? (OpeningUnveil - 1 + PickNumber - 1) % seats.Count : null;

    /// <summary>Which pick of this Unveil is up now, counting from 1.</summary>
    public int PickNumber => seats.Count - toPick.Count + 1;

    private void Hand(Hex tile, int seat, ReportLines lines, string line)
    {
        seats[seat].Take(tile);
        toPick.Remove(tile);
        lines.Add(line);
    }

    /// <summary>What a pick gets, or why it can't be made. Changes nothing.</summary>
    public Report CheckPick(Pick pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        if (Phase != MatchPhase.Opening)
        {
            return Report.Fail("The opening draft is over.");
        }

        if (pick.Seat != Picker)
        {
            return Report.Fail($"It's Seat {Picker + 1}'s turn to pick, not Seat {pick.Seat + 1}'s.");
        }

        if (!toPick.Contains(pick.Tile))
        {
            return Report.Fail($"{pick.Tile} isn't one of the tiles to pick from.");
        }

        return new Report(true,
        [
            $"Pick {Describe(pick.Tile)}. It costs nothing.",
            toPick.Count == 2 ? $"Seat {(Picker + 1) % seats.Count + 1} gets the other one." : $"Then Seat {(Picker + 1) % seats.Count + 1} picks from the rest.",
        ]);
    }

    /// <summary>Reveals the next opening Unveil: one tile per seat, from the dark tiles at random, listed by name.</summary>
    private void NextOpeningUnveil(IRandomSource random)
    {
        OpeningUnveil++;
        toPick.Clear();
        toPick.AddRange(RevealDark(seats.Count, random).OrderBy(h => h.R).ThenBy(h => h.Q));
    }

    /// <summary>As the rounds begin, the free tiles are revealed: unowned, for anyone to attune (the opening itself is
    /// only the draft; owner's choice, 24 Oct 2026).</summary>
    private void RevealFreeTiles(IRandomSource random, ReportLines lines)
    {
        var free = RevealDark(Rules.FreeTilesWhenRoundsBegin, random).OrderBy(h => h.R).ThenBy(h => h.Q).ToList();
        if (free.Count > 0)
        {
            lines.Add($"Free to attune: {string.Join("; ", free.Select(Describe))}.");
        }
    }

    /// <summary>Turns over this many dark tiles, picked at random (never guarded: only later Unveils have monsters).</summary>
    private List<Hex> RevealDark(int count, IRandomSource random)
    {
        var dark = Map.Dark.Select(t => t.Hex).OrderBy(h => h.Q).ThenBy(h => h.R).ToList();
        var revealed = new List<Hex>();
        for (var i = 0; i < count && dark.Count > 0; i++)
        {
            var pick = random.Next(dark.Count);
            Map.Unveil(dark[pick], Rules, random);
            revealed.Add(dark[pick]);
            dark.RemoveAt(pick);
        }

        return revealed;
    }

    private string Describe(Hex hex) =>
        $"the {Map[hex].Type} tile at {hex} (yields {Rules.YieldRange(Map[hex].Type)}" + (Map[hex].IsGuarded ? $", guarded by power {Map[hex].Guard})" : ")");

    /// <summary>A tile's type, place and yield, for lines that say who holds it separately.</summary>
    private string DescribePlain(Hex hex) => $"the {Map[hex].Type} tile at {hex} (yields {Rules.YieldRange(Map[hex].Type)})";
}
