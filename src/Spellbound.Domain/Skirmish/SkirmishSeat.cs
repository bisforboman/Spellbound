namespace Spellbound.Domain.Skirmish;

/// <summary>One player's mage in a Skirmish game. Its land is the regions it holds on the board.</summary>
public sealed class SkirmishSeat
{
    public SkirmishSeat(int index, string name, MagicSchool school, SkirmishRules rules)
    {
        Index = index;
        Name = name;
        School = school;
        Gold = rules.StartingGold;
        Mana = rules.StartingMana;
    }

    /// <summary>For deserialization.</summary>
    private SkirmishSeat()
    {
        Name = string.Empty;
    }

    public int Index { get; private set; }

    public string Name { get; private set; }

    public MagicSchool School { get; private set; }

    public int Gold { get; internal set; }

    public int Mana { get; internal set; }

    /// <summary>Units by <see cref="UnitDefinition.Id"/>.</summary>
    public Dictionary<string, int> Army { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Points from discovery cards.</summary>
    public int CardPoints { get; internal set; }

    /// <summary>Battles won by taking a region.</summary>
    public int BattlesWon { get; internal set; }

    /// <summary>Rounds in a row the player didn't ready up.</summary>
    public int MissedInARow { get; internal set; }

    /// <summary>Out of the game for not playing: holds nothing, scores nothing, can't be targeted.</summary>
    public bool Out { get; internal set; }

    /// <summary>Actions added to (or taken from) the next round's, by spells and hexes.</summary>
    public int ActionBonus { get; internal set; }

    /// <summary>Self-spells running on the seat.</summary>
    public List<SkirmishEffect> Effects { get; } = [];

    public int Units => Army.Values.Sum();

    public int AttackPower => UnitBook.All.Sum(u => Army.GetValueOrDefault(u.Id) * u.Attack);

    /// <summary>The army's defense, before a region's guards and terrain.</summary>
    public int DefensePower => UnitBook.All.Sum(u => Army.GetValueOrDefault(u.Id) * u.Defense);

    public bool IsAway(SkirmishRules rules) => MissedInARow >= rules.AwayAfterMissed;

    /// <summary>The multiplier of the running self-spell with this effect, or 1.</summary>
    public double Multiplier(EffectKind effect) =>
        Effects.Select(e => SpellBook.Find(e.SpellId)).FirstOrDefault(s => s?.Effect == effect)?.Multiplier ?? 1;

    public int UpkeepPerRound(SkirmishRules rules) =>
        UnitBook.All.Sum(u => Army.GetValueOrDefault(u.Id) * u.GoldUpkeep) * rules.UpkeepPerRound;

    /// <summary>Loses a share of every unit type; returns how many units were lost.</summary>
    internal int LoseUnits(int percent, bool roundUp)
    {
        var lost = 0;
        foreach (var (id, count) in Army.ToList())
        {
            var loss = roundUp ? (int)Math.Ceiling(count * percent / 100.0) : count * percent / 100;
            Army[id] = count - loss;
            lost += loss;
        }

        return lost;
    }
}
