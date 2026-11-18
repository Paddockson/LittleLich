using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Core.Ticks;
using LittleLich.Crypt;
using LittleLich.Crypt.Buildings;

namespace LittleLich.Managers;

/// <summary>
/// Autoload singleton (after LichManager, before TickManager). Owns the Crypt:
/// <see cref="Storage"/> (resources, capped), the minimal <see cref="Inventory"/> (phase 6 completes it), joined by
/// <see cref="Stockpile"/>, the seven Workshop buildings (one class each), research (phase 7: <see cref="Research"/>,
/// one class per tome, Necromancy 1 only), the shared <see cref="Unlocks"/> and the Unit Builder (phase 9: <see cref="Units"/>). Does not own the necromancer pool: the Summoning Circle adds to the
/// LichManager's pool, and buildings are staffed through LichManager assignments (owner = building id).
/// </summary>
public partial class CryptManager : Node
{
    public static CryptManager Instance { get; private set; } = null!;

    [Signal] public delegate void StockChangedEventHandler(string resourceId, long stock);
    [Signal] public delegate void InventoryChangedEventHandler(string itemId, long count);
    [Signal] public delegate void StorageRatesUpdatedEventHandler();
    [Signal] public delegate void StorageCapsChangedEventHandler();
    [Signal] public delegate void BuildingLevelChangedEventHandler(string buildingId, int level);
    [Signal] public delegate void BuildingStatusChangedEventHandler(string buildingId, int status, string detail);
    [Signal] public delegate void BuildingStaffChangedEventHandler(string buildingId, long delta);
    [Signal] public delegate void ResearchNodeStartedEventHandler(string nodeId, int bench);
    [Signal] public delegate void ResearchNodeCompletedEventHandler(string nodeId);
    [Signal] public delegate void ResearchNodeAvailableEventHandler(string nodeId);
    [Signal] public delegate void ResearchNodeCancelledEventHandler(string nodeId, int bench);
    [Signal] public delegate void ResearchBenchesChangedEventHandler();
    [Signal] public delegate void UnitTypesChangedEventHandler();
    [Signal] public delegate void UnitRosterChangedEventHandler(string unitTypeId, long count);
    [Signal] public delegate void FormationsChangedEventHandler();

    public Storage Storage { get; private set; } = new(ContentDatabase.Empty);
    public Inventory Inventory { get; private set; } = new(ContentDatabase.Empty);
    public Stockpile Stockpile { get; private set; }

    private readonly List<BuildingBase> _buildings = new();

    /// <summary>What research has unlocked (shared with skills and buildings).</summary>
    public Unlocks Unlocks { get; private set; } = Unlocks.None;

    /// <summary>Research benches and tomes (null until boot).</summary>
    public Crypt.Research.ResearchManager? Research { get; private set; }

    /// <summary>Research income with no building (soul harvest rate).</summary>
    public Crypt.PassiveIncome? Income { get; private set; }

    /// <summary>Unit types, the roster and formations (null until boot).</summary>
    public Crypt.Units.UnitBuilder? Units { get; private set; }

    /// <summary>The seven buildings in flow order (empty until boot).</summary>
    public IReadOnlyList<BuildingBase> Buildings => _buildings;

    public BuildingBase? Building(string id) => _buildings.FirstOrDefault(b => b.Id == id);
    public T? Building<T>() where T : BuildingBase => _buildings.OfType<T>().FirstOrDefault();

    private bool _initialized;

    public CryptManager()
    {
        Instance = this;
        Stockpile = new Stockpile(Storage, Inventory);
    }

    /// <summary>
    /// Boot step 3 (called by GameManager after content loads, before the save is read): creates Storage, Inventory
    /// and the buildings, registers ticking buildings in flow order, and returns every Crypt saveable.
    /// </summary>
    public IReadOnlyList<ISaveable> Initialize(ContentDatabase content, LichState lich)
    {
        if (_initialized) throw new InvalidOperationException("CryptManager is already initialised.");
        _initialized = true;
        Storage = new Storage(content);
        Inventory = new Inventory(content);
        Stockpile = new Stockpile(Storage, Inventory);
        Storage.Changed += (id, n) => EmitSignal(SignalName.StockChanged, id, n);
        Storage.RatesUpdated += () => EmitSignal(SignalName.StorageRatesUpdated);
        Storage.CapsChanged += () => EmitSignal(SignalName.StorageCapsChanged);
        Inventory.Changed += (id, n) => EmitSignal(SignalName.InventoryChanged, id, n);

        var tm = TickManager.Instance;
        Unlocks = new Unlocks();
        Research = new Crypt.Research.ResearchManager(lich, Stockpile, content, Unlocks, () => tm.Scheduler.ActiveRecap);
        Research.NodeStarted += (n, b) => EmitSignal(SignalName.ResearchNodeStarted, n, b);
        Research.NodeAvailable += n => EmitSignal(SignalName.ResearchNodeAvailable, n);
        Research.NodeCancelled += (n, b) => EmitSignal(SignalName.ResearchNodeCancelled, n, b);
        Research.BenchesChanged += () => EmitSignal(SignalName.ResearchBenchesChanged);
        Research.NodeCompleted += (n, _) => EmitSignal(SignalName.ResearchNodeCompleted, n);
        Research.TomeFound += t => EmitSignal(SignalName.ResearchBenchesChanged);
        Research.BenchLevelChanged += () => EmitSignal(SignalName.ResearchBenchesChanged);
        tm.Register(Research, TickGroup.Research);
        // Research targets read unit families (skeleton, wisp).
        StatTargets.IsUnit = id => content.TryGet<UnitDefinitionBase>(id, out var u) && u.IsPlayer;
        StatTargets.UnitTags = id => content.TryGet<UnitDefinitionBase>(id, out var u) ? u.Tags : Array.Empty<string>();
        Income = new Crypt.PassiveIncome(lich, Stockpile, () => tm.Scheduler.ActiveRecap);
        tm.Register(Income, TickGroup.Buildings, 50);

        Units = new Crypt.Units.UnitBuilder(content, Stockpile, Unlocks) { CostMultiplier = id => Crypt.ResearchStats.CostMultiplier(lich.Stats, id) };
        Units.TypesChanged += () => EmitSignal(SignalName.UnitTypesChanged);
        Units.RosterChanged += (id, n) => EmitSignal(SignalName.UnitRosterChanged, id, n);
        Units.FormationsChanged += () => EmitSignal(SignalName.FormationsChanged);

        var ctx = new CryptContext(lich, Stockpile, content, () => tm.Scheduler.ActiveRecap) { Unlocks = Unlocks, Units = Units };
        var order = 0;
        foreach (var b in BuildingFactory.CreateAll(ctx))
        {
            _buildings.Add(b);
            b.LevelChanged += x => EmitSignal(SignalName.BuildingLevelChanged, x.Id, x.Level);
            b.StatusChanged += x => EmitSignal(SignalName.BuildingStatusChanged, x.Id, (int)x.Status, x.StatusDetail ?? "");
            b.StaffChanged += (x, d) => EmitSignal(SignalName.BuildingStaffChanged, x.Id, d);
            order += 100; // ManaWell 100 ... UndeadMarket 600 (TickOrder constants)
            if (b.Ticks) tm.Register(b, TickGroup.Buildings, order);
        }
        // Research loads before the buildings so its level caps and unlocks count when levels are restored;
        // the Unit Builder before the buildings so the Summoning Circle finds its unit type.
        return new ISaveable[] { Storage, Inventory, Research, Units }.Concat(_buildings).ToList();
    }

    public override void _Ready()
    {
        // Rates for the resource bar: net change per tick over each tick batch.
        var tm = TickManager.Instance;
        tm.TickStarted += (_, _) => Storage.BeginBatch();
        tm.TickEnded += (_, n) => Storage.EndBatch(n);
    }

    public override void _ExitTree()
    {
        if (TickManager.Instance is { } tm)
        {
            foreach (var b in _buildings) tm.Unregister(b);
            if (Research is not null) tm.Unregister(Research);
            if (Income is not null) tm.Unregister(Income);
        }
    }

    // ---- Commands ----

    /// <summary>Adds a resource (capped). Returns how much was added.</summary>
    public long Add(string resourceId, long amount) => Storage.Add(resourceId, amount);

    /// <summary>Spends exactly <paramref name="amount"/> of a resource, or nothing.</summary>
    public bool TrySpend(string resourceId, long amount) => Storage.TrySpend(resourceId, amount);

    public bool TryUpgrade(string buildingId) => Building(buildingId)?.TryUpgrade() ?? false;

    /// <summary>Debug command: add a resource or item (resources respect the cap). Returns how many were added.</summary>
    public long DebugGive(string id, long amount)
    {
        if (!Stockpile.Exists(id)) throw new ArgumentException($"No resource or item '{id}'.");
        return amount >= 0 ? Stockpile.Add(id, amount) : (Stockpile.TryRemove(id, -amount) ? amount : 0);
    }
}
