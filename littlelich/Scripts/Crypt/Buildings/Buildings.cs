using System;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Lich;

namespace LittleLich.Crypt.Buildings;

/// <summary>
/// Mana Well (adjustment 02): clicking the well channels <see cref="ClickPower"/> mana at once; each assigned
/// necromancer adds <see cref="OutputPerNecromancer"/> mana per tick, times the level bonus and the
/// "mana_well_output" stat (base 1, for research and buffs). Places and level bonus come from the level table in
/// Data/buildings.json. Mana goes into Storage and stops at the cap; staff stay assigned.
/// </summary>
public sealed class ManaWell : BuildingBase
{
    /// <summary>Stat multiplying the well's output (base 1).</summary>
    public const string OutputStat = "mana_well_output";

    private long _carry;

    public ManaWell(CryptContext c) : base(BuildingIds.ManaWell, c)
    {
        c.Lich.Stats.SetBase(OutputStat, 1);
    }

    private ManaWellDefinition Def => (ManaWellDefinition)Definition;

    public string Output => Def.Output;

    /// <summary>Mana per click: click_power plus click_resource from research (Mage Hand).</summary>
    public double ClickPower => Def.ClickPower + Math.Max(0, Context.Lich.Stats.Get("click_resource").ToDouble());

    /// <summary>Mana per tick each assigned necromancer adds, before the level bonus and modifiers (Necro Mana adds to it).</summary>
    public double OutputPerNecromancer => WorkPerNecromancer;

    public override double WorkPerNecromancer =>
        Definition.WorkPerNecromancer + Context.Lich.Stats.PerNecromancer("mana_per_tick", Id);

    public long AssignedNecromancers => Staffed;

    /// <summary>The most necromancers the well holds at its level.</summary>
    public long MaxNecromancers => Slots;

    /// <summary>Research efficiency for the well, x mana_efficiency (Mana Springs), x the mana_well_output stat.</summary>
    public override double OutputModifier =>
        base.OutputModifier
        * Math.Max(0, Context.Lich.Stats.GetFor("mana_efficiency", Id, Num.One).ToDouble())
        * Math.Max(0, Context.Lich.Stats.Get(OutputStat).ToDouble());

    /// <summary>Mana per tick right now: necromancers x per necromancer x level bonus x modifiers.</summary>
    public double OutputPerTick() => WorkPerTick;

    /// <summary>
    /// Channel by hand: adds <see cref="ClickPower"/> mana now (fractions carry), up to the cap.
    /// Returns how much mana was added (0 when the store is full).
    /// </summary>
    public long Click()
    {
        var room = Context.Stock.Room(Output);
        var (ops, carry, limited) = FixedPoint.Run(_carry, FixedPoint.ToMicro(ClickPower), 1, FixedPoint.Micro, room);
        _carry = carry;
        if (ops > 0) Context.Stock.Add(Output, ops);
        if (limited) SetStatus(BuildingStatus.OutputFull, Output);
        return ops;
    }

    protected override void TickUnlocked(long ticks)
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

/// <summary>Infuser: mana into the element the player picks (fire, water, air or earth).</summary>
public sealed class Infuser : ConvertingBuilding
{
    public Infuser(CryptContext c) : base(BuildingIds.Infuser, c) => Element = Infusion.Elements.FirstOrDefault() ?? "";

    public InfuserDefinition Infusion => (InfuserDefinition)Definition;
    public double Progress => ProgressOf(Infusion.WorkPerOp);

    /// <summary>The element being infused (fire, water, air or earth). Saved.</summary>
    public string Element { get; private set; }

    /// <summary>Choose the element to infuse (progress resets). Throws for an element the Infuser cannot make.</summary>
    public void Select(string element)
    {
        if (!Infusion.Elements.Contains(element)) throw new ArgumentException($"The Infuser cannot make '{element}'.", nameof(element));
        if (element == Element) return;
        Element = element;
        ResetProgress();
    }

    /// <summary>Elements made per infusion: 1, or infuser_elemental_per_mana from research (Elemental Proficiency sets 2).</summary>
    public long ElementsPerOp => Math.Max(1, (long)Math.Floor(Context.Lich.Stats.GetFor("infuser_elemental_per_mana", Id, Num.One).ToDouble() + 1e-9));

    protected override void TickUnlocked(long ticks) =>
        Convert(ticks, new[] { Infusion.Input }, new ItemAmount { Id = Element, Count = ElementsPerOp }, Infusion.WorkPerOp);

    protected override void SaveExtra(SaveData d)
    {
        base.SaveExtra(d);
        d.Set("element", Element);
    }

    protected override void LoadExtra(SaveData d)
    {
        base.LoadExtra(d);
        var e = d.Get<string?>("element", null);
        Element = e is not null && Infusion.Elements.Contains(e) ? e : Infusion.Elements.FirstOrDefault() ?? "";
    }
}

/// <summary>Soul Well: elements into souls (fixed conversion from its definition: one of each element per soul).</summary>
public sealed class SoulWell : ConvertingBuilding
{
    public SoulWell(CryptContext c) : base(BuildingIds.SoulWell, c) { }
    public ConverterDefinition Conversion => (ConverterDefinition)Definition;
    public double Progress => ProgressOf(Conversion.WorkPerOp);
    protected override void TickUnlocked(long ticks) => Convert(ticks, Conversion.Inputs, Conversion.Output, Conversion.WorkPerOp);
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

    protected override void TickUnlocked(long ticks) => Convert(ticks, Recipe?.Inputs, Recipe?.Output, Recipe?.Work ?? 1);

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

/// <summary>One line of the Undead Market's sell list.</summary>
public sealed record SellEntry(string Id, bool Enabled, long Reserve);

/// <summary>
/// Undead Market: each unit of work sells one unit from the sell list (in list order), keeping the reserve,
/// at the item's sell_price, for gold. Stops when gold are full (goods are not lost).
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

    /// <summary>Gold per unit: sell_price x the sell_price research for this item (Marrow Market), rounded down, never below sell_price.</summary>
    public long PriceOf(string id)
    {
        var b = StockIds.SellPriceOf(Context.Content, id);
        var m = Context.Lich.Stats.GetFor("sell_price", id, Num.One, includeUntargeted: false).ToDouble();
        return b <= 0 ? b : Math.Max(b, (long)Math.Floor(b * m + 1e-9));
    }

    /// <summary>Adds or updates a sell list line. Throws for ids that cannot be sold.</summary>
    public void SetSell(string id, bool enabled, long reserve)
    {
        if (PriceOf(id) <= 0) throw new ArgumentException($"'{id}' cannot be sold (no sell_price).", nameof(id));
        var e = new SellEntry(id, enabled, Math.Max(0, reserve));
        var i = _sell.FindIndex(x => x.Id == id);
        if (i >= 0) _sell[i] = e; else _sell.Add(e);
    }

    public SellEntry? Entry(string id) => _sell.FirstOrDefault(x => x.Id == id);

    protected override void TickUnlocked(long ticks)
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
        var goldRoom = Context.Storage.Room(Gold);
        long sold = 0;
        long gold = 0;
        foreach (var e in active)
        {
            if (budget - sold <= 0) break;
            var price = PriceOf(e.Id);
            var n = Math.Min(budget - sold, Math.Max(0, Context.Stock.Get(e.Id) - e.Reserve));
            n = Math.Min(n, (goldRoom - gold) / price);
            if (n <= 0) continue;
            Context.Stock.TryRemove(e.Id, n);
            sold += n;
            gold += n * price;
        }
        if (gold > 0)
        {
            Context.Storage.Add(Gold, gold);
            Context.Recap()?.AddGained("resource", Gold, gold);
        }
        if (sold < budget)
        {
            _carry = 0;
            var goldFull = Context.Storage.Room(Gold) < active.Min(e => PriceOf(e.Id));
            if (goldFull) Context.Recap()?.MarkCapped(Gold);
            SetStatus(goldFull ? BuildingStatus.OutputFull : BuildingStatus.NothingToDo, goldFull ? Gold : null);
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

    protected override void TickUnlocked(long ticks) { }

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
