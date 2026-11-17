using Godot;
using LittleLich.Content;
using LittleLich.Core;
using LittleLich.Crypt;

namespace LittleLich.Managers;

/// <summary>
/// Autoload singleton (after LichManager, before TickManager). Phase 4 pulled forward only what skills need:
/// <see cref="Storage"/> (resources, capped) and a minimal <see cref="Inventory"/> (item stacks; phase 6 completes it),
/// joined by <see cref="Stockpile"/>. Workshop buildings, research tomes and the Unit Builder arrive in phases 5, 7, 9.
/// </summary>
public partial class CryptManager : Node
{
    public static CryptManager Instance { get; private set; } = null!;

    [Signal] public delegate void StockChangedEventHandler(string resourceId, long stock);
    [Signal] public delegate void InventoryChangedEventHandler(string itemId, long count);
    [Signal] public delegate void StorageRatesUpdatedEventHandler();

    public Storage Storage { get; private set; } = new(ContentDatabase.Empty);
    public Inventory Inventory { get; private set; } = new(ContentDatabase.Empty);
    public Stockpile Stockpile { get; private set; }

    private bool _initialized;

    public CryptManager()
    {
        Instance = this;
        Stockpile = new Stockpile(Storage, Inventory);
    }

    /// <summary>
    /// Boot step 3 (called by GameManager once content is loaded, before the save is read):
    /// creates Storage and Inventory for the loaded content. Returns the saveables to register.
    /// </summary>
    public ISaveable[] Initialize(ContentDatabase content)
    {
        if (_initialized) throw new System.InvalidOperationException("CryptManager is already initialised.");
        _initialized = true;
        Storage = new Storage(content);
        Inventory = new Inventory(content);
        Stockpile = new Stockpile(Storage, Inventory);
        Storage.Changed += (id, n) => EmitSignal(SignalName.StockChanged, id, n);
        Storage.RatesUpdated += () => EmitSignal(SignalName.StorageRatesUpdated);
        Inventory.Changed += (id, n) => EmitSignal(SignalName.InventoryChanged, id, n);
        return new ISaveable[] { Storage, Inventory };
    }

    public override void _Ready()
    {
        // Rates for the resource bar: net change per tick over each tick batch.
        var tm = TickManager.Instance;
        tm.TickStarted += (_, _) => Storage.BeginBatch();
        tm.TickEnded += (_, n) => Storage.EndBatch(n);
    }

    /// <summary>Debug command: add a resource or item (resources respect the cap). Returns how many were added.</summary>
    public long DebugGive(string id, long amount)
    {
        if (!Stockpile.Exists(id)) throw new System.ArgumentException($"No resource or item '{id}'.");
        return amount >= 0 ? Stockpile.Add(id, amount) : (Stockpile.TryRemove(id, -amount) ? amount : 0);
    }
}
