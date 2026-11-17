using System;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Crypt;

namespace LittleLich.Gear;

public enum EquipResult
{
    Ok,
    UnknownSlot,
    /// <summary>Not an item the lich can wear.</summary>
    NotEquippable,
    /// <summary>The item does not list this slot.</summary>
    WrongSlot,
    /// <summary>None in the inventory.</summary>
    NotInInventory,
    /// <summary>The item already worn there cannot go back into the inventory (no room).</summary>
    InventoryFull,
}

/// <summary>
/// The lich's seven equipment slots (save section "equipment"). Equipping takes one item out of the inventory
/// (swapping the old one back in); unequipping puts it back. Worn items add their modifiers to the lich through the
/// phase 2 modifier system with source "gear:&lt;slot&gt;"; changing a slot replaces exactly that slot's modifiers.
/// Modifiers are always rebuilt from the item definitions (on load too), so content changes apply to worn gear.
/// </summary>
public sealed class Equipment : ISaveable
{
    private readonly LichState _lich;
    private readonly Inventory _inventory;
    private readonly ContentDatabase _content;
    private readonly Dictionary<string, string> _worn = new(StringComparer.Ordinal);

    public Equipment(LichState lich, Inventory inventory, ContentDatabase content)
    {
        _lich = lich;
        _inventory = inventory;
        _content = content;
    }

    public string SaveKey => "equipment";

    /// <summary>A slot changed (slot id).</summary>
    public event Action<string>? Changed;

    /// <summary>Everything was reloaded from a save (one event instead of one per slot).</summary>
    public event Action? Reloaded;

    public string? Worn(string slot) => _worn.GetValueOrDefault(slot);

    public EquippableDefinitionBase? WornDefinition(string slot) =>
        Worn(slot) is { } id && _content.TryGet<EquippableDefinitionBase>(id, out var d) ? d : null;

    /// <summary>Lich-wearable items in the inventory that fit <paramref name="slot"/>.</summary>
    public IEnumerable<EquippableDefinitionBase> CandidatesFor(string slot) =>
        _inventory.Stacks.Select(s => _content.TryGet<EquippableDefinitionBase>(s.Id, out var d) ? d : null)
            .OfType<EquippableDefinitionBase>()
            .Where(d => d.Slots.Contains(slot));

    public EquipResult Equip(string slot, string itemId)
    {
        if (!LichSlots.All.Contains(slot)) return EquipResult.UnknownSlot;
        if (!_content.TryGet<EquippableDefinitionBase>(itemId, out var def) || !def.FitsLich) return EquipResult.NotEquippable;
        if (!def.Slots.Contains(slot)) return EquipResult.WrongSlot;
        if (Worn(slot) == itemId) return EquipResult.Ok;
        if (!_inventory.TryRemove(itemId, 1)) return EquipResult.NotInInventory;

        if (Worn(slot) is { } old && _inventory.Add(old, 1) == 0)
        {
            _inventory.Add(itemId, 1);   // just removed, so it fits again
            return EquipResult.InventoryFull;
        }
        _worn[slot] = itemId;
        Apply(slot);
        Changed?.Invoke(slot);
        return EquipResult.Ok;
    }

    /// <summary>Equips into the first free slot the item fits (or its first slot). </summary>
    public EquipResult EquipAuto(string itemId)
    {
        if (!_content.TryGet<EquippableDefinitionBase>(itemId, out var def) || !def.FitsLich) return EquipResult.NotEquippable;
        var slot = def.Slots.FirstOrDefault(s => Worn(s) is null) ?? def.Slots[0];
        return Equip(slot, itemId);
    }

    /// <summary>Takes off what is in the slot and puts it back in the inventory. False if empty or no room.</summary>
    public bool Unequip(string slot)
    {
        if (Worn(slot) is not { } id) return false;
        if (_inventory.Add(id, 1) == 0) return false;
        _worn.Remove(slot);
        Apply(slot);
        Changed?.Invoke(slot);
        return true;
    }

    /// <summary>Makes the slot's "gear:&lt;slot&gt;" modifiers match what is worn. New ones are added before old ones are removed
    /// so max health never dips (which would clamp current health) when swapping similar gear.</summary>
    private void Apply(string slot)
    {
        var source = LichSlots.Source(slot);
        var stats = _lich.Stats;
        var desired = (WornDefinition(slot)?.Modifiers ?? new List<StatModifierDef>())
            .Select(m => new Modifier(m.Stat, m.IsMul ? ModifierOp.Mul : ModifierOp.Add, m.Value, source))
            .ToList();
        var old = stats.Modifiers.Where(m => m.Source == source).ToList();
        if (old.SequenceEqual(desired)) return;
        foreach (var m in desired) stats.Add(m);
        foreach (var m in old) stats.RemoveWhere(x => ReferenceEquals(x, m));
    }

    public SaveData Save() => new SaveData().Set("worn", _worn.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value));

    /// <summary>
    /// Restores worn items. An item that no longer exists is dropped; one that no longer fits its slot is returned
    /// to the inventory. Every slot's modifiers are then rebuilt from the definitions (stale saved ones are removed).
    /// </summary>
    public void Load(SaveData data)
    {
        _worn.Clear();
        foreach (var (slot, id) in data.Get("worn", new Dictionary<string, string>()))
        {
            if (!LichSlots.All.Contains(slot) || !_content.TryGet<EquippableDefinitionBase>(id, out var def)) continue;
            if (def.Slots.Contains(slot)) _worn[slot] = id;
            else _inventory.Add(id, 1);
        }
        foreach (var slot in LichSlots.All) Apply(slot);
        Reloaded?.Invoke();
    }
}

public enum UseResult
{
    Ok,
    /// <summary>Not a usable item (only potions in v0.1; bombs are for battles).</summary>
    NotUsable,
    NoneLeft,
}

/// <summary>
/// Using items from the inventory. v0.1: potions heal and/or apply timed modifiers (source "potion:&lt;id&gt;");
/// drinking the same potion again refreshes its duration instead of stacking.
/// </summary>
public sealed class ItemUser
{
    private readonly LichState _lich;
    private readonly Inventory _inventory;
    private readonly ContentDatabase _content;

    public ItemUser(LichState lich, Inventory inventory, ContentDatabase content)
    {
        _lich = lich;
        _inventory = inventory;
        _content = content;
    }

    /// <summary>(item id) after a successful use.</summary>
    public event Action<string>? Used;

    public bool IsUsable(string itemId) => _content.Contains<PotionDefinition>(itemId);

    public UseResult Use(string itemId)
    {
        if (!_content.TryGet<PotionDefinition>(itemId, out var p)) return UseResult.NotUsable;
        if (!_inventory.TryRemove(itemId, 1)) return UseResult.NoneLeft;

        var source = "potion:" + itemId;
        var old = _lich.Stats.Modifiers.Where(m => m.Source == source).ToList();
        foreach (var e in p.Effects)
            _lich.AddTimedModifier(e.Stat, e.IsMul ? ModifierOp.Mul : ModifierOp.Add, e.Value, source, p.DurationTicks);
        foreach (var m in old) _lich.Stats.RemoveWhere(x => ReferenceEquals(x, m));
        if (p.Heal > 0) _lich.Heal(p.Heal);

        Used?.Invoke(itemId);
        return UseResult.Ok;
    }
}
