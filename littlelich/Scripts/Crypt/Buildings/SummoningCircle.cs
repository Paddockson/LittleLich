using System;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Lich;

namespace LittleLich.Crypt.Buildings;

/// <summary>
/// Something the circle can summon (adjustment 01): a necromancer summon (Data/recipes/summoning.json, joins the
/// free pool) or a unit (a Unit Builder type or a bare player body, joins the roster). Only unlocked options exist.
/// </summary>
public sealed record UnitOption(string Id, string Name, string Description, IReadOnlyList<ItemAmount> Cost, double Required, long Count, bool IsNecromancer);

/// <summary>One summon slot: the unit it summons and its bar (progress toward <see cref="Required"/> power).</summary>
public sealed class SummonSlot
{
    internal SummonSlot(int index) => Index = index;

    public int Index { get; }

    /// <summary>The chosen unit option id; null when empty (the UI shows PICK).</summary>
    public string? UnitId { get; internal set; }

    /// <summary>Progress in micro-power (exact, for batching).</summary>
    internal long ProgressMicro { get; set; }

    /// <summary>Power gathered so far (0 to <see cref="Required"/>).</summary>
    public double Progress => ProgressMicro / (double)FixedPoint.Micro;

    /// <summary>Power needed per summon, from the unit's summon_required (0 when empty).</summary>
    public double Required { get; internal set; }

    /// <summary>0..1 bar fill.</summary>
    public double Fraction => Required > 0 ? Math.Clamp(Progress / Required, 0, 1) : 0;

    public bool IsEmpty => UnitId is null;

    /// <summary>Why a full bar is waiting: a missing resource or item id, <see cref="SummoningCircle.PoolFull"/>, or null.</summary>
    public string? WaitingFor { get; internal set; }
}

/// <summary>
/// Summoning Circle (adjustment 01). The circle has summon slots; each slot summons one chosen unit. Power fills the
/// bars: clicking the circle adds <see cref="ClickPower"/> to the first slot that is not full, and assigned
/// necromancers add power every tick, split evenly across the non-empty slots. A full bar spends the unit's
/// summon_cost and summons one; a slot that cannot pay (or whose necromancers have no room in the pool) keeps its bar
/// full and waits. Summoned necromancers join the free pool; units join the Unit Builder roster.
///
/// Only unlocked slots and units exist: slots come from slots_by_level plus research (unlock_summon_slot); units
/// from summon_level, research locks and the Unit Builder. Each level above 1 also raises max necromancers
/// (modifier source "building:summoning_circle").
/// </summary>
public sealed class SummoningCircle : BuildingBase
{
    public const string Source = "building:summoning_circle";

    /// <summary><see cref="SummonSlot.WaitingFor"/> when the necromancer pool is at its max.</summary>
    public const string PoolFull = "pool";

    private readonly List<SummonSlot> _slots = new();

    public SummoningCircle(CryptContext c) : base(BuildingIds.SummoningCircle, c)
    {
        // Research adds summon slots through a stat (Expanded Minions, Summoning Capacity).
        c.Lich.Stats.StatChanged += (stat, _) => { if (stat.StartsWith(SlotStat, StringComparison.Ordinal)) SyncSlots(); };
        SyncSlots();
        ApplyDefaults();
        // Research opens the necromancer summon after the circle exists: put it in the first slot then.
        c.Unlocks.Unlocked += (kind, id) => { if (kind == Unlocks.Unit && c.Content.Contains<SummonDefinitionBase>(id)) ApplyDefaults(); };
    }

    private SummoningCircleDefinition Def => (SummoningCircleDefinition)Definition;

    /// <summary>Power one click adds.</summary>
    public double ClickPower => Def.ClickPower;

    /// <summary>Power each assigned necromancer adds per tick, after the level bonus.</summary>
    public double PowerPerNecromancerPerTick => Definition.WorkPerNecromancer * LevelMultiplier * OutputModifier;

    /// <summary>The circle opens with the Summoning research (feature "summoning").</summary>
    public override (string Kind, string Id) UnlockKey => (Unlocks.Feature, Unlocks.SummoningFeature);

    /// <summary>Research efficiency x summoning_efficiency (Summoning Power).</summary>
    public override double OutputModifier =>
        base.OutputModifier * Math.Max(0, Context.Lich.Stats.GetFor("summoning_efficiency", Id, Num.One).ToDouble());

    private const string SlotStat = "summon_slots";

    /// <summary>Necromancer power per tick in total (before it is split across slots).</summary>
    public double PowerPerTick => WorkPerTick;

    public long AssignedNecromancers => Staffed;

    /// <summary>The unlocked slots (there are no locked placeholders).</summary>
    public IReadOnlyList<SummonSlot> SummonSlots => _slots;

    /// <summary>How many slots are unlocked: slots_by_level at this level plus research.</summary>
    public int UnlockedSlotCount =>
        Math.Max(0, Def.SlotsAt(Level) + (int)Math.Floor(Context.Lich.Stats.GetFor(SlotStat, Id, Num.Zero, includeUntargeted: false).ToDouble() + 1e-9));

    /// <summary>The slot count or a slot's unit changed.</summary>
    public event Action? SlotsChanged;

    /// <summary>(slot, unit option id, how many summons) after a slot summons.</summary>
    public event Action<SummonSlot, string, long>? Summoned;

    // ---- What can be summoned ----

    /// <summary>
    /// Unlocked options only, in content order: necromancer summons, then each unlocked player body (bare) followed
    /// by its Unit Builder loadouts. Unlocked = circle level >= summon_level and not locked by research.
    /// </summary>
    public IReadOnlyList<UnitOption> AvailableUnits()
    {
        var list = new List<UnitOption>();
        foreach (var s in Context.Content.All<SummonDefinitionBase>())
            if (s.SummonLevel <= Level && !Context.Unlocks.IsLocked(Unlocks.Unit, s.Id))
                list.Add(new UnitOption(s.Id, s.Name, s.Description, NecromancerCost(s.SummonCost), s.SummonRequired, s.Count, true));
        if (Context.Units is not { } u) return list;
        foreach (var b in u.Bases)
        {
            if (b.SummonLevel > Level || u.IsBaseLocked(b.Id) || list.Any(o => o.Id == b.Id)) continue;
            list.Add(new UnitOption(b.Id, b.Name, b.Description, u.BaseCost(b.Id), b.SummonRequired, 1, false));
            foreach (var t in u.Types.Where(t => t.Base == b.Id && t.Id != b.Id))
                list.Add(new UnitOption(t.Id, t.Name, b.Description, u.CostPerUnit(t.Id), b.SummonRequired, 1, false));
        }
        return list;
    }

    public UnitOption? Option(string id) => AvailableUnits().FirstOrDefault(o => o.Id == id);

    /// <summary>Necromancer costs x necromancer_summon_cost (Dark Recruitment x0.75, Dark Efficiency x0.5), rounded up.</summary>
    private IReadOnlyList<ItemAmount> NecromancerCost(IReadOnlyList<ItemAmount> cost)
    {
        var m = Math.Max(0, Context.Lich.Stats.GetFor("necromancer_summon_cost", Id, Num.One).ToDouble());
        return cost.Select(c => new ItemAmount { Id = c.Id, Count = ResearchStats.Scaled(c.Count, m) }).ToList();
    }

    // ---- Commands ----

    /// <summary>Choose the unit a slot summons (an id from <see cref="AvailableUnits"/>). Progress restarts. False if invalid.</summary>
    public bool SetUnit(int slotIndex, string unitId)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Count || Option(unitId) is not { } opt) return false;
        var slot = _slots[slotIndex];
        if (slot.UnitId == unitId) return true;
        slot.UnitId = unitId;
        slot.Required = opt.Required;
        slot.ProgressMicro = 0;
        slot.WaitingFor = null;
        SlotsChanged?.Invoke();
        return true;
    }

    /// <summary>Empty a slot (its progress is lost).</summary>
    public void ClearSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Count || _slots[slotIndex].IsEmpty) return;
        Empty(_slots[slotIndex]);
        SlotsChanged?.Invoke();
    }

    /// <summary>
    /// One click on the circle: adds <see cref="ClickPower"/> to the first non-empty slot whose bar is not full
    /// (a full, waiting slot passes the click on). A bar that fills summons at once if it can pay.
    /// Returns false when no slot took the click.
    /// </summary>
    public bool Click()
    {
        var power = FixedPoint.ToMicro(ClickPower);
        if (power <= 0) return false;
        foreach (var (slot, opt) in Active(AvailableUnits()))
        {
            var req = FixedPoint.ToMicro(opt.Required);
            if (slot.ProgressMicro >= req) continue;
            slot.ProgressMicro = Math.Min(req, slot.ProgressMicro + power);
            if (slot.ProgressMicro >= req) Resolve(slot, opt, slot.ProgressMicro, req);
            return true;
        }
        return false;
    }

    // ---- Tick ----

    /// <summary>
    /// Necromancer power per tick is split evenly (integer micro-power) across the non-empty slots; each slot runs
    /// floor(progress / required) summons, limited by stock and pool room. Batched: Tick(n) equals n x Tick(1)
    /// for each slot on its own (slots sharing scarce stock are served in slot order within a batch).
    /// </summary>
    protected override void TickUnlocked(long ticks)
    {
        if (ticks <= 0) return;
        var active = Active(AvailableUnits());
        if (active.Count == 0)
        {
            SetStatus(BuildingStatus.NothingToDo);
            return;
        }
        var share = WorkMicroPerTick / active.Count;
        foreach (var (slot, opt) in active)
        {
            var progress = (Int128)slot.ProgressMicro + (Int128)share * ticks;
            Resolve(slot, opt, progress, FixedPoint.ToMicro(opt.Required));
        }
        var waiting = active.Select(a => a.Slot.WaitingFor).FirstOrDefault(w => w is not null);
        if (waiting == PoolFull) SetStatus(BuildingStatus.PoolFull, "max");
        else if (waiting is not null) SetStatus(BuildingStatus.MissingInput, waiting);
        else SetStatus(share > 0 ? BuildingStatus.Running : BuildingStatus.Idle);
    }

    /// <summary>Summons what <paramref name="progress"/> pays for; leftover carries, or the bar stays full and waits.</summary>
    private void Resolve(SummonSlot slot, UnitOption opt, Int128 progress, long req)
    {
        var possible = FixedPoint.Clamp(progress / req);
        if (possible == 0)
        {
            slot.ProgressMicro = (long)progress;
            slot.WaitingFor = null;
            return;
        }
        var (max, why) = Capacity(opt);
        var made = Summon(slot, opt, Math.Min(possible, max));
        if (made < possible)
        {
            slot.ProgressMicro = req;
            slot.WaitingFor = why ?? opt.Cost.FirstOrDefault()?.Id ?? PoolFull;
        }
        else
        {
            slot.ProgressMicro = (long)(progress - (Int128)made * req);
            slot.WaitingFor = null;
        }
    }

    /// <summary>How many summons stock and pool room allow, and what limits it.</summary>
    private (long Max, string? Why) Capacity(UnitOption opt)
    {
        long max = long.MaxValue;
        string? why = null;
        foreach (var c in opt.Cost)
        {
            var n = Context.Stock.Get(c.Id) / c.Count;
            if (n < max) { max = n; why = c.Id; }
        }
        if (opt.IsNecromancer)
        {
            var pool = Context.Lich.Pool;
            var room = Math.Max(0, pool.Max - pool.Total) / opt.Count;
            if (room < max) { max = room; why = PoolFull; }
        }
        return (max, why);
    }

    /// <summary>Pays and summons up to <paramref name="count"/>. Returns how many summons happened.</summary>
    private long Summon(SummonSlot slot, UnitOption opt, long count)
    {
        if (count <= 0) return 0;
        long made;
        if (opt.IsNecromancer)
        {
            foreach (var c in opt.Cost) Context.Stock.TryRemove(c.Id, MulClamp(count, c.Count));
            var added = Context.Lich.AddToPool(MulClamp(count, opt.Count));
            Context.Recap()?.AddGained("necromancers", "summoned", added);
            made = count;
        }
        else
        {
            if (Context.Units is not { } u) return 0;
            if (u.Type(opt.Id) is null) u.EnsureBare(opt.Id);
            made = u.SummonUpTo(opt.Id, count);
            if (made > 0) Context.Recap()?.AddGained("units", opt.Id, made);
        }
        if (made > 0) Summoned?.Invoke(slot, opt.Id, made);
        return made;
    }

    /// <summary>Non-empty slots with their option. A slot whose unit is no longer available is emptied.</summary>
    private List<(SummonSlot Slot, UnitOption Opt)> Active(IReadOnlyList<UnitOption> options)
    {
        var list = new List<(SummonSlot, UnitOption)>();
        var emptied = false;
        foreach (var s in _slots)
        {
            if (s.UnitId is null) continue;
            var opt = options.FirstOrDefault(o => o.Id == s.UnitId);
            if (opt is null) { Empty(s); emptied = true; continue; }
            s.Required = opt.Required;
            list.Add((s, opt));
        }
        if (emptied) SlotsChanged?.Invoke();
        return list;
    }

    private static void Empty(SummonSlot s)
    {
        s.UnitId = null;
        s.Required = 0;
        s.ProgressMicro = 0;
        s.WaitingFor = null;
    }

    // ---- Slots and level ----

    /// <summary>Grow (or, after a load at a lower level, shrink) the slot list to the unlocked count.</summary>
    private void SyncSlots()
    {
        var want = UnlockedSlotCount;
        if (want == _slots.Count) return;
        while (_slots.Count < want) _slots.Add(new SummonSlot(_slots.Count));
        if (_slots.Count > want) _slots.RemoveRange(want, _slots.Count - want);
        SlotsChanged?.Invoke();
    }

    /// <summary>New game, or when research first opens the necromancer: an all-empty circle summons necromancers in slot 1.</summary>
    private void ApplyDefaults()
    {
        if (_slots.Count == 0 || _slots.Any(s => !s.IsEmpty)) return;
        if (AvailableUnits().FirstOrDefault(o => o.IsNecromancer) is { } necro) SetUnit(0, necro.Id);
    }

    protected override void OnLevelChanged()
    {
        SyncSlots();
        var bonus = (Num)((Level - 1) * Def.MaxNecromancersPerLevel);
        var stats = Context.Lich.Stats;
        var old = stats.Modifiers.Where(m => m.Source == Source).ToList();
        if (old.Count == 1 && old[0].Value == bonus) return;
        // Add before removing so Max never dips below the current total in between.
        if (bonus > Num.Zero) stats.Add(new Modifier(LichStats.MaxNecromancers, ModifierOp.Add, bonus, Source));
        foreach (var m in old) stats.RemoveWhere(x => ReferenceEquals(x, m));
    }

    // ---- Save ----

    protected override void SaveExtra(SaveData d) =>
        d.Set("slots", _slots.Select(s => new SlotDto { Unit = s.UnitId, Progress = s.ProgressMicro }).ToList());

    /// <summary>
    /// Research and the Unit Builder load first, so unavailable units are dropped. Saves from before adjustment 01
    /// (no "slots") start with necromancers in slot 1 and the old unit type, if any, in slot 2.
    /// </summary>
    protected override void LoadExtra(SaveData d)
    {
        SyncSlots();
        foreach (var s in _slots) Empty(s);
        if (d.Has("slots"))
        {
            var saved = d.Get("slots", new List<SlotDto>());
            for (var i = 0; i < _slots.Count && i < saved.Count; i++)
            {
                if (saved[i].Unit is not { } id || Option(id) is not { } opt) continue;
                var s = _slots[i];
                s.UnitId = id;
                s.Required = opt.Required;
                s.ProgressMicro = Math.Clamp(saved[i].Progress, 0, FixedPoint.ToMicro(opt.Required));
            }
        }
        else
        {
            ApplyDefaults();
            if (d.Get<string?>("unit_type", null) is { } old && _slots.Count > 1) SetUnit(1, old);
        }
        SlotsChanged?.Invoke();
    }

    public sealed class SlotDto
    {
        public string? Unit { get; set; }
        public long Progress { get; set; }
    }
}
