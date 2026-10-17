using Spellbound.Domain;

namespace Spellbound.Balance;

/// <summary>A simulated player: plays one check-in with every turn it has, through Mage's public actions only.</summary>
public abstract class Bot(string name, MagicSchool school)
{
    public string Name { get; } = name;

    public MagicSchool School { get; } = school;

    public abstract string Strategy { get; }

    public abstract void Play(Mage me, IReadOnlyList<Mage> others, Sim sim);

    /// <summary>
    /// The shared economy: cast the school's buff when affordable, build on free land (a share as nodes), and explore
    /// when there's nothing to build. Keeps <paramref name="goldReserve"/> gold back and stops at
    /// <paramref name="keepTurns"/> turns.
    /// </summary>
    protected static void Grow(Mage me, Sim sim, double nodeShare = 0.25, int goldReserve = 0, int keepTurns = 0)
    {
        var rules = sim.Rules;
        CastBuff(me, sim);
        while (me.Turns > keepTurns)
        {
            var budget = me.Turns - keepTurns;
            var affordable = Math.Max(0, me.Gold - goldReserve) / rules.BuildingGoldCost;
            var count = Math.Min(me.FreeLand, Math.Min(affordable, budget * rules.BuildingsPerTurn));
            if (count > 0)
            {
                var wantNodes = me.Nodes < nodeShare * (me.BuiltLand + count);
                if (me.Build(wantNodes ? BuildingType.Node : BuildingType.Town, count, sim.Now, rules).Succeeded)
                {
                    continue;
                }
            }

            me.Explore(Math.Min(10, budget), sim.Now, rules);
        }
    }

    protected static void CastBuff(Mage me, Sim sim)
    {
        foreach (var spell in SpellBook.ForSchool(me.School).Where(s => s.School is not null))
        {
            if (me.ActiveEffects.All(e => e.SpellId != spell.Id) && me.Mana >= spell.ManaCost && me.Turns >= spell.TurnCost)
            {
                me.Cast(spell.Id, sim.Now, sim.Rules);
            }
        }
    }
}

/// <summary>Grows land and buildings, never fights.</summary>
public sealed class Builder(string name) : Bot(name, MagicSchool.Radiant)
{
    public override string Strategy => "economy only";

    public override void Play(Mage me, IReadOnlyList<Mage> others, Sim sim) => Grow(me, sim);
}

/// <summary>Grows, keeps militia at one per acre and a Barrier up.</summary>
public sealed class Turtle(string name) : Bot(name, MagicSchool.Tidal)
{
    public override string Strategy => "economy + militia (1/acre) + barrier";

    public override void Play(Mage me, IReadOnlyList<Mage> others, Sim sim)
    {
        var rules = sim.Rules;
        var barrier = SpellBook.Find("barrier")!;
        if (me.ActiveEffects.All(e => e.SpellId != barrier.Id) && me.Mana >= barrier.ManaCost && me.Turns >= barrier.TurnCost)
        {
            me.Cast("barrier", sim.Now, rules);
        }

        var wanted = me.Land - me.UnitCount("militia");
        var affordable = me.Gold / UnitBook.Find("militia")!.GoldCost;
        var count = Math.Min(wanted, Math.Min(affordable, me.Turns * rules.UnitsRecruitedPerTurn));
        if (count > 0)
        {
            me.Recruit("militia", count, sim.Now, rules);
        }

        Grow(me, sim);
    }
}

/// <summary>
/// Grows, puts half its spare gold into knights, and attacks the biggest target it can surely beat, again and again
/// while it has turns. Sees targets' true defense (players can't; this is the worst case for defenders).
/// </summary>
public sealed class Warmonger(string name) : Bot(name, MagicSchool.Ember)
{
    public override string Strategy => "economy + knights + attacks when it beats exploring";

    public override void Play(Mage me, IReadOnlyList<Mage> others, Sim sim)
    {
        var rules = sim.Rules;
        while (me.Turns >= rules.AttackTurnCost && !me.IsProtected(sim.Now, rules))
        {
            var target = others
                .Where(t => me.TargetProblem(t, sim.Now, rules) is null)
                .Where(t => me.AttackPower * (1 + (me.CounterSign(t) * rules.CounterBonus)) * (1 - rules.CombatRollSpread)
                            > t.DefensePower(rules) * (1 + rules.CombatRollSpread))
                .Select(t => (Target: t, Acres: (int)(t.Land * t.LandTakenPercent(sim.Now, rules) / 100)))
                .Where(x => x.Acres >= rules.AttackTurnCost * me.ExploreAcresPerTurn(rules)) // else exploring pays more
                .OrderByDescending(x => x.Acres)
                .Select(x => x.Target)
                .FirstOrDefault();
            if (target is null)
            {
                break;
            }

            var landBefore = target.Land;
            var result = me.Attack(target, sim.Now, rules, sim.Random);
            if (!result.Report.Succeeded)
            {
                break;
            }

            sim.RecordAttack(this, target, landBefore - target.Land);
        }

        var knights = (me.Gold / 2) / UnitBook.Find("knights")!.GoldCost;
        knights = Math.Min(knights, me.Turns * rules.UnitsRecruitedPerTurn);
        if (knights > 0)
        {
            me.Recruit("knights", knights, sim.Now, rules);
        }

        Grow(me, sim);
    }
}

/// <summary>Signs up and never plays again.</summary>
public sealed class Idle(string name) : Bot(name, MagicSchool.Verdant)
{
    public override string Strategy => "signs up, never plays";

    public override void Play(Mage me, IReadOnlyList<Mage> others, Sim sim)
    {
    }
}
