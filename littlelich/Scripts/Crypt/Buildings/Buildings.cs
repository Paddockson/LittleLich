using System;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Lich;

namespace LittleLich.Crypt.Buildings;

/// <summary>
/// Mana Well: one unit of work makes one mana. Has base work, so it produces with no necromancers
/// (a new game starts with none). A full mana store stops it; staff stay assigned.
/// </summary>
public sealed class ManaWell : BuildingBase
{
    private long _carry;

    public ManaWell(CryptContext c) : base(BuildingIds.ManaWell, c) { }

    public string Output => ((ManaWellDefinition)Definition).Output;

    public override void Tick(long ticks)
    {
        if (ticks <= 0) return;
        var room = Context.Stock.Room(Output);
        var (ops, carry, limited) = FixedPoint.Run(_carry, WorkMicroPerTick, ticks, FixedPoint.Micro, room);
        _carry = carry;
        if (ops > 0)
        {
            Context.Stock.Add(Output, ops);
            Context.Recap()?.AddGained("resource", Output, ops);
        }
        if (limited)
        {
            Context.Recap()?.MarkCapped(Output);
            SetStatus(BuildingStatus.OutputFull, Output);
        }
        else SetStatus(WorkMicroPerTick > 0 ? BuildingStatus.Running : BuildingStatus.Idle);
    }

    protected override void SaveExtra(SaveData d) => d.Set("carry", _carry);
    protected override void LoadExtra(SaveData d) => _carry = Math.Clamp(d.Get("carry", 0L), 0, FixedPoint.Micro - 1);
}

/// <summary>
/// Shared maths for buildings that turn inputs into an output: ops = work / work per op, limited by inputs and output room.
/// When limited, progress resets and the status says why.
/// </summary>
public abstract class ConvertingBuilding : BuildingBase
{
    private long _carry;

    protected ConvertingBuilding(string id, CryptContext c) : base(id, c) { }

    protected void Convert(long ticks, IReadOnlyList<ItemAmount>? inputs, ItemAmount? output, double workPerOp)
    {
        if (ticks <= 0) return;
        if (inputs is null || output is null)
        {
            _carry = 0;
            SetStatus(BuildingStatus.NothingToDo);
            return;
        }
        var work = WorkMicroPerTick;
        if (work == 0)
        {
            SetStatus(BuildingStatus.Idle);   // unstaffed with no base work: nothing to report missing
            return;
        }
        var (can, limiting) = Affordable(inputs);
        var outRoom = Context.Stock.Room(output.Id) / output.Count;
        var (ops, carry, limited) = FixedPoint.Run(_carry, work, ticks, FixedPoint.ToMicro(workPerOp), Math.Min(can, outRoom));
        _carry = carry;
        if (ops > 0)
        {
            foreach (var i in inputs) Context.Stock.TryRemove(i.Id, MulClamp(ops, i.Count));
            var made = Context.Stock.Add(output.Id, MulClamp(ops, output.Count));
            Context.Recap()?.AddGained(Context.Stock.KindOf(output.Id), output.Id, made);
        }
        if (limited)
        {
            if (can <= outRoom) SetStatus(BuildingStatus.MissingInput, limiting);
            else
            {
                Context.Recap()?.MarkCapped(output.Id);
                SetStatus(BuildingStatus.OutputFull, output.Id);
            }
        }
        else SetStatus(work > 0 ? BuildingStatus.Running : BuildingStatus.Idle);
    }

    /// <summary>0..1 progress toward the next operation (for the page's progress bar).</summary>
    protected double ProgressOf(double workPerOp) => workPerOp > 0 ? Math.Clamp(_carry / (double)FixedPoint.ToMicro(workPerOp), 0, 1) : 0;

    protected void ResetProgress() => _carry = 0;

    protected override void SaveExtra(SaveData d) => d.Set("carry", _carry);
    protected override void LoadExtra(SaveData d) => _carry = Math.Max(0, d.Get("carry", 0L));
}

/// <summary>Infuser: mana into infused mana (fixed conversion from its definition).</summary>
public sealed class Infuser : ConvertingBuilding
{
    public Infuser(CryptContext c) : base(BuildingIds.Infuser, c) { }
    public ConverterDefinition Conversion => (ConverterDefinition)Definition;
    public double Progress => ProgressOf(Conversion.WorkPerOp);
    public override void Tick(long ticks) => Convert(ticks, Conversion.Inputs, Conversion.Output, Conversion.WorkPerOp);
}

/// <summary>Soul Well: infused mana into souls (fixed conversion from its definition).</summary>
public sealed class SoulWell : ConvertingBuilding
{
    public SoulWell(CryptContext c) : base(BuildingIds.SoulWell, c) { }
    public ConverterDefinition Conversion => (ConverterDefinition)Definition;
    public double Progress => ProgressOf(Conversion.WorkPerOp);
    public override void Tick(long ticks) => Convert(ticks, Conversion.Inputs, Conversion.Output, Conversion.WorkPerOp);
}

/// <summary>Transmuter: runs the one chosen resource conversion (v0.1: resources only).</summary>
public sealed class Transmuter : ConvertingBuilding
{
    public Transmuter(CryptContext c) : base(BuildingIds.Transmuter, c) { }

    public IReadOnlyList<TransmutationDefinition> Recipes => Context.Content.All<TransmutationDefinition>().ToList();

    public bool IsLocked(string recipeId) => Context.Unlocks.IsLocked(Unlocks.Transmutation, recipeId);
    public string? SelectedRecipe { get; private set; }
    public TransmutationDefinition? Recipe => SelectedRecipe is { } id ? Recipes.FirstOrDefault(r => r.Id == id) : null;
    public double Progress => Recipe is { } r ? ProgressOf(r.Work) : 0;

    /// <summary>Choose a recipe (null stops). Progress resets.</summary>
    public void Select(string? recipeId)
    {
        if (recipeId is not null && Recipes.All(r => r.Id != recipeId))
            throw new ArgumentException($"No transmutation '{recipeId}'.", nameof(recipeId));
        if (recipeId is not null && IsLocked(recipeId))
            throw new ArgumentException($"'{recipeId}' is locked behind research.", nameof(recipeId));
        if (recipeId == SelectedRecipe) return;
        SelectedRecipe = recipeId;
        ResetProgress();
        SetStatus(recipeId is null ? BuildingStatus.NothingToDo : BuildingStatus.Idle);
    }

    public override void Tick(long ticks) => Convert(ticks, Recipe?.Inputs, Recipe?.Output, Recipe?.Work ?? 1);

    protected override void SaveExtra(SaveData d)
    {
        base.SaveExtra(d);
        d.Set("recipe", SelectedRecipe);
    }

    protected override void LoadExtra(SaveData d)
    {
        base.LoadExtra(d);
        var id = d.Get<string?>("recipe", null);
        SelectedRecipe = id is not null && Recipes.Any(r => r.Id == id) ? id : null;
    }
}

/// <summary>
/// Summoning Circle: spends the summon's costs (mana) to raise necromancers into the free pool through
/// LichState.AddToPool. Stops at the pool max or at the player's target total (0 = no target).
/// Each level above 1 raises max necromancers (modifier source "building:summoning_circle").
/// v0.1 has one circle running the first summon recipe (necromancer); unit recipes come in phase 9.
/// </summary>
public sealed class SummoningCircle : BuildingBase
{
    public const string Source = "building:summoning_circle";
    private long _carry;
    private long _target;

    public SummoningCircle(CryptContext c) : base(BuildingIds.SummoningCircle, c) { }

    /// <summary>The first summon recipe not locked behind research (necromancer in v0.1).</summary>
    public SummonDefinition? Recipe => Context.Content.All<SummonDefinition>().FirstOrDefault(r => !Context.Unlocks.IsLocked(Unlocks.Summon, r.Id));

    /// <summary>Stop when the pool's total reaches this (0 = keep going to the max).</summary>
    public long Target
    {
        get => _target;
        set => _target = Math.Max(0, value);
    }

    public double Progress => Recipe is { } r ? Math.Clamp(_carry / (double)FixedPoint.ToMicro(r.Work), 0, 1) : 0;

    /// <summary>Summons per tick at the current work (ignores costs).</summary>
    public double SummonsPerTick => Recipe is { } r ? WorkPerTick / r.Work : 0;

    // ---- Unit mode (phase 9): keep one unit type's stack topped up to a target ----

    private long _unitCarry;

    /// <summary>The unit type the circle keeps topped up (null = summon necromancers).</summary>
    public string? UnitType { get; private set; }

    /// <summary>The stack size to keep the unit type at.</summary>
    public long UnitTarget { get; private set; }

    /// <summary>
    /// Switch the circle to keep <paramref name="typeId"/>'s roster stack at <paramref name="target"/> (null = back to
    /// necromancers). Progress resets. Throws for an unknown type.
    /// </summary>
    public void SetUnitTarget(string? typeId, long target)
    {
        if (typeId is not null && Context.Units?.Type(typeId) is null) throw new ArgumentException($"No unit type '{typeId}'.", nameof(typeId));
        UnitType = typeId;
        UnitTarget = Math.Max(0, target);
        _unitCarry = 0;
        SetStatus(BuildingStatus.Idle);
    }

    /// <summary>0..1 progress toward the next unit (unit mode).</summary>
    public double UnitProgress =>
        UnitType is { } t && Context.Units is { } u && u.Type(t) is not null
            ? Math.Clamp(_unitCarry / (double)FixedPoint.ToMicro(u.BaseOf(t).SummonWork), 0, 1) : 0;

    private void TickUnits(long ticks, Units.UnitBuilder units, string typeId)
    {
        if (units.Type(typeId) is null)
        {
            SetStatus(BuildingStatus.NothingToDo);
            return;
        }
        var room = Math.Max(0, UnitTarget - units.Count(typeId));
        var (can, limiting) = units.Affordable(typeId);
        if (units.IsBaseLocked(units.Type(typeId)!.Base)) can = 0;
        var work = WorkMicroPerTick;
        var (ops, carry, limited) = FixedPoint.Run(_unitCarry, work, ticks, FixedPoint.ToMicro(units.BaseOf(typeId).SummonWork), Math.Min(can, room));
        _unitCarry = carry;
        if (ops > 0)
        {
            var made = units.SummonUpTo(typeId, ops);
            Context.Recap()?.AddGained("units", typeId, made);
        }
        if (limited)
            SetStatus(room <= can ? BuildingStatus.PoolFull : BuildingStatus.MissingInput, room <= can ? "target" : limiting);
        else SetStatus(work > 0 ? BuildingStatus.Running : BuildingStatus.Idle);
    }

    public override void Tick(long ticks)
    {
        if (ticks <= 0) return;
        if (UnitType is { } unitType && Context.Units is { } units)
        {
            TickUnits(ticks, units, unitType);
            return;
        }
        if (Recipe is not { } r)
        {
            SetStatus(BuildingStatus.NothingToDo);
            return;
        }
        var pool = Context.Lich.Pool;
        var room = Math.Max(0, pool.Max - pool.Total);
        if (_target > 0) room = Math.Min(room, Math.Max(0, _target - pool.Total));
        var (can, limiting) = Affordable(r.Costs);
        var work = WorkMicroPerTick;
        var (ops, carry, limited) = FixedPoint.Run(_carry, work, ticks, FixedPoint.ToMicro(r.Work), Math.Min(can, room / r.Count));
        _carry = carry;
        if (ops > 0)
        {
            foreach (var cost in r.Costs) Context.Stock.TryRemove(cost.Id, MulClamp(ops, cost.Count));
            var added = Context.Lich.AddToPool(MulClamp(ops, r.Count));
            Context.Recap()?.AddGained("necromancers", "summoned", added);
        }
        if (limited)
            SetStatus(room / r.Count <= can ? BuildingStatus.PoolFull : BuildingStatus.MissingInput,
                room / r.Count <= can ? (_target > 0 && _target <= pool.Max ? "target" : "max") : limiting);
        else SetStatus(work > 0 ? BuildingStatus.Running : BuildingStatus.Idle);
    }

    protected override void OnLevelChanged()
    {
        var bonus = (Num)((Level - 1) * ((SummoningCircleDefinition)Definition).MaxNecromancersPerLevel);
        var stats = Context.Lich.Stats;
        var old = stats.Modifiers.Where(m => m.Source == Source).ToList();
        if (old.Count == 1 && old[0].Value == bonus) return;
        // Add before removing so Max never dips below the current total in between.
        if (bonus > Num.Zero) stats.Add(new Modifier(LichStats.MaxNecromancers, ModifierOp.Add, bonus, Source));
        foreach (var m in old) stats.RemoveWhere(x => ReferenceEquals(x, m));
    }

    protected override void SaveExtra(SaveData d) => d.Set("carry", _carry).Set("target", _target)
        .Set("unit_type", UnitType).Set("unit_target", UnitTarget).Set("unit_carry", _unitCarry);

    /// <summary>The Unit Builder loads first, so a unit type that no longer exists switches the circle back to necromancers.</summary>
    protected override void LoadExtra(SaveData d)
    {
        _carry = Math.Max(0, d.Get("carry", 0L));
        _target = Math.Max(0, d.Get("target", 0L));
        var type = d.Get<string?>("unit_type", null);
        UnitType = type is not null && Context.Units?.Type(type) is not null ? type : null;
        UnitTarget = UnitType is null ? 0 : Math.Max(0, d.Get("unit_target", 0L));
        _unitCarry = UnitType is null ? 0 : Math.Max(0, d.Get("unit_carry", 0L));
    }
}

/// <summary>One line of the Undead Market's sell list.</summary>
public sealed record SellEntry(string Id, bool Enabled, long Reserve);

/// <summary>
/// Undead Market: each unit of work sells one unit from the sell list (in list order), keeping the reserve,
/// at the item's sell_price, for coins. Stops when coins are full (goods are not lost).
/// </summary>
public sealed class UndeadMarket : BuildingBase
{
    private readonly List<SellEntry> _sell = new();
    private long _carry;

    public UndeadMarket(CryptContext c) : base(BuildingIds.UndeadMarket, c) { }

    public IReadOnlyList<SellEntry> SellList => _sell;

    /// <summary>Every resource and item with a price, for the page's list.</summary>
    public IEnumerable<string> Sellable =>
        Context.Content.All<ResourceDefinitionBase>().Where(r => r.SellPrice > 0).Select(r => r.Id)
            .Concat(Context.Content.All<ItemDefinitionBase>().Where(i => i.SellPrice > 0).Select(i => i.Id));

    public long PriceOf(string id) => StockIds.SellPriceOf(Context.Content, id);

    /// <summary>Adds or updates a sell list line. Throws for ids that cannot be sold.</summary>
    public void SetSell(string id, bool enabled, long reserve)
    {
        if (PriceOf(id) <= 0) throw new ArgumentException($"'{id}' cannot be sold (no sell_price).", nameof(id));
        var e = new SellEntry(id, enabled, Math.Max(0, reserve));
        var i = _sell.FindIndex(x => x.Id == id);
        if (i >= 0) _sell[i] = e; else _sell.Add(e);
    }

    public SellEntry? Entry(string id) => _sell.FirstOrDefault(x => x.Id == id);

    public override void Tick(long ticks)
    {
        if (ticks <= 0) return;
        var active = _sell.Where(e => e.Enabled).ToList();
        if (active.Count == 0)
        {
            _carry = 0;
            SetStatus(BuildingStatus.NothingToDo);
            return;
        }
        var work = WorkMicroPerTick;
        var progress = (Int128)_carry + (Int128)work * ticks;
        var budget = FixedPoint.Clamp(progress / FixedPoint.Micro);
        var coinRoom = Context.Storage.Room(ResourceIdsCoins);
        long sold = 0;
        long coins = 0;
        foreach (var e in active)
        {
            if (budget - sold <= 0) break;
            var price = PriceOf(e.Id);
            var n = Math.Min(budget - sold, Math.Max(0, Context.Stock.Get(e.Id) - e.Reserve));
            n = Math.Min(n, (coinRoom - coins) / price);
            if (n <= 0) continue;
            Context.Stock.TryRemove(e.Id, n);
            sold += n;
            coins += n * price;
        }
        if (coins > 0)
        {
            Context.Storage.Add(ResourceIdsCoins, coins);
            Context.Recap()?.AddGained("resource", ResourceIdsCoins, coins);
        }
        if (sold < budget)
        {
            _carry = 0;
            var coinsFull = Context.Storage.Room(ResourceIdsCoins) < active.Min(e => PriceOf(e.Id));
            if (coinsFull) Context.Recap()?.MarkCapped(ResourceIdsCoins);
            SetStatus(coinsFull ? BuildingStatus.OutputFull : BuildingStatus.NothingToDo, coinsFull ? ResourceIdsCoins : null);
        }
        else
        {
            _carry = (long)(progress % FixedPoint.Micro);
            SetStatus(work > 0 ? BuildingStatus.Running : BuildingStatus.Idle);
        }
    }

    protected override void SaveExtra(SaveData d) =>
        d.Set("carry", _carry).Set("sell", _sell.Select(e => new SellDto { Id = e.Id, Enabled = e.Enabled, Reserve = e.Reserve }).ToList());

    protected override void LoadExtra(SaveData d)
    {
        _carry = Math.Clamp(d.Get("carry", 0L), 0, FixedPoint.Micro - 1);
        _sell.Clear();
        foreach (var s in d.Get("sell", new List<SellDto>()))
            if (s.Id is { } id && PriceOf(id) > 0 && _sell.All(x => x.Id != id))
                _sell.Add(new SellEntry(id, s.Enabled, Math.Max(0, s.Reserve)));
    }

    public sealed class SellDto
    {
        public string? Id { get; set; }
        public bool Enabled { get; set; }
        public long Reserve { get; set; }
    }
}

/// <summary>Magical Storage: no work and no staff; each level raises every resource cap and the inventory space.</summary>
public sealed class MagicalStorage : BuildingBase
{
    public MagicalStorage(CryptContext c) : base(BuildingIds.MagicalStorage, c) => SetStatus(BuildingStatus.Passive);

    public override bool Ticks => false;

    public double CapMultiplier => 1 + ((MagicalStorageDefinition)Definition).CapBonusPerLevel * (Level - 1);

    public override void Tick(long ticks) { }

    public long InventorySlots
    {
        get
        {
            var d = (MagicalStorageDefinition)Definition;
            return d.BaseInventorySlots + d.InventorySlotsPerLevel * (Level - 1);
        }
    }

    protected override void OnLevelChanged()
    {
        Context.Storage.CapMultiplier = CapMultiplier;
        Context.Stock.Inventory.SlotCapacity = InventorySlots;
    }
}

public static class BuildingFactory
{
    /// <summary>All seven buildings in flow order (Magical Storage last; it does not tick).</summary>
    public static IReadOnlyList<BuildingBase> CreateAll(CryptContext c) => new BuildingBase[]
    {
        new ManaWell(c), new Infuser(c), new SoulWell(c), new Transmuter(c), new SummoningCircle(c), new UndeadMarket(c), new MagicalStorage(c),
    };
}
