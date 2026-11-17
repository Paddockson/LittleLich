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
/// <see cref="Stockpile"/>, and the seven Workshop buildings (one class each). Research tomes (phase 7) and the
/// Unit Builder (phase 9) come later. Does not own the necromancer pool: the Summoning Circle adds to the
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

    public Storage Storage { get; private set; } = new(ContentDatabase.Empty);
    public Inventory Inventory { get; private set; } = new(ContentDatabase.Empty);
    public Stockpile Stockpile { get; private set; }

    private readonly List<BuildingBase> _buildings = new();

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
        var ctx = new CryptContext(lich, Stockpile, content, () => tm.Scheduler.ActiveRecap);
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
        return new ISaveable[] { Storage, Inventory }.Concat(_buildings).ToList();
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
            foreach (var b in _buildings) tm.Unregister(b);
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
