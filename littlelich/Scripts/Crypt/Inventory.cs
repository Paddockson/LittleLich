using System;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;

namespace LittleLich.Crypt;

/// <summary>
/// Item stacks by id (save section "inventory"). Counts only, no per-instance data (v0.1).
/// Space is counted in slots: an id with count c uses ceil(c / stack_size) slots, up to <see cref="SlotCapacity"/>
/// (set by Magical Storage; unlimited until then). Equippable and consumable items live here; resources live in Storage.
/// Gear used by summons (phase 9) is spent from here. Only ids defined as items are accepted.
/// </summary>
public sealed class Inventory : ISaveable
{
    private readonly ContentDatabase _content;
    private readonly Dictionary<string, long> _stacks = new(StringComparer.Ordinal);
    private long _capacity = long.MaxValue;

    public Inventory(ContentDatabase content) => _content = content;

    public string SaveKey => "inventory";

    /// <summary>(item id, new count).</summary>
    public event Action<string, long>? Changed;
    /// <summary>The slot capacity changed.</summary>
    public event Action? CapacityChanged;

    public bool IsItem(string id) => _content.Contains<ItemDefinitionBase>(id);

    public long Get(string id) => _stacks.GetValueOrDefault(id);
    public long Count(string id) => Get(id);

    /// <summary>Stacks with a count above 0, by id.</summary>
    public IReadOnlyList<(string Id, long Count)> Stacks =>
        _stacks.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)).ToList();

    /// <summary>How many slots the inventory has (long.MaxValue = unlimited).</summary>
    public long SlotCapacity
    {
        get => _capacity;
        set
        {
            var v = Math.Max(0, value);
            if (v == _capacity) return;
            _capacity = v;
            CapacityChanged?.Invoke();
        }
    }

    public long SlotsUsed => _stacks.Sum(kv => SlotsFor(kv.Key, kv.Value));
    public long FreeSlots => _capacity == long.MaxValue ? long.MaxValue : Math.Max(0, _capacity - SlotsUsed);

    public int StackSize(string id) => Def(id).StackSize;

    /// <summary>How many more of this item fit: the rest of its last stack plus every free slot.</summary>
    public long Room(string id)
    {
        var size = StackSize(id);
        var have = Get(id);
        if (_capacity == long.MaxValue) return long.MaxValue - have;
        var partial = (SlotsFor(id, have) * size) - have;
        var room = (Int128)partial + (Int128)FreeSlots * size;
        return FixedPoint.Clamp(room);
    }

    /// <summary>Adds up to the room left. Returns how many were added.</summary>
    public long Add(string id, long amount)
    {
        if (amount <= 0) { Def(id); return 0; }
        var added = Math.Min(amount, Room(id));
        if (added <= 0) return 0;
        Set(id, Get(id) + added);
        return added;
    }

    /// <summary>Removes exactly <paramref name="amount"/>, or nothing.</summary>
    public bool TryRemove(string id, long amount)
    {
        Def(id);
        if (amount < 0) return false;
        if (amount == 0) return true;
        if (Get(id) < amount) return false;
        Set(id, Get(id) - amount);
        return true;
    }

    public bool Remove(string id, long amount) => TryRemove(id, amount);

    public SaveData Save() => new SaveData().Set("stacks",
        _stacks.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value));

    /// <summary>Unknown ids are dropped. Stacks over the capacity are kept (nothing more fits until there is room).</summary>
    public void Load(SaveData data)
    {
        var old = _stacks.Keys.ToList();
        _stacks.Clear();
        foreach (var (id, n) in data.Get("stacks", new Dictionary<string, long>()))
            if (n > 0 && IsItem(id)) _stacks[id] = n;
        foreach (var id in old.Union(_stacks.Keys).ToList()) Changed?.Invoke(id, Get(id));
    }

    private long SlotsFor(string id, long count)
    {
        if (count <= 0) return 0;
        var size = _content.TryGet<ItemDefinitionBase>(id, out var d) ? d.StackSize : 1;
        return (count + size - 1) / size;
    }

    private void Set(string id, long value)
    {
        _stacks[id] = value;
        Changed?.Invoke(id, value);
    }

    private ItemDefinitionBase Def(string id) =>
        _content.TryGet<ItemDefinitionBase>(id, out var d) ? d : throw new ArgumentException($"'{id}' is not an item.", nameof(id));
}
