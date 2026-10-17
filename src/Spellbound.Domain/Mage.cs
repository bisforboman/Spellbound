using System.Globalization;

namespace Spellbound.Domain;

public enum BuildingType
{
    /// <summary>Produces mana every turn and raises mana storage.</summary>
    Node,

    /// <summary>Produces gold every turn.</summary>
    Town,
}

/// <summary>
/// A player's mage for one round. Time only moves forward for a mage when it spends turns:
/// each spent turn runs one economy tick (gold, army upkeep, mana, spell durations).
/// Every action accrues turns first, and either fully succeeds or changes nothing.
/// </summary>
public sealed partial class Mage
{
    public const int MinNameLength = 3;
    public const int MaxNameLength = 24;

    private readonly List<ActiveEffect> _activeEffects = [];

    private Mage()
    {
        OwnerId = string.Empty; // for deserialization
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Identity user id of the player who owns this mage.</summary>
    public string OwnerId { get; private set; }

    public string Name { get; private set; }

    public MagicSchool School { get; private set; }

    public int Land { get; private set; }

    public int Nodes { get; private set; }

    public int Towns { get; private set; }

    public int Gold { get; private set; }

    public int Mana { get; private set; }

    public int Turns { get; private set; }

    public DateTimeOffset TurnsAccruedAt { get; private set; }

    public int TurnsSpent { get; private set; }

    /// <summary>When the mage was created. Mages saved before this existed read as the minimum value, so they're
    /// protected by spent turns only.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<ActiveEffect> ActiveEffects => _activeEffects;

    public int BuiltLand => Nodes + Towns;

    public int FreeLand => Land - BuiltLand;

    public static Mage Create(string ownerId, string name, MagicSchool school, DateTimeOffset now, GameRules rules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(rules);

        var trimmed = name.Trim();
        if (trimmed.Length is < MinNameLength or > MaxNameLength)
        {
            throw new ArgumentException(
                $"Name must be {MinNameLength}–{MaxNameLength} characters.", nameof(name));
        }

        var mage = new Mage
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = trimmed,
            School = school,
            Land = rules.StartingLand,
            Nodes = rules.StartingNodes,
            Towns = rules.StartingTowns,
            Gold = rules.StartingGold,
            Turns = rules.StartingTurns,
            TurnsAccruedAt = now,
            CreatedAt = now,
        };
        mage.Mana = Math.Min(rules.StartingMana, mage.ManaCapacity(rules));
        return mage;
    }

    // ---- Derived values (also useful for UI previews) ----

    /// <summary>Protected until both the protection turns are spent and the protection time has passed.</summary>
    public bool IsProtected(DateTimeOffset now, GameRules rules) =>
        TurnsSpent < rules.ProtectionTurns || now < CreatedAt + rules.ProtectionDuration;

    /// <summary>What's left of the protection, in words: "for another 2 days 5 hours and until you've spent 30 more
    /// turns". Empty when the mage isn't protected.</summary>
    public string DescribeProtection(DateTimeOffset now, GameRules rules)
    {
        var turns = Math.Max(0, rules.ProtectionTurns - TurnsSpent);
        var time = CreatedAt + rules.ProtectionDuration - now;
        return (turns > 0, time > TimeSpan.Zero) switch
        {
            (true, true) => $"for another {Duration(time)} and until you've spent {N(turns)} more turns",
            (false, true) => $"for another {Duration(time)}",
            (true, false) => $"until you've spent {N(turns)} more turns",
            _ => string.Empty,
        };

        static string Duration(TimeSpan span) => span.TotalDays >= 1
            ? $"{(int)span.TotalDays} {Plural((int)span.TotalDays, "day")} {span.Hours} {Plural(span.Hours, "hour")}"
            : span.TotalHours >= 1
                ? $"{(int)span.TotalHours} {Plural((int)span.TotalHours, "hour")} {span.Minutes} {Plural(span.Minutes, "minute")}"
                : $"{Math.Max(1, (int)Math.Ceiling(span.TotalMinutes))} minutes";

        static string Plural(int count, string word) => count == 1 ? word : word + "s";
    }

    public int ManaCapacity(GameRules rules) => rules.BaseManaStorage + (Nodes * rules.ManaStoragePerNode);

    public int ExploreAcresPerTurn(GameRules rules)
    {
        var baseAcres = (int)((long)rules.ExploreBaseAcresPerTurn * rules.StartingLand / Math.Max(1, Land));
        return Math.Max(1, (int)(baseAcres * Multiplier(EffectKind.ExploreYield)));
    }

    public int MeditateManaPerTurn(GameRules rules) =>
        (int)(rules.MeditateManaPerTurn * Multiplier(EffectKind.MeditateYield));

    public int BuildGoldCost(int count, GameRules rules) =>
        (int)Math.Ceiling(count * rules.BuildingGoldCost * Multiplier(EffectKind.BuildCost));

    public static int BuildTurnCost(int count, GameRules rules) =>
        (count + rules.BuildingsPerTurn - 1) / rules.BuildingsPerTurn;

    public TimeSpan? TimeUntilNextTurn(DateTimeOffset now, GameRules rules) =>
        TurnClock.TimeUntilNextTurn(Turns, TurnsAccruedAt, now, rules);

    // ---- Turns ----

    /// <summary>Brings the turn counter up to date. Safe to call any number of times.</summary>
    public void AccrueTurns(DateTimeOffset now, GameRules rules)
    {
        var accrual = TurnClock.Accrue(Turns, TurnsAccruedAt, now, rules);
        Turns = accrual.Turns;
        TurnsAccruedAt = accrual.AccruedAt;
    }

    // ---- Actions ----

    public ActionReport Explore(int turns, DateTimeOffset now, GameRules rules)
    {
        AccrueTurns(now, rules);
        if (CheckTurns(turns) is { } failure)
        {
            return failure;
        }

        var acresGained = 0;
        var summary = SpendTurns(turns, rules, () =>
        {
            var acres = ExploreAcresPerTurn(rules);
            Land += acres;
            acresGained += acres;
        });

        return Report(turns, summary, $"You explored {N(acresGained)} new acres. You now hold {N(Land)} acres.");
    }

    public ActionReport Build(BuildingType type, int count, DateTimeOffset now, GameRules rules)
    {
        AccrueTurns(now, rules);
        if (count <= 0)
        {
            return ActionReport.Fail("Choose at least one building.");
        }

        if (count > FreeLand)
        {
            return ActionReport.Fail($"You only have {N(FreeLand)} free acres.");
        }

        var goldCost = BuildGoldCost(count, rules);
        if (goldCost > Gold)
        {
            return ActionReport.Fail($"Building {N(count)} costs {N(goldCost)} gold, but you have {N(Gold)}.");
        }

        var turns = BuildTurnCost(count, rules);
        if (CheckTurns(turns) is { } failure)
        {
            return failure;
        }

        Gold -= goldCost;
        switch (type)
        {
            case BuildingType.Node:
                Nodes += count;
                break;
            case BuildingType.Town:
                Towns += count;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        var summary = SpendTurns(turns, rules);
        var noun = type == BuildingType.Node ? "nodes" : "towns";
        return Report(turns, summary, $"You built {N(count)} {noun} for {N(goldCost)} gold.");
    }

    public ActionReport Meditate(int turns, DateTimeOffset now, GameRules rules)
    {
        AccrueTurns(now, rules);
        if (CheckTurns(turns) is { } failure)
        {
            return failure;
        }

        var manaGained = 0;
        var summary = SpendTurns(turns, rules, () =>
        {
            manaGained += AddMana(MeditateManaPerTurn(rules), rules);
        });

        var line = manaGained > 0
            ? $"You meditated and gathered {N(manaGained)} extra mana."
            : "You meditated, but your mana stores are full.";
        return Report(turns, summary, line);
    }

    public ActionReport Cast(string spellId, DateTimeOffset now, GameRules rules)
    {
        AccrueTurns(now, rules);
        var spell = SpellBook.Find(spellId);
        if (spell is null)
        {
            return ActionReport.Fail("You don't know that spell.");
        }

        if (spell.School is { } school && school != School)
        {
            return ActionReport.Fail($"{spell.Name} belongs to the {spell.School} school.");
        }

        if (Mana < spell.ManaCost)
        {
            return ActionReport.Fail($"{spell.Name} needs {N(spell.ManaCost)} mana, but you have {N(Mana)}.");
        }

        if (CheckTurns(spell.TurnCost) is { } failure)
        {
            return failure;
        }

        Mana -= spell.ManaCost;
        var summary = SpendTurns(spell.TurnCost, rules);

        var existing = _activeEffects.Find(e => e.SpellId == spell.Id);
        string line;
        if (existing is null)
        {
            _activeEffects.Add(new ActiveEffect(spell.Id, spell.DurationTurns));
            line = $"You cast {spell.Name}. {spell.Description}";
        }
        else
        {
            existing.RemainingTurns = spell.DurationTurns;
            line = $"You renewed {spell.Name} for another {spell.DurationTurns} turns.";
        }

        return Report(spell.TurnCost, summary, line);
    }

    // ---- Internals ----

    private ActionReport? CheckTurns(int turns)
    {
        if (turns <= 0)
        {
            return ActionReport.Fail("Choose at least one turn.");
        }

        return turns > Turns ? ActionReport.Fail($"That takes {N(turns)} turns, but you have {N(Turns)}.") : null;
    }

    /// <summary>
    /// Spends turns one at a time: optional per-turn action, then economy tick, then spell durations.
    /// Callers must have validated that enough turns are available.
    /// </summary>
    private TurnSummary SpendTurns(int turns, GameRules rules, Action? perTurnAction = null)
    {
        var summary = new TurnSummary();
        for (var i = 0; i < turns; i++)
        {
            perTurnAction?.Invoke();

            var gold = (int)(((Land * rules.GoldPerAcrePerTurn) + (Towns * rules.GoldPerTownPerTurn))
                             * Multiplier(EffectKind.GoldIncome));
            Gold += gold;
            summary.Gold += gold;
            PayUpkeep(rules, summary);

            var mana = (int)(Nodes * rules.ManaPerNodePerTurn * Multiplier(EffectKind.ManaIncome));
            summary.Mana += AddMana(mana, rules);

            TickEffects(summary.Faded);
        }

        Turns -= turns;
        TurnsSpent += turns;
        return summary;
    }

    /// <summary>Adds mana up to capacity and returns how much was actually added.</summary>
    private int AddMana(int amount, GameRules rules)
    {
        var room = Math.Max(0, ManaCapacity(rules) - Mana);
        var added = Math.Clamp(amount, 0, room);
        Mana += added;
        return added;
    }

    private void TickEffects(List<string> faded)
    {
        for (var i = _activeEffects.Count - 1; i >= 0; i--)
        {
            var effect = _activeEffects[i];
            effect.RemainingTurns--;
            if (effect.RemainingTurns <= 0)
            {
                _activeEffects.RemoveAt(i);
                faded.Add(SpellBook.Find(effect.SpellId)?.Name ?? effect.SpellId);
            }
        }
    }

    private double Multiplier(EffectKind kind)
    {
        var multiplier = 1.0;
        foreach (var effect in _activeEffects)
        {
            var spell = SpellBook.Find(effect.SpellId);
            if (spell is not null && spell.Effect == kind)
            {
                multiplier *= spell.Multiplier;
            }
        }

        return multiplier;
    }

    private static ActionReport Report(int turns, TurnSummary summary, params string[] headlines)
    {
        var lines = new List<string>(headlines)
        {
            $"Over {N(turns)} {(turns == 1 ? "turn" : "turns")} your towns and lands brought in {N(summary.Gold)} gold " +
            $"and your nodes produced {N(summary.Mana)} mana.",
        };
        if (summary.Upkeep > 0)
        {
            lines.Add($"Your army's upkeep cost {N(summary.Upkeep)} gold.");
        }

        if (summary.Deserted > 0)
        {
            lines.Add($"You ran out of gold to pay your army: {N(summary.Deserted)} units deserted.");
        }

        lines.AddRange(summary.Faded.Select(name => $"{name} has faded."));
        return new ActionReport(true, turns, lines);
    }

    private static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private sealed class TurnSummary
    {
        public int Gold { get; set; }

        public int Mana { get; set; }

        public int Upkeep { get; set; }

        public int Deserted { get; set; }

        public List<string> Faded { get; } = [];
    }
}
