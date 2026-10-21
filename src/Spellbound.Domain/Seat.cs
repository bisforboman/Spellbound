namespace Spellbound.Domain;

/// <summary>One player's place in a match: their mana, the tiles they hold and their banked points. Seats are
/// numbered from 0.</summary>
public sealed class Seat
{
    private readonly HashSet<Hex> tiles = [];

    public Seat(int index, int mana)
    {
        Index = index;
        Mana = mana;
    }

    public int Index { get; }

    public int Mana { get; internal set; }

    public IReadOnlySet<Hex> Tiles => tiles;

    /// <summary>Points banked so far; they are never taken away.</summary>
    public int Score { get; internal set; }

    /// <summary>Mana produced this round, scored at the round's end.</summary>
    public int ProducedThisRound { get; internal set; }

    /// <summary>Rival tiles taken and monster tiles cleared this round, scored at the round's end.</summary>
    public int ConquestsThisRound { get; internal set; }

    /// <summary>Produced mana not yet worth a point, carried to the next round's scoring.</summary>
    public int ProducedCarry { get; internal set; }

    /// <summary>Tile-rounds not yet worth a point, carried to the next round's scoring.</summary>
    public int LandCarry { get; internal set; }

    /// <summary>The traits this seat was offered to draft from; empty in a match without traits.</summary>
    public IReadOnlyList<Trait> Offer { get; internal set; } = [];

    /// <summary>The trait drafted; hidden from rivals until the rounds begin.</summary>
    public Trait? Trait { get; internal set; }

    /// <summary>The trait goal's points, once scored at the end.</summary>
    public int TraitPoints { get; internal set; }

    // What the trait goals count, over the whole match.

    /// <summary>The tiles the seat held when the opening ended.</summary>
    public IReadOnlyList<Hex> OpeningTiles { get; internal set; } = [];

    public int MonstersCleared { get; internal set; }

    public int RivalTilesTaken { get; internal set; }

    /// <summary>The different spells cast.</summary>
    public IReadOnlySet<Spell> SpellsCast => spellsCast;

    /// <summary>Rounds in which a spell was cast (and, for Scorch, burned something).</summary>
    public int RoundsCast { get; internal set; }

    /// <summary>Scorches and Blights cast.</summary>
    public int HexesCast { get; internal set; }

    private readonly HashSet<Spell> spellsCast = [];

    private readonly Dictionary<int, Trait> guesses = [];

    /// <summary>The seat's guesses at its rivals' traits, by rival seat index. Secret until the end.</summary>
    public IReadOnlyDictionary<int, Trait> Guesses => guesses;

    /// <summary>The points its right guesses earned, once scored at the end.</summary>
    public int GuessPoints { get; internal set; }

    internal void Guess(int rival, Trait trait) => guesses[rival] = trait;

    internal void Cast(Spell spell)
    {
        RoundsCast++;
        spellsCast.Add(spell);
        if (spell is Spell.Scorch or Spell.Blight)
        {
            HexesCast++;
        }
    }

    internal void Take(Hex hex) => tiles.Add(hex);

    internal void Lose(Hex hex) => tiles.Remove(hex);
}
