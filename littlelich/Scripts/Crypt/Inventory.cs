using System;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;

namespace LittleLich.Crypt;

/// <summary>
/// Item stacks by id (save section "inventory"). Minimal version pulled forward from phase 6 so crafting has
/// somewhere to put gear, potions, bombs and runes: counts only, no space limit, no equipping.
/// Phase 6 adds inventory space, equipping and item use. Only ids defined as items are accepted.
/// </summary>
public sealed class Inventory : ISaveable
{
    private readonly ContentDatabase _content;
    private readonly Dictionary<string, long> _stacks = new(StringComparer.Ordinal);

    public Inventory(ContentDatabase content) => _content = content;

    public string SaveKey => "inventory";

    /// <summary>(item id, new count).</summary>
    public event Action<string, long>? Changed;

    public bool IsItem(string id) => _content.Contains<ItemDefinitionBase>(id);

    public long Get(string id) => _stacks.GetValueOrDefault(id);

    /// <summary>Stacks with a count above 0, by id.</summary>
    public IReadOnlyList<(string Id, long Count)> Stacks =>
        _stacks.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)).ToList();

    /// <summary>No space limit yet (phase 6).</summary>
    public long Room(string id)
    {
        Check(id);
        return long.MaxValue - Get(id);
    }

    public long Add(string id, long amount)
    {
        Check(id);
        if (amount <= 0) return 0;
        var added = Math.Min(amount, long.MaxValue - Get(id));
        Set(id, Get(id) + added);
        return added;
    }

    public bool TryRemove(string id, long amount)
    {
        Check(id);
        if (amount < 0) return false;
        if (amount == 0) return true;
        if (Get(id) < amount) return false;
        Set(id, Get(id) - amount);
        return true;
    }

    public SaveData Save() => new SaveData().Set("stacks",
        _stacks.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value));

    public void Load(SaveData data)
    {
        var old = _stacks.Keys.ToList();
        _stacks.Clear();
        foreach (var (id, n) in data.Get("stacks", new Dictionary<string, long>()))
            if (n > 0 && IsItem(id)) _stacks[id] = n;
        foreach (var id in old.Union(_stacks.Keys).ToList()) Changed?.Invoke(id, Get(id));
    }

    private void Set(string id, long value)
    {
        _stacks[id] = value;
        Changed?.Invoke(id, value);
    }

    private void Check(string id)
    {
        if (!IsItem(id)) throw new ArgumentException($"'{id}' is not an item.", nameof(id));
    }
}
