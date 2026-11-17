using System;
using LittleLich.Content;
using LittleLich.Content.Definitions;

namespace LittleLich.Crypt;

/// <summary>
/// One view over Storage (resources) and the Inventory (items), so skills can gather into and craft from
/// either by id. Ids are unique across the two (checked at content load).
/// </summary>
public sealed class Stockpile
{
    public Stockpile(Storage storage, Inventory inventory)
    {
        Storage = storage;
        Inventory = inventory;
    }

    public Storage Storage { get; }
    public Inventory Inventory { get; }

    public bool Exists(string id) => Storage.IsResource(id) || Inventory.IsItem(id);

    public long Get(string id) => Storage.IsResource(id) ? Storage.Get(id) : Inventory.IsItem(id) ? Inventory.Get(id) : 0;

    /// <summary>Space left before the cap (resources) or effectively unlimited (items, until phase 6).</summary>
    public long Room(string id) => Storage.IsResource(id) ? Storage.Room(id) : Inventory.Room(id);

    public long Add(string id, long amount) => Storage.IsResource(id) ? Storage.Add(id, amount) : Inventory.Add(id, amount);

    public bool TryRemove(string id, long amount) =>
        Storage.IsResource(id) ? Storage.TryRemove(id, amount) : Inventory.TryRemove(id, amount);

    /// <summary>"resource" or "item", for recaps and logs.</summary>
    public string KindOf(string id) => Storage.IsResource(id) ? "resource" : Inventory.IsItem(id) ? "item" : throw new ArgumentException($"Unknown stock id '{id}'.");
}
