using System;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;

namespace LittleLich.Crypt;

/// <summary>
/// Resource stock with caps (save section "storage"). Minimal version pulled forward from phase 5 so skills
/// have somewhere to put what they gather. Stock, cap (base_cap x <see cref="CapMultiplier"/>, which Magical Storage
/// raises) and the net rate per tick of the last tick batch (for the resource bar).
/// Only ids defined as resources are accepted.
/// </summary>
public sealed class Storage : ISaveable
{
    private readonly ContentDatabase _content;
    private readonly Dictionary<string, long> _stock = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _batchDelta = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _ratePerTick = new(StringComparer.Ordinal);

    public Storage(ContentDatabase content) => _content = content;

    public string SaveKey => "storage";

    /// <summary>(resource id, new stock).</summary>
    public event Action<string, long>? Changed;

    /// <summary>Rates were recomputed at the end of a tick batch.</summary>
    public event Action? RatesUpdated;

    public bool IsResource(string id) => _content.Contains<ResourceDefinitionBase>(id);

    public long Get(string id) => _stock.GetValueOrDefault(id);

    private double _capMultiplier = 1;

    /// <summary>Raised when caps change (Magical Storage level).</summary>
    public event Action? CapsChanged;

    /// <summary>Every cap is base_cap x this (set by Magical Storage; 1 at level 1).</summary>
    public double CapMultiplier
    {
        get => _capMultiplier;
        set
        {
            var v = double.IsFinite(value) && value > 0 ? value : 1;
            if (v == _capMultiplier) return;
            _capMultiplier = v;
            CapsChanged?.Invoke();
        }
    }

    public long Cap(string id)
    {
        var c = Def(id).BaseCap * _capMultiplier;
        return c >= long.MaxValue ? long.MaxValue : (long)Math.Floor(c);
    }

    /// <summary>Space left before the cap (0 when at or over it).</summary>
    public long Room(string id) => Math.Max(0, Cap(id) - Get(id));

    public bool IsFull(string id) => Room(id) == 0;

    /// <summary>Net change per tick over the last tick batch (gathered minus spent).</summary>
    public double RatePerTick(string id) => _ratePerTick.GetValueOrDefault(id);

    /// <summary>Adds up to the cap. Returns how much was actually added.</summary>
    public long Add(string id, long amount)
    {
        if (amount <= 0) return 0;
        var added = Math.Min(amount, Room(id));
        if (added == 0) return 0;
        Set(id, Get(id) + added);
        _batchDelta[id] = _batchDelta.GetValueOrDefault(id) + added;
        return added;
    }

    public bool Has(string id, long amount) => Get(id) >= amount;

    /// <summary>Spends exactly <paramref name="amount"/> (e.g. gold for an upgrade), or nothing. Same as TryRemove.</summary>
    public bool TrySpend(string id, long amount) => TryRemove(id, amount);

    /// <summary>Removes exactly <paramref name="amount"/>, or nothing if there is not enough.</summary>
    public bool TryRemove(string id, long amount)
    {
        if (amount < 0) return false;
        Def(id);
        if (amount == 0) return true;
        var have = Get(id);
        if (have < amount) return false;
        Set(id, have - amount);
        _batchDelta[id] = _batchDelta.GetValueOrDefault(id) - amount;
        return true;
    }

    /// <summary>Tick step 1: start counting this batch's changes.</summary>
    public void BeginBatch() => _batchDelta.Clear();

    /// <summary>Tick step 7: turn the batch's net changes into per-tick rates.</summary>
    public void EndBatch(long ticks)
    {
        if (ticks <= 0) return;
        _ratePerTick.Clear();
        foreach (var (id, delta) in _batchDelta)
            if (delta != 0) _ratePerTick[id] = (double)delta / ticks;
        _batchDelta.Clear();
        RatesUpdated?.Invoke();
    }

    public SaveData Save() => new SaveData().Set("stock",
        _stock.Where(kv => kv.Value != 0).OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value));

    /// <summary>Unknown ids (content removed) are dropped; negative stock becomes 0. Stock above the cap is kept.</summary>
    public void Load(SaveData data)
    {
        var old = _stock.Keys.ToList();
        _stock.Clear();
        _batchDelta.Clear();
        _ratePerTick.Clear();
        foreach (var (id, n) in data.Get("stock", new Dictionary<string, long>()))
            if (n > 0 && IsResource(id)) _stock[id] = n;
        foreach (var id in old.Union(_stock.Keys).ToList()) Changed?.Invoke(id, Get(id));
        RatesUpdated?.Invoke();
    }

    private void Set(string id, long value)
    {
        _stock[id] = value;
        Changed?.Invoke(id, value);
    }

    private ResourceDefinitionBase Def(string id) =>
        _content.TryGet<ResourceDefinitionBase>(id, out var d) ? d : throw new ArgumentException($"'{id}' is not a resource.", nameof(id));
}
