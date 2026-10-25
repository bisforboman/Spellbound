namespace Spellbound.Domain;

/// <summary>
/// Every number the game runs on (docs/game-design.md); nothing else hardcodes them. Each game stores the rules it
/// runs under. All of them are first guesses until playtested.
/// </summary>
public sealed record GameRules
{
    public static GameRules Default { get; } = new();

    // Map

    /// <summary>The board's radius for up to this many players: 2 (19 hexes) for 2–3 players.</summary>
    public int SmallMapMaxPlayers { get; init; } = 3;
    public int SmallMapRadius { get; init; } = 2;

    /// <summary>The board's radius for more players: 3 (37 hexes) for 4–5.</summary>
    public int LargeMapRadius { get; init; } = 3;

    public int MinPlayers { get; init; } = 2;
    public int MaxPlayers { get; init; } = 5;

    // Setup and the opening (docs/game-design.md, Resources and Unveil)

    /// <summary>The mana each player starts with, for the rounds: the opening draft is free.</summary>
    public int StartingMana { get; init; } = 4;

    /// <summary>How many Unveils the opening has. Each reveals one tile per player, and the players take one each in turn.</summary>
    public int OpeningUnveils { get; init; } = 3;

    /// <summary>The tiles revealed unowned as the rounds begin, free for anyone to attune.</summary>
    public int FreeTilesWhenRoundsBegin { get; init; } = 3;

    // Rounds (docs/game-design.md, Rounds and stances, Units, Taking unowned tiles, Scoring and winning)

    /// <summary>How many rounds a match lasts after the opening.</summary>
    public int Rounds { get; init; } = 8;

    /// <summary>What a stance's picker saves on their own action (docs/game-design.md, Rounds and stances): this much
    /// off what it costs, never below nothing.</summary>
    public int PickerDiscount { get; init; } = 1;

    /// <summary>The mana Channel gives its picker; nobody else takes Channel.</summary>
    public int ChannelMana { get; init; } = 2;

    public int ImpPower { get; init; } = 1;
    public int ImpCost { get; init; } = 1;
    public int GolemPower { get; init; } = 3;
    public int GolemCost { get; init; } = 2;

    /// <summary>The mana each Golem costs at the start of every round, after income.</summary>
    public int GolemUpkeep { get; init; } = 1;

    /// <summary>An Attune costs this plus <see cref="AttunePricePerTile"/> for each of the first
    /// <see cref="AttuneCheapTiles"/> tiles the player already holds, and <see cref="AttunePricePerTileBeyond"/> for
    /// each tile beyond them: cheap for a small realm, dear for a big one.</summary>
    public int AttuneBasePrice { get; init; } = 1;
    public int AttunePricePerTile { get; init; } = 1;
    public int AttuneCheapTiles { get; init; } = 3;
    public int AttunePricePerTileBeyond { get; init; } = 3;

    /// <summary>An Attune's price is its base plus a die of this many faces, rolled when the stance is taken (the seat
    /// then pays or declines).</summary>
    public int AttuneDieFaces { get; init; } = 6;

    /// <summary>What each step beyond the first from a seat's nearest tile adds to an Attune's base price: any free tile
    /// can be attuned, nearby land is cheapest.</summary>
    public int AttuneDistanceCost { get; init; } = 1;

    /// <summary>The rounds that start with a later Unveil, which reveals the tiles still dark, split evenly (an odd one
    /// goes to the earlier round). After the last of them the whole map is revealed.</summary>
    public IReadOnlyList<int> LaterUnveilRounds { get; init; } = [2, 4];

    /// <summary>A tile revealed in a later Unveil is guarded on this many faces of a die with
    /// <see cref="GuardDieFaces"/> faces: 2 in 6, about one in three.</summary>
    public int GuardedFaces { get; init; } = 2;
    public int GuardDieFaces { get; init; } = 6;

    /// <summary>The battle power of a tile's monster guard.</summary>
    public int MonsterPower { get; init; } = 3;

    // Spells (docs/game-design.md, Spells)

    /// <summary>Scorch's price for each power that actually burns; what doesn't burn (units that moved away, or a
    /// Golem too strong to burn whole) costs nothing.</summary>
    public int ScorchManaPerPower { get; init; } = 3;
    public int WardCost { get; init; } = 3;

    /// <summary>The defence a Ward adds to its tile in this round's battles.</summary>
    public int WardPower { get; init; } = 3;

    public int LeylineManaPerUnit { get; init; } = 1;
    public int LeylineMinCost { get; init; } = 2;
    public int BlightCost { get; init; } = 3;
    public int LureCost { get; init; } = 3;
    public int PacifyCost { get; init; } = 3;

    /// <summary>The spells each trait lets its player cast (owner's choice, 24 Oct 2026): one each, and the whole book
    /// for a Spellweaver. Warden's was Ward until Pacify came (25 Oct 2026); a match keeps the table it was saved with. A seat without a trait can cast any spell. Kept in trait order, so
    /// the rules save the same however they were read (the database's jsonb reorders keys).</summary>
    public IReadOnlyDictionary<Trait, IReadOnlyList<Spell>> TraitSpells
    {
        get => traitSpells;
        init => traitSpells = new SortedDictionary<Trait, IReadOnlyList<Spell>>(
            (value ?? throw new ArgumentNullException(nameof(value))).ToDictionary(p => p.Key, p => p.Value));
    }

    private readonly SortedDictionary<Trait, IReadOnlyList<Spell>> traitSpells = new()
    {
        [Trait.Hearthkeeper] = [Spell.Ward],
        [Trait.Warden] = [Spell.Pacify],
        [Trait.Wanderer] = [Spell.Leyline],
        [Trait.Weaver] = [Spell.Leyline],
        [Trait.Beastbane] = [Spell.Lure],
        [Trait.Golemwright] = [Spell.Lure],
        [Trait.Wildheart] = [Spell.Blight],
        [Trait.Hexer] = [Spell.Blight],
        [Trait.Warbrand] = [Spell.Scorch],
        [Trait.Spellweaver] = [Spell.Scorch, Spell.Ward, Spell.Leyline, Spell.Blight, Spell.Lure, Spell.Pacify],
    };

    /// <summary>Every spell a seat can cast with no trait in the way (Rally, removed, isn't one).</summary>
    public static IReadOnlyList<Spell> AllSpells { get; } = [Spell.Scorch, Spell.Ward, Spell.Leyline, Spell.Blight, Spell.Lure, Spell.Pacify];

    /// <summary>How much a Lure weakens a monster guard, for good; a guard never drops below <see cref="MinGuard"/>.</summary>
    public int LurePower { get; init; } = 1;
    public int MinGuard { get; init; } = 1;

    /// <summary>Points banked for each monster tile cleared or defended rival tile taken.</summary>
    public int PointsPerConquest { get; init; } = 2;

    /// <summary>Points for taking a rival's tile that had no defence at all (no units, Ward or Tower): a raid.</summary>
    public int PointsPerEmptyConquest { get; init; } = 1;

    // Reinforcing (docs/game-design.md, Rounds and stances): a small summon alongside an attack.

    /// <summary>The most power an attack's reinforcement can summon.</summary>
    public int ReinforceMaxPower { get; init; } = 3;

    /// <summary>What a reinforcement costs on top of the units' price, per unit.</summary>
    public int ReinforceSurcharge { get; init; } = 1;

    public int ReinforceCostOf(UnitKind kind, int count) => (CostOf(kind) + ReinforceSurcharge) * count;

    /// <summary>One point for every this many tiles held, counted every round and added up: holding 4 tiles for 3 rounds
    /// is 12 tile-rounds, 4 points. The remainder carries over.</summary>
    public int TilesPerPoint { get; init; } = 3;

    /// <summary>One point for every this much mana a player's land produced; the remainder carries over.</summary>
    public int ManaPerPoint { get; init; } = 10;

    // Upgrades (docs/game-design.md, Upgrades)

    /// <summary>What an upgrade costs to build (the Build stance).</summary>
    public int UpgradeCost { get; init; } = 5;

    /// <summary>The most upgrades a realm can hold when it builds another; taken ones count.</summary>
    public int UpgradeCap { get; init; } = 3;

    public int ShrineYield { get; init; } = 1;
    public int TowerPower { get; init; } = 2;
    public int SanctumDiscount { get; init; } = 1;

    /// <summary>Points at the end for each upgrade a seat holds.</summary>
    public int PointsPerUpgrade { get; init; } = 1;

    // Traits (docs/game-design.md, Traits)

    /// <summary>The traits each seat is offered to draft one from; no trait is offered to two seats.</summary>
    public int TraitsOffered { get; init; } = 2;

    /// <summary>The most points a trait goal can add.</summary>
    public int TraitGoalCap { get; init; } = 3;

    /// <summary>Wildheart and Hearthkeeper: one point per this many tiles of their type.</summary>
    public int TilesOfTypePerTraitPoint { get; init; } = 1;

    /// <summary>Weaver: one point per this many tiles in the largest connected group.</summary>
    public int GroupTilesPerTraitPoint { get; init; } = 2;

    /// <summary>Weaver: the tiles of the largest group that score nothing; one point per
    /// <see cref="GroupTilesPerTraitPoint"/> beyond them (4 tiles = 1 point, 6 = 2, 8 = 3).</summary>
    public int WeaverFreeTiles { get; init; } = 2;

    /// <summary>Wanderer: the smallest group that counts (a lone tile doesn't).</summary>
    public int WandererGroupSize { get; init; } = 2;

    /// <summary>Spellweaver: one point per this many different spells cast...</summary>
    public int SpellsPerTraitPoint { get; init; } = 2;

    /// <summary>...and one more for casting a spell in this many rounds.</summary>
    public int SpellRoundsForTraitPoint { get; init; } = 6;


    /// <summary>Golemwright: one point per this many Golems at the end.</summary>
    public int GolemsPerTraitPoint { get; init; } = 3;

    /// <summary>Hexer: one point per this many rounds it casts Blight (or Scorch, if its spells include it).</summary>
    public int HexesPerTraitPoint { get; init; } = 1;

    // Tiles: the possible yields of each type, one entry per face of a die, so a type's spread and odds are both here.
    // A held tile rolls its yield every round. On average every type yields 2.

    public IReadOnlyList<int> SteadyYields { get; init; } = [2, 2, 2, 2, 2, 2];
    public IReadOnlyList<int> ShiftingYields { get; init; } = [1, 1, 2, 2, 3, 3];
    public IReadOnlyList<int> WildYields { get; init; } = [0, 1, 2, 2, 3, 4];

    public int MapRadius(int players)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players, MinPlayers);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(players, MaxPlayers);
        return players <= SmallMapMaxPlayers ? SmallMapRadius : LargeMapRadius;
    }

    public int CostOf(UnitKind kind) => kind == UnitKind.Golem ? GolemCost : ImpCost;

    public int PowerOf(UnitKind kind) => kind == UnitKind.Golem ? GolemPower : ImpPower;

    public int AttunePrice(int tilesHeld) => AttuneBasePrice + AttunePricePerTile * Math.Min(tilesHeld, AttuneCheapTiles)
        + AttunePricePerTileBeyond * Math.Max(0, tilesHeld - AttuneCheapTiles);

    /// <summary>One roll of a tile's yield: a face of its type's die.</summary>
    public int RollYield(TileType type, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var faces = YieldsOf(type);
        return faces[random.Next(faces.Count)];
    }

    /// <summary>Whether a type's yield can change from one roll to the next (Steady's can't).</summary>
    public bool Varies(TileType type) => YieldsOf(type).Distinct().Skip(1).Any();

    /// <summary>A type's possible yields in words: "2" or "0–4".</summary>
    public string YieldRange(TileType type) =>
        Varies(type) ? $"{YieldsOf(type).Min()}–{YieldsOf(type).Max()}" : $"{YieldsOf(type)[0]}";

    /// <summary>What a type yields on average, for the bots' sums.</summary>
    public double AverageYield(TileType type) => YieldsOf(type).Average();

    public IReadOnlyList<int> YieldsOf(TileType type) => type switch
    {
        TileType.Steady => SteadyYields,
        TileType.Shifting => ShiftingYields,
        TileType.Wild => WildYields,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
