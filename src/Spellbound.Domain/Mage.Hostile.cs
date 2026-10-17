namespace Spellbound.Domain;

/// <summary>
/// Outcome of an action against another mage: the actor's report, plus the lines for the target's news log
/// (empty when the action failed and changed nothing).
/// </summary>
public sealed record HostileReport(ActionReport Report, IReadOnlyList<string> TargetLines)
{
    public static HostileReport Fail(string reason) => new(ActionReport.Fail(reason), []);
}

/// <summary>Attacks and hexes against other mages.</summary>
public sealed partial class Mage
{
    /// <summary>Whether the target is big enough to pick on: at least AttackRangePercent of your land.</summary>
    public bool InRange(Mage target, GameRules rules) =>
        (long)target.Land * 100 >= (long)Land * rules.AttackRangePercent;

    /// <summary>+1 if your school is strong against theirs, -1 if weak, 0 otherwise.</summary>
    public int CounterSign(Mage target) =>
        School.StrongAgainst() == target.School ? 1
        : School.WeakAgainst() == target.School ? -1
        : 0;

    /// <summary>Chance a hex lands on the target, before any Barrier they may have up.</summary>
    public double HexChance(Mage target, GameRules rules) =>
        Math.Clamp(rules.HexBaseSuccessChance + (CounterSign(target) * rules.HexCounterBonus), 0, 1);

    public HostileReport Attack(Mage target, DateTimeOffset now, GameRules rules, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(target);
        AccrueTurns(now, rules);
        target.AccrueTurns(now, rules);
        if (TargetProblem(target, rules) is { } invalid)
        {
            return HostileReport.Fail(invalid);
        }

        if (AttackPower == 0)
        {
            return HostileReport.Fail("You have no army to attack with. Recruit some units first.");
        }

        if (CheckTurns(rules.AttackTurnCost) is { } failure)
        {
            return new HostileReport(failure, []);
        }

        var attack = AttackPower * (1 + (CounterSign(target) * rules.CounterBonus)) * Roll(random, rules);
        var defense = target.DefensePower(rules) * Roll(random, rules);
        var won = attack > defense;

        var ourLosses = LoseUnits(won ? rules.AttackerLossPercentOnWin : rules.AttackerLossPercentOnLoss, roundUp: false);
        var theirLosses = target.LoseUnits(
            won ? rules.DefenderLossPercentOnLoss : rules.DefenderLossPercentOnWin, roundUp: false);

        var ours = new List<string>();
        var theirs = new List<string>();
        if (won)
        {
            var acres = Math.Max(1, target.Land * rules.LandTakenPercent / 100);
            var (nodes, towns) = target.LoseLand(acres, rules);
            Land += acres;
            ours.Add($"You attacked {target.Name} and broke through, taking {N(acres)} acres.");
            theirs.Add($"{Name} ({School}) attacked you and broke through. You lost {N(acres)} acres.");
            if (nodes + towns > 0)
            {
                var ruins = $"{N(nodes)} nodes and {N(towns)} towns were destroyed in the fighting.";
                ours.Add($"Of their buildings, {ruins}");
                theirs.Add($"Of your buildings, {ruins}");
            }
        }
        else
        {
            ours.Add($"You attacked {target.Name}, but their defenses held.");
            theirs.Add($"{Name} ({School}) attacked you, but your defenses held.");
        }

        ours.Add($"Your attack power was {N((int)attack)} against their defense of {N((int)defense)}.");
        ours.Add($"You lost {N(ourLosses)} units; they lost {N(theirLosses)}.");
        theirs.Add($"You lost {N(theirLosses)} units; they lost {N(ourLosses)}.");
        if (CounterLine(target) is { } counter)
        {
            ours.Add(counter);
        }

        var summary = SpendTurns(rules.AttackTurnCost, rules);
        return new HostileReport(Report(rules.AttackTurnCost, summary, [.. ours]), theirs);
    }

    public HostileReport CastHex(string hexId, Mage target, DateTimeOffset now, GameRules rules, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(target);
        AccrueTurns(now, rules);
        target.AccrueTurns(now, rules);
        var hex = SpellBook.FindHex(hexId);
        if (hex is null)
        {
            return HostileReport.Fail("You don't know that spell.");
        }

        if (hex.School != School)
        {
            return HostileReport.Fail($"{hex.Name} belongs to the {hex.School} school.");
        }

        if (TargetProblem(target, rules) is { } invalid)
        {
            return HostileReport.Fail(invalid);
        }

        if (Mana < hex.ManaCost)
        {
            return HostileReport.Fail($"{hex.Name} needs {N(hex.ManaCost)} mana, but you have {N(Mana)}.");
        }

        if (CheckTurns(hex.TurnCost) is { } failure)
        {
            return new HostileReport(failure, []);
        }

        Mana -= hex.ManaCost;
        var chance = HexChance(target, rules) * target.Multiplier(EffectKind.HexWard);
        string ours, theirs;
        if (random.NextDouble() < chance)
        {
            (ours, theirs) = ApplyHex(hex, target, rules);
        }
        else
        {
            ours = $"You cast {hex.Name} at {target.Name}, but it was turned aside.";
            theirs = $"{Name} ({School}) cast {hex.Name} at you, but it was turned aside.";
        }

        var summary = SpendTurns(hex.TurnCost, rules);
        return new HostileReport(Report(hex.TurnCost, summary, ours), [theirs]);
    }

    /// <summary>Why this mage can't attack or hex the target right now, or null if it can.</summary>
    public string? TargetProblem(Mage target, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Id == Id)
        {
            return "You can't target yourself.";
        }

        if (IsProtected(rules))
        {
            return $"You are under protection for {N(rules.ProtectionTurns - TurnsSpent)} more turns " +
                   "and can't attack or hex anyone yet.";
        }

        if (target.IsProtected(rules))
        {
            return $"{target.Name} is still under protection.";
        }

        return InRange(target, rules)
            ? null
            : $"{target.Name} is too small to target: they need at least {rules.AttackRangePercent}% of your land.";
    }

    // ---- Internals ----

    private (string Ours, string Theirs) ApplyHex(HexDefinition hex, Mage target, GameRules rules)
    {
        var by = $"{Name} ({School}) cast {hex.Name} at you";
        switch (hex.Effect)
        {
            case HexEffect.KillUnits:
                var killed = target.LoseUnits(hex.Strength * 100, roundUp: false);
                return ($"{hex.Name} struck {target.Name}'s army: {N(killed)} units died.",
                    $"{by}: {N(killed)} of your units died.");
            case HexEffect.DestroyGold:
                var gold = (int)(target.Gold * hex.Strength);
                target.Gold -= gold;
                return ($"{hex.Name} devoured {N(gold)} of {target.Name}'s gold.",
                    $"{by}: {N(gold)} of your gold was devoured.");
            case HexEffect.StealMana:
                var mana = (int)(target.Mana * hex.Strength);
                target.Mana -= mana;
                var kept = AddMana(mana, rules);
                return ($"{hex.Name} drew {N(mana)} mana from {target.Name}; you kept {N(kept)}.",
                    $"{by}: {N(mana)} of your mana was drained.");
            case HexEffect.BurnBuildings:
                var (nodes, towns) = target.DestroyBuildings((int)(target.BuiltLand * hex.Strength), rules);
                return ($"{hex.Name} burned {N(nodes)} nodes and {N(towns)} towns of {target.Name}'s.",
                    $"{by}: {N(nodes)} nodes and {N(towns)} towns burned.");
            case HexEffect.DrainTurns:
                var turns = Math.Min(target.Turns, (int)hex.Strength);
                target.Turns -= turns;
                return ($"{hex.Name} haunted {target.Name}: they lost {N(turns)} turns.",
                    $"{by}: you lost {N(turns)} turns.");
            default:
                throw new ArgumentOutOfRangeException(nameof(hex), hex.Effect, null);
        }
    }

    private string? CounterLine(Mage target) => CounterSign(target) switch
    {
        1 => $"Your {School} magic had the edge over their {target.School} magic.",
        -1 => $"Their {target.School} magic had the edge over your {School} magic.",
        _ => null,
    };

    /// <summary>Gives up land; if it was built on, buildings go with it.</summary>
    private (int Nodes, int Towns) LoseLand(int acres, GameRules rules)
    {
        Land -= Math.Min(acres, Land);
        return BuiltLand > Land ? DestroyBuildings(BuiltLand - Land, rules) : (0, 0);
    }

    /// <summary>Destroys buildings, split between nodes and towns in proportion to how many there are.</summary>
    private (int Nodes, int Towns) DestroyBuildings(int count, GameRules rules)
    {
        count = Math.Min(count, BuiltLand);
        if (count <= 0)
        {
            return (0, 0);
        }

        var nodes = Math.Min(Nodes, (int)Math.Round((double)count * Nodes / BuiltLand));
        var towns = Math.Min(Towns, count - nodes);
        nodes = count - towns;
        Nodes -= nodes;
        Towns -= towns;
        Mana = Math.Min(Mana, ManaCapacity(rules));
        return (nodes, towns);
    }

    private static double Roll(IRandomSource random, GameRules rules) =>
        1 + (((random.NextDouble() * 2) - 1) * rules.CombatRollSpread);
}
