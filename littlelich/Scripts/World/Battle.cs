using System;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Content.Definitions;

namespace LittleLich.World;

public enum BattleSide { Attacker, Defender }

public enum BattleOutcome
{
    /// <summary>Every defender is dead.</summary>
    Victory,
    /// <summary>Every attacker is dead, or the time limit ran out.</summary>
    Defeat,
    /// <summary>The player pulled the army back.</summary>
    Retreat,
}

/// <summary>
/// Units of one type in one section. HP is pooled: the group shows one HP bar, and the number still standing
/// is ceil(hp / unit hp). Actions scale with the units still standing.
/// </summary>
public sealed class BattleGroup
{
    internal BattleGroup(BattleSide side, string section, BattleUnit unit, long count, long capacity, int slot, int order)
    {
        Side = side;
        Section = section;
        Unit = unit;
        StartCount = count;
        Capacity = Math.Max(count, capacity);
        Slot = slot;
        Hp = count * unit.Hp;
        Order = order;
        NextFireMs = unit.Actions.Select(a => ToMs(a.Cooldown)).ToArray();
    }

    public BattleSide Side { get; }
    public string Section { get; }
    public BattleUnit Unit { get; }
    /// <summary>Units at the start of the battle.</summary>
    public long StartCount { get; }
    /// <summary>Most units the group can hold (a player squad: 5; otherwise the start count). The HP bar is out of this.</summary>
    public long Capacity { get; }
    /// <summary>The army formation slot this group fights for (player squads), or -1.</summary>
    public int Slot { get; }
    /// <summary>Units added during the battle (the lich's replacements).</summary>
    public long Reinforced { get; private set; }
    public double MaxHp => Capacity * Unit.Hp;
    /// <summary>Healing tops up to the units fielded so far (start + replacements), never beyond.</summary>
    internal double HealCap => Math.Min(MaxHp, (StartCount + Reinforced) * Unit.Hp);
    public double Hp { get; internal set; }
    public bool IsAlive => Hp > 0;

    /// <summary>Units still standing.</summary>
    public long Alive => Hp <= 0 ? 0 : Math.Min(Capacity, (long)Math.Ceiling(Hp / Unit.Hp - 1e-9));

    /// <summary>Adds up to <paramref name="count"/> fresh units (full HP each), within <see cref="Capacity"/>. Returns how many.</summary>
    internal long AddUnits(long count)
    {
        var n = Math.Min(count, Capacity - Alive);
        if (n <= 0) return 0;
        Hp = Math.Min(MaxHp, Hp + n * Unit.Hp);
        Reinforced += n;
        return n;
    }

    /// <summary>Damage reduction for the next incoming attack (0 = none), set by a brace action.</summary>
    public double Brace { get; internal set; }

    /// <summary>Battle time (ms) each action fires next, in action order.</summary>
    public long[] NextFireMs { get; }

    internal int Order { get; }

    internal static long ToMs(double seconds) => (long)Math.Round(seconds * 1000);
}

/// <summary>
/// One group to put in a battle: section, unit and how many. A <paramref name="Capacity"/> above the count lets the
/// group be refilled during the fight (0 = the count); <paramref name="Slot"/> is the army formation slot it fights for.
/// </summary>
public sealed record BattleEntry(string Section, BattleUnit Unit, long Count, long Capacity = 0, int Slot = -1);

/// <summary>What happened when an action fired (for the battle log and UI).</summary>
public sealed record BattleAction(long AtMs, BattleGroup Source, BattleUnitAction Action, BattleGroup? Target, double Amount, bool Braced);

/// <summary>
/// One fight, created per battle by the WorldManager. Runs on its own clock in whole milliseconds, separate from
/// the 2 s tick (the TickManager passes elapsed game seconds while the game is not paused).
///
/// Each group's actions fire every cooldown (first after one cooldown). Within an Advance, actions fire in time order;
/// ties go to the attacker, then front to back, then group order, then action order, so Advance(10 s) equals
/// ten Advance(1 s). Melee actions are only usable from the front-most living section of their side; an action that is
/// due but unusable waits and fires as soon as it can.
/// Damage = value x units standing; a target with reduce_next_incoming (brace) takes (1 - brace) of it and the brace is used up.
/// The battle ends when one side has no one standing, on retreat, or at the time limit (counted as a defeat).
/// </summary>
public sealed class BattleController
{
    public const long DefaultTimeLimitMs = 300_000;

    private readonly List<BattleGroup> _groups = new();
    private double _carryMs;

    public BattleController(
        IEnumerable<(string Section, BattleUnit Unit, long Count)> attackers,
        IEnumerable<(string Section, BattleUnit Unit, long Count)> defenders,
        long timeLimitMs = DefaultTimeLimitMs)
        : this(attackers.Select(a => new BattleEntry(a.Section, a.Unit, a.Count)), defenders.Select(d => new BattleEntry(d.Section, d.Unit, d.Count)), timeLimitMs)
    {
    }

    /// <summary>Entries with a capacity above their count (player squads) can be refilled with <see cref="Reinforce"/>.</summary>
    public BattleController(IEnumerable<BattleEntry> attackers, IEnumerable<BattleEntry> defenders, long timeLimitMs = DefaultTimeLimitMs)
    {
        TimeLimitMs = timeLimitMs;
        Add(BattleSide.Attacker, attackers);
        Add(BattleSide.Defender, defenders);
        if (!SideAlive(BattleSide.Attacker) || !SideAlive(BattleSide.Defender))
            throw new ArgumentException("Both sides need at least one unit.");
    }

    public long TimeLimitMs { get; }
    public long NowMs { get; private set; }
    public double NowSeconds => NowMs / 1000.0;
    public BattleOutcome? Outcome { get; private set; }
    public bool IsOver => Outcome is not null;

    /// <summary>Every group, attacker first, front to back.</summary>
    public IReadOnlyList<BattleGroup> Groups => _groups;
    public IEnumerable<BattleGroup> GroupsOf(BattleSide side) => _groups.Where(g => g.Side == side);

    public event Action<BattleAction>? ActionFired;
    public event Action<BattleOutcome>? Ended;

    /// <summary>Advance by game seconds (fractions of a millisecond carry over).</summary>
    public void Advance(double seconds)
    {
        if (!(seconds > 0) || IsOver) return;
        _carryMs += seconds * 1000;
        var ms = (long)Math.Floor(_carryMs);
        _carryMs -= ms;
        AdvanceMs(ms);
    }

    public void AdvanceMs(long ms)
    {
        if (ms <= 0 || IsOver) return;
        var target = Math.Min(NowMs + ms, TimeLimitMs);
        while (!IsOver)
        {
            var next = NextDue(target);
            if (next is null) break;
            var (group, index, at) = next.Value;
            NowMs = Math.Max(NowMs, at);
            Fire(group, index);
            CheckEnd();
        }
        if (IsOver) return;
        NowMs = target;
        if (NowMs >= TimeLimitMs) End(BattleOutcome.Defeat);
    }

    /// <summary>Pull the army back now (survivors keep their current numbers).</summary>
    public void Retreat()
    {
        if (!IsOver) End(BattleOutcome.Retreat);
    }

    /// <summary>The attacker group fighting for an army slot, if any.</summary>
    public BattleGroup? SlotGroup(int slot) => slot < 0 ? null : GroupsOf(BattleSide.Attacker).FirstOrDefault(g => g.Slot == slot);

    /// <summary>The lich's replacements: adds up to <paramref name="count"/> units to a slot's group. Returns how many.</summary>
    public long Reinforce(int slot, long count) => IsOver || SlotGroup(slot) is not { } g ? 0 : g.AddUnits(count);

    /// <summary>Units that took part per unit id on one side (start count plus replacements).</summary>
    public Dictionary<string, long> Fielded(BattleSide side) =>
        GroupsOf(side).GroupBy(g => g.Unit.Id).ToDictionary(g => g.Key, g => g.Sum(x => x.StartCount + x.Reinforced));

    /// <summary>Units still standing per unit id on one side.</summary>
    public Dictionary<string, long> Survivors(BattleSide side) =>
        GroupsOf(side).GroupBy(g => g.Unit.Id).ToDictionary(g => g.Key, g => g.Sum(x => x.Alive));

    public double SideHp(BattleSide side) => GroupsOf(side).Sum(g => g.Hp);
    public double SideMaxHp(BattleSide side) => GroupsOf(side).Sum(g => g.MaxHp);

    /// <summary>The front-most section with someone standing on that side.</summary>
    public string? FrontSection(BattleSide side) =>
        Sections.All.FirstOrDefault(s => GroupsOf(side).Any(g => g.Section == s && g.IsAlive));

    // ---- internals ----

    private void Add(BattleSide side, IEnumerable<BattleEntry> units)
    {
        foreach (var e in units
                     .Where(u => u.Count > 0 || u.Capacity > 0)
                     .OrderBy(u => Sections.All.ToList().IndexOf(u.Section)))
        {
            if (!Sections.All.Contains(e.Section)) throw new ArgumentException($"'{e.Section}' is not a section.");
            _groups.Add(new BattleGroup(side, e.Section, e.Unit, Math.Max(0, e.Count), e.Capacity, e.Slot, _groups.Count));
        }
    }

    private bool SideAlive(BattleSide side) => GroupsOf(side).Any(g => g.IsAlive);

    private bool Usable(BattleGroup g, BattleUnitAction a) => g.IsAlive && (!a.IsMelee || FrontSection(g.Side) == g.Section);

    /// <summary>The earliest usable action due at or before <paramref name="limit"/> (stable tie order).</summary>
    private (BattleGroup Group, int Action, long At)? NextDue(long limit)
    {
        (BattleGroup, int, long)? best = null;
        foreach (var g in _groups)
        {
            if (!g.IsAlive) continue;
            for (var i = 0; i < g.Unit.Actions.Count; i++)
            {
                var at = g.NextFireMs[i];
                if (at > limit || !Usable(g, g.Unit.Actions[i])) continue;
                var effective = Math.Max(at, NowMs);
                if (best is null || effective < best.Value.Item3) best = (g, i, effective);
            }
        }
        return best;
    }

    private void Fire(BattleGroup g, int index)
    {
        var a = g.Unit.Actions[index];
        g.NextFireMs[index] = NowMs + BattleGroup.ToMs(a.Cooldown);
        var target = PickTarget(g, a);
        double amount = 0;
        var braced = false;
        switch (a.Effect)
        {
            case "damage" when target is not null:
                amount = a.Value * g.Alive;
                if (target.Brace > 0)
                {
                    amount *= 1 - target.Brace;
                    target.Brace = 0;
                    braced = true;
                }
                target.Hp = Math.Max(0, target.Hp - amount);
                break;
            case "reduce_next_incoming":
                target = g;
                g.Brace = Math.Max(g.Brace, a.Value);
                amount = a.Value;
                break;
            case "heal" when target is not null:
                var before = target.Hp;
                target.Hp = Math.Max(before, Math.Min(target.HealCap, target.Hp + a.Value * g.Alive));
                amount = target.Hp - before;
                break;
        }
        ActionFired?.Invoke(new BattleAction(NowMs, g, a, target, amount, braced));
    }

    private BattleGroup? PickTarget(BattleGroup g, BattleUnitAction a)
    {
        var enemy = g.Side == BattleSide.Attacker ? BattleSide.Defender : BattleSide.Attacker;
        var foes = GroupsOf(enemy).Where(x => x.IsAlive).ToList();
        var friends = GroupsOf(g.Side).Where(x => x.IsAlive).ToList();
        return a.Target switch
        {
            "self" => g,
            "front" => FrontSection(enemy) is { } s ? foes.First(x => x.Section == s) : null,
            "back" => foes.LastOrDefault(),
            "lowest" => foes.OrderBy(x => x.Hp / x.MaxHp).ThenBy(x => x.Order).FirstOrDefault(),
            "ally_lowest" => friends.OrderBy(x => x.Hp / x.MaxHp).ThenBy(x => x.Order).FirstOrDefault(),
            _ => null,
        };
    }

    private void CheckEnd()
    {
        if (!SideAlive(BattleSide.Defender)) End(BattleOutcome.Victory);
        else if (!SideAlive(BattleSide.Attacker)) End(BattleOutcome.Defeat);
    }

    private void End(BattleOutcome outcome)
    {
        Outcome = outcome;
        Ended?.Invoke(outcome);
    }
}
