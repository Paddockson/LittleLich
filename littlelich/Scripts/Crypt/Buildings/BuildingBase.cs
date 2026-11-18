using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>The Unit Builder (phase 9), for the Summoning Circle's unit mode. Null in tests that do not need it.</summary>
    public Units.UnitBuilder? Units { get; init; }

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
/// A Workshop building: a level bought with gold, and necromancer slots staffed through the LichManager pool
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

    // ---- Research gate (research revamp) ----

    /// <summary>
    /// What research opens this building: (feature, "summoning") for the Summoning Circle, (building, id) for the
    /// others. Buildings no research unlocks (the Mana Well) are open from the start.
    /// </summary>
    public virtual (string Kind, string Id) UnlockKey => (Unlocks.Building, Id);

    /// <summary>False while research has not opened it: the Workshop does not draw it, it takes no staff and does not tick.</summary>
    public bool IsUnlocked => Context.Unlocks.IsUnlocked(UnlockKey.Kind, UnlockKey.Id);

    // ---- Staff ----

    /// <summary>
    /// The most necromancers the building holds at its level (the level table's max_necromancers, if it has one),
    /// raised by capacity research (Necro Capacity +1 each, Magical Efficiency x2 for the Mana Well, Dark Empowerment x2).
    /// </summary>
    public long Slots => SlotsAt(Level);

    public long SlotsAt(int level) => ResearchStats.Room(Context.Lich.Stats, Id, BaseSlotsAt(level));

    /// <summary>Places at a level before research.</summary>
    public long BaseSlotsAt(int level) => Definition.Row(level) is { } r ? r.MaxNecromancers
        : Definition.BaseSlots + Definition.SlotsPerLevel * (level - 1);
    public long Staffed => Context.Lich.Pool.GetAssigned(Id, StaffActivity);
    public long FreeSlots => Math.Max(0, Slots - Staffed);

    /// <summary>Staffs up to <paramref name="count"/> necromancers, limited by free slots and the free pool. Returns how many.</summary>
    public long AddNecromancer(long count)
    {
        if (!IsUnlocked) return 0;
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

    public double LevelMultiplier => 1 + LevelBonusAt(Level);

    /// <summary>Output bonus at a level: the table's output_bonus, or level_bonus x (level - 1). 0.1 = +10%.</summary>
    public double LevelBonusAt(int level) => Definition.Row(level) is { } r ? r.OutputBonus : Definition.LevelBonus * (level - 1);

    /// <summary>Work per tick right now (staff beyond the slot count, e.g. after a load with fewer slots, still counts only up to Slots).</summary>
    public double WorkPerTick =>
        (Definition.BaseWork + Math.Min(Staffed, Slots) * WorkPerNecromancer) * LevelMultiplier * OutputModifier;

    /// <summary>Work each necromancer adds per tick before bonuses (the definition's, plus research for some buildings).</summary>
    public virtual double WorkPerNecromancer => Definition.WorkPerNecromancer;

    /// <summary>Research multipliers on work (efficiency, output, necromancer efficiency for this building). 1 with none.</summary>
    public virtual double OutputModifier => ResearchStats.Efficiency(Context.Lich.Stats, Id, Math.Min(Staffed, Slots));

    protected long WorkMicroPerTick => FixedPoint.ToMicro(WorkPerTick);

    // ---- Upgrades ----

    /// <summary>This building's level upgrade (Data/upgrades.json; content validation guarantees there is one).</summary>
    public BuildingUpgradeDefinition Upgrade =>
        _upgrade ??= Context.Content.All<BuildingUpgradeDefinition>().FirstOrDefault(u => u.Building == Id)
                     ?? throw new InvalidOperationException($"No building_upgrade for '{Id}'.");
    private BuildingUpgradeDefinition? _upgrade;

    /// <summary>Upgrade max level plus building_and_node_level_cap from research (never past the level table's last row).</summary>
    public int MaxLevel
    {
        get
        {
            var max = Upgrade.MaxLevel + ResearchStats.LevelCapBonus(Context.Lich.Stats);
            return Definition.HasLevelTable ? Math.Min(max, Definition.Levels.Count) : max;
        }
    }

    public bool IsMaxLevel => Level >= MaxLevel;

    /// <summary>What the next level costs: the next level table row's cost, or the upgrade's cost formula.</summary>
    public IReadOnlyList<ItemAmount> UpgradeCosts =>
        Definition.HasLevelTable
            ? Definition.Row(Level + 1)!.Cost.Select(c => new ItemAmount { Id = c.Key, Count = c.Value }).ToList()
            : Upgrade.CostAt(Level);

    /// <summary>
    /// True when <see cref="TryUpgrade"/> would succeed; otherwise a short reason for the UI
    /// ("Highest level reached", "Not enough mana yet").
    /// </summary>
    public bool CanUpgrade(out string reason)
    {
        if (!IsUnlocked) { reason = "Not researched yet"; return false; }
        if (IsMaxLevel) { reason = "Highest level reached"; return false; }
        var shortOf = UpgradeCosts.FirstOrDefault(c => Context.Stock.Get(c.Id) < c.Count);
        reason = shortOf is null ? "" : $"Not enough {StockIds.NameOf(Context.Content, shortOf.Id).ToLowerInvariant()} yet";
        return shortOf is null;
    }

    /// <summary>The gold part of the next level's cost (0 if it costs no gold).</summary>
    public long UpgradeCost => UpgradeCosts.FirstOrDefault(c => c.Id == Gold)?.Count ?? 0;

    public bool CanAffordUpgrade => UpgradeCosts.All(c => Context.Stock.Get(c.Id) >= c.Count);

    /// <summary>Pays every cost and raises the level, or does nothing. False if at max level or short of anything.</summary>
    public bool TryUpgrade()
    {
        if (!IsUnlocked || IsMaxLevel || !CanAffordUpgrade) return false;
        foreach (var c in UpgradeCosts) Context.Stock.TryRemove(c.Id, c.Count);
        SetLevel(Level + 1);
        return true;
    }

    /// <summary>The currency: upgrades cost gold, the Undead Market pays gold.</summary>
    public const string Gold = "gold";

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

    /// <summary>Locked buildings do nothing (and stay Idle); open ones run <see cref="TickUnlocked"/>.</summary>
    public void Tick(long ticks)
    {
        if (!IsUnlocked) { SetStatus(Ticks ? BuildingStatus.Idle : BuildingStatus.Passive); return; }
        TickUnlocked(ticks);
    }

    protected abstract void TickUnlocked(long ticks);

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
