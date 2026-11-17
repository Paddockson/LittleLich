using System;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Core.Ticks;

namespace LittleLich.Crypt.Buildings;

/// <summary>What a building needs from the rest of the game (plain objects, testable without Godot).</summary>
public sealed record CryptContext(LichState Lich, Stockpile Stock, ContentDatabase Content, Func<AwayRecap?> Recap)
{
    /// <summary>Research unlocks (phase 7): locked transmutations/summons and extra building levels.</summary>
    public Unlocks Unlocks { get; init; } = Unlocks.None;

    public Storage Storage => Stock.Storage;
}

public enum BuildingStatus
{
    /// <summary>No work this tick (no staff and no base work, or nothing chosen).</summary>
    Idle,
    Running,
    /// <summary>An input ran out (see <see cref="BuildingBase.StatusDetail"/>).</summary>
    MissingInput,
    /// <summary>The output is at its cap.</summary>
    OutputFull,
    /// <summary>No recipe chosen (Transmuter) or nothing to sell (Market).</summary>
    NothingToDo,
    /// <summary>Summoning Circle: the necromancer pool is at its max, or the target is reached.</summary>
    PoolFull,
    /// <summary>Does not tick (Magical Storage).</summary>
    Passive,
}

/// <summary>
/// A Workshop building: a level bought with coins, and necromancer slots staffed through the LichManager pool
/// (owner = building id, activity = "main"; the pool is the single source of truth, as with skills).
/// Work per tick = (base work + staffed x work per necromancer) x level multiplier; each building decides what work does.
/// Saves its own section "building_&lt;id&gt;".
/// </summary>
public abstract class BuildingBase : ITickable, ISaveable
{
    public const string StaffActivity = "main";

    protected BuildingBase(string id, CryptContext context)
    {
        Context = context;
        Definition = context.Content.TryGet<BuildingDefinitionBase>(id, out var d)
            ? d
            : throw new InvalidOperationException($"Content has no building '{id}' (Data/buildings.json).");
        Id = id;
    }

    public string Id { get; }
    public string Name => Definition.Name;
    public BuildingDefinitionBase Definition { get; }
    public int Level { get; private set; } = 1;
    public BuildingStatus Status { get; private set; } = BuildingStatus.Idle;
    /// <summary>Extra detail for the status (the missing input id, ...).</summary>
    public string? StatusDetail { get; private set; }

    protected CryptContext Context { get; }

    public string SaveKey => "building_" + Id;

    /// <summary>False for buildings that do nothing on the tick (Magical Storage); they are not registered.</summary>
    public virtual bool Ticks => true;

    public event Action<BuildingBase>? LevelChanged;
    public event Action<BuildingBase>? StatusChanged;
    /// <summary>(building, change in staff).</summary>
    public event Action<BuildingBase, long>? StaffChanged;

    // ---- Staff ----

    public long Slots => Definition.BaseSlots + Definition.SlotsPerLevel * (Level - 1);
    public long Staffed => Context.Lich.Pool.GetAssigned(Id, StaffActivity);
    public long FreeSlots => Math.Max(0, Slots - Staffed);

    /// <summary>Staffs up to <paramref name="count"/> necromancers, limited by free slots and the free pool. Returns how many.</summary>
    public long AddNecromancer(long count)
    {
        var n = Context.Lich.TryAssign(Id, StaffActivity, Math.Min(count, FreeSlots));
        if (n > 0) StaffChanged?.Invoke(this, n);
        return n;
    }

    public long RemoveNecromancer(long count)
    {
        var n = Context.Lich.Unassign(Id, StaffActivity, count);
        if (n > 0) StaffChanged?.Invoke(this, -n);
        return n;
    }

    // ---- Work ----

    public double LevelMultiplier => 1 + Definition.LevelBonus * (Level - 1);

    /// <summary>Work per tick right now (staff beyond the slot count, e.g. after a load with fewer slots, still counts only up to Slots).</summary>
    public double WorkPerTick =>
        (Definition.BaseWork + Math.Min(Staffed, Slots) * Definition.WorkPerNecromancer) * LevelMultiplier;

    protected long WorkMicroPerTick => FixedPoint.ToMicro(WorkPerTick);

    // ---- Upgrades ----

    /// <summary>Definition max level plus any extra levels research has unlocked.</summary>
    public int MaxLevel => Definition.MaxLevel + Context.Unlocks.BuildingLevelBonus(Id);

    public bool IsMaxLevel => Level >= MaxLevel;

    /// <summary>Coins for the next level.</summary>
    public long UpgradeCost
    {
        get
        {
            var c = Definition.UpgradeCost * Math.Pow(Definition.UpgradeGrowth, Level - 1);
            return c >= long.MaxValue ? long.MaxValue : (long)Math.Round(c);
        }
    }

    /// <summary>Spends coins and raises the level. False if at max level or short of coins.</summary>
    public bool TryUpgrade()
    {
        if (IsMaxLevel) return false;
        if (!Context.Storage.TrySpend(ResourceIdsCoins, UpgradeCost)) return false;
        SetLevel(Level + 1);
        return true;
    }

    public const string ResourceIdsCoins = "coins";

    /// <summary>Debug: set the level directly (no cost).</summary>
    public void DebugSetLevel(int level) => SetLevel(Math.Clamp(level, 1, MaxLevel));

    private void SetLevel(int level)
    {
        if (level == Level) return;
        Level = level;
        OnLevelChanged();
        LevelChanged?.Invoke(this);
    }

    /// <summary>Apply level effects (max necromancers, storage caps). Also called after load.</summary>
    protected virtual void OnLevelChanged() { }

    protected void SetStatus(BuildingStatus status, string? detail = null)
    {
        if (Status == status && StatusDetail == detail) return;
        Status = status;
        StatusDetail = detail;
        StatusChanged?.Invoke(this);
    }

    public abstract void Tick(long ticks);

    // ---- Save ----

    protected virtual void SaveExtra(SaveData data) { }
    protected virtual void LoadExtra(SaveData data) { }

    public SaveData Save()
    {
        var d = new SaveData().Set("level", Level);
        SaveExtra(d);
        return d;
    }

    public void Load(SaveData data)
    {
        Level = Math.Clamp(data.Get("level", 1), 1, MaxLevel);   // research loads first, so its extra levels count
        SetStatus(Ticks ? BuildingStatus.Idle : BuildingStatus.Passive);
        LoadExtra(data);
        OnLevelChanged();
        LevelChanged?.Invoke(this);
    }

    // ---- Helpers for converting buildings ----

    /// <summary>How many times the inputs can be paid for (long.MaxValue if none), and the first limiting input.</summary>
    protected (long Can, string? Limiting) Affordable(System.Collections.Generic.IEnumerable<ItemAmount> inputs)
    {
        long can = long.MaxValue;
        string? limiting = null;
        foreach (var i in inputs)
        {
            var n = Context.Stock.Get(i.Id) / i.Count;
            if (n < can)
            {
                can = n;
                limiting = i.Id;
            }
        }
        return (can, limiting);
    }

    protected static long MulClamp(long a, long b)
    {
        var p = (Int128)a * b;
        return FixedPoint.Clamp(p);
    }
}
