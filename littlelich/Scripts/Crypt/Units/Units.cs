using System;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;

namespace LittleLich.Crypt.Units;

/// <summary>
/// A unit type: a base body plus one loadout (weapon, armour, optional items). One loadout per type (v0.1);
/// a different loadout is a different type. Every summon uses up one of each piece of gear and item, plus mana.
/// </summary>
public sealed record UnitType(string Id, string Name, string Base, string? Weapon, string? Armour, IReadOnlyList<string> Items)
{
    /// <summary>The automatic id for a loadout: base[_weapon][_armour][_item...].</summary>
    public static string IdFor(string baseId, string? weapon, string? armour, IEnumerable<string> items) =>
        string.Join("_", new[] { baseId, weapon, armour }.Concat(items).Where(x => !string.IsNullOrEmpty(x)));
}

public enum BuildResult
{
    Ok,
    UnknownBase,
    /// <summary>Enemy units cannot be built.</summary>
    NotAPlayerUnit,
    /// <summary>The base is locked behind research (e.g. Raise Skeletons).</summary>
    BaseLocked,
    /// <summary>Not a weapon (or a lich-only weapon).</summary>
    BadWeapon,
    BadArmour,
    BadItem,
}

/// <summary>
/// The eight-slot formation (phase 9 decision): 2 front, 3 middle, 3 back. Each slot holds a unit type and how
/// many of that stack to send. A formation is a saved loadout the player reuses.
/// </summary>
public sealed class Formation
{
    public const int SlotCount = 8;

    /// <summary>The section of each slot, by index.</summary>
    public static readonly IReadOnlyList<string> SlotSections = new[]
    {
        Sections.Front, Sections.Front,
        Sections.Middle, Sections.Middle, Sections.Middle,
        Sections.Back, Sections.Back, Sections.Back,
    };

    public Formation(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }
    public string Name { get; set; }
    public FormationSlot[] Slots { get; } = Enumerable.Range(0, SlotCount).Select(i => new FormationSlot(i)).ToArray();

    public bool IsEmpty => Slots.All(s => s.UnitType is null || s.Count <= 0);
}

public sealed class FormationSlot
{
    public FormationSlot(int index) => Index = index;
    public int Index { get; }
    public string Section => Formation.SlotSections[Index];
    public string? UnitType { get; internal set; }
    public long Count { get; internal set; }
}

/// <summary>
/// The Unit Builder (save section "units"), owned by the CryptManager: unit types, the roster (summoned units per
/// type, the army's pool) and formations.
///
/// Summoning costs, per unit: the base's mana_cost in mana, one weapon, one armour and one of each item (from the
/// inventory). <see cref="TrySummon"/> is instant and all-or-nothing; the Summoning Circle uses
/// <see cref="SummonUpTo"/> over ticks to keep a type's stack at a target.
///
/// Battle stats for a type (placeholder rules until phase 10): HP = base hp + armour defence x 5;
/// every attack action does + weapon damage. Items are used up but have no battle effect yet (runes later).
/// </summary>
public sealed class UnitBuilder : ISaveable
{
    public const string Mana = "mana";
    public const double HpPerDefence = 5;

    private readonly ContentDatabase _content;
    private readonly Stockpile _stock;
    private readonly Unlocks _unlocks;
    private readonly List<UnitType> _types = new();
    private readonly Dictionary<string, long> _roster = new(StringComparer.Ordinal);
    private readonly List<Formation> _formations = new();
    private readonly Dictionary<string, UnitDefinition> _battleCache = new(StringComparer.Ordinal);

    public UnitBuilder(ContentDatabase content, Stockpile stock, Unlocks unlocks)
    {
        _content = content;
        _stock = stock;
        _unlocks = unlocks;
    }

    public string SaveKey => "units";

    public IReadOnlyList<UnitType> Types => _types;
    public IReadOnlyDictionary<string, long> Roster => _roster;
    public IReadOnlyList<Formation> Formations => _formations;

    public event Action? TypesChanged;
    /// <summary>(unit type id, new count).</summary>
    public event Action<string, long>? RosterChanged;
    public event Action? FormationsChanged;

    /// <summary>Player base bodies, in content order.</summary>
    public IEnumerable<UnitDefinitionBase> Bases => _content.All<UnitDefinitionBase>().Where(u => u.Side == "player");

    public bool IsBaseLocked(string baseId) => _unlocks.IsLocked(Unlocks.Unit, baseId);

    public UnitType? Type(string id) => _types.FirstOrDefault(t => t.Id == id);
    public long Count(string typeId) => _roster.GetValueOrDefault(typeId);

    // ---- Building types ----

    /// <summary>Creates (or returns the existing) unit type for a base and loadout.</summary>
    public (BuildResult Result, UnitType? Type) Build(string baseId, string? weapon = null, string? armour = null, IEnumerable<string>? items = null, string? name = null)
    {
        var itemList = (items ?? Enumerable.Empty<string>()).Where(i => !string.IsNullOrEmpty(i)).ToList();
        if (!_content.TryGet<UnitDefinitionBase>(baseId, out var b)) return (BuildResult.UnknownBase, null);
        if (b.Side != "player") return (BuildResult.NotAPlayerUnit, null);
        if (IsBaseLocked(baseId)) return (BuildResult.BaseLocked, null);
        if (weapon is { Length: > 0 } && !(_content.TryGet<WeaponDefinition>(weapon, out var w) && !w.FitsLich)) return (BuildResult.BadWeapon, null);
        if (armour is { Length: > 0 } && !(_content.TryGet<ArmourDefinition>(armour, out var a) && !a.FitsLich)) return (BuildResult.BadArmour, null);
        if (itemList.Any(i => !_content.Contains<ItemDefinitionBase>(i) || _content.Contains<EquippableDefinitionBase>(i))) return (BuildResult.BadItem, null);

        var id = UnitType.IdFor(baseId, weapon, armour, itemList);
        if (Type(id) is { } existing) return (BuildResult.Ok, existing);
        var t = new UnitType(id, name ?? DefaultName(b, weapon, armour), baseId, NullIfEmpty(weapon), NullIfEmpty(armour), itemList);
        _types.Add(t);
        TypesChanged?.Invoke();
        return (BuildResult.Ok, t);
    }

    /// <summary>The gear-less type for a base (id = base id), created if needed. Used by debug commands and old saves.</summary>
    public UnitType EnsureBare(string baseId)
    {
        if (Type(baseId) is { } t) return t;
        if (!_content.TryGet<UnitDefinitionBase>(baseId, out var b) || b.Side != "player")
            throw new ArgumentException($"'{baseId}' is not a player unit.", nameof(baseId));
        t = new UnitType(baseId, b.Name, baseId, null, null, Array.Empty<string>());
        _types.Add(t);
        TypesChanged?.Invoke();
        return t;
    }

    /// <summary>Set by the world: true while units of a type are out fighting (they cannot be deleted then).</summary>
    public Func<string, bool> InBattle { get; set; } = _ => false;

    /// <summary>Deletes a type with no units (in the roster or fighting) and no formation slots. False otherwise.</summary>
    public bool Remove(string typeId)
    {
        if (Type(typeId) is not { } t || Count(typeId) > 0 || InBattle(typeId) || _formations.Any(f => f.Slots.Any(s => s.UnitType == typeId))) return false;
        _types.Remove(t);
        _battleCache.Remove(typeId);
        TypesChanged?.Invoke();
        return true;
    }

    // ---- Summoning ----

    /// <summary>What one unit of the type costs (mana, weapon, armour, items).</summary>
    public IReadOnlyList<ItemAmount> CostPerUnit(string typeId)
    {
        var t = Type(typeId) ?? throw new ArgumentException($"No unit type '{typeId}'.", nameof(typeId));
        var b = _content.Get<UnitDefinitionBase>(t.Base);
        var cost = new List<ItemAmount>();
        if (b.ManaCost > 0) cost.Add(new ItemAmount { Id = Mana, Count = b.ManaCost });
        foreach (var id in new[] { t.Weapon, t.Armour }.OfType<string>().Concat(t.Items).GroupBy(x => x))
            cost.Add(new ItemAmount { Id = id.Key, Count = id.Count() });
        return cost;
    }

    /// <summary>How many units the current stock pays for, and the first input that limits it.</summary>
    public (long Count, string? Limiting) Affordable(string typeId)
    {
        long can = long.MaxValue;
        string? limiting = null;
        foreach (var c in CostPerUnit(typeId))
        {
            var n = _stock.Get(c.Id) / c.Count;
            if (n < can) { can = n; limiting = c.Id; }
        }
        return (can, limiting);
    }

    /// <summary>Instant summon: pays for all <paramref name="count"/> or nothing. False if locked, unknown or short of stock.</summary>
    public bool TrySummon(string typeId, long count)
    {
        if (count <= 0 || Type(typeId) is not { } t || IsBaseLocked(t.Base)) return false;
        if (Affordable(typeId).Count < count) return false;
        Pay(typeId, count);
        AddToRoster(typeId, count);
        return true;
    }

    /// <summary>Summons up to <paramref name="max"/> units the stock can pay for (Summoning Circle). Returns how many.</summary>
    public long SummonUpTo(string typeId, long max)
    {
        if (max <= 0 || Type(typeId) is not { } t || IsBaseLocked(t.Base)) return 0;
        var n = Math.Min(max, Affordable(typeId).Count);
        if (n <= 0) return 0;
        Pay(typeId, n);
        AddToRoster(typeId, n);
        return n;
    }

    private void Pay(string typeId, long count)
    {
        foreach (var c in CostPerUnit(typeId)) _stock.TryRemove(c.Id, c.Count * count);
    }

    // ---- Roster ----

    public void AddToRoster(string typeId, long count)
    {
        if (count <= 0) return;
        if (Type(typeId) is null) throw new ArgumentException($"No unit type '{typeId}'.", nameof(typeId));
        _roster[typeId] = Count(typeId) + count;
        RosterChanged?.Invoke(typeId, _roster[typeId]);
    }

    /// <summary>Takes exactly <paramref name="count"/> units out of the roster (sending them to battle), or none.</summary>
    public bool TakeFromRoster(string typeId, long count)
    {
        if (count < 0 || Count(typeId) < count) return false;
        if (count == 0) return true;
        _roster[typeId] = Count(typeId) - count;
        RosterChanged?.Invoke(typeId, _roster[typeId]);
        return true;
    }

    // ---- Battle stats ----

    /// <summary>The type as a battle unit: base stats plus its gear (placeholder rules, see the class summary).</summary>
    public UnitDefinitionBase BattleUnit(string typeId)
    {
        if (_battleCache.TryGetValue(typeId, out var cached)) return cached;
        var t = Type(typeId) ?? throw new ArgumentException($"No unit type '{typeId}'.", nameof(typeId));
        var b = _content.Get<UnitDefinitionBase>(t.Base);
        var damage = t.Weapon is { } w ? _content.Get<WeaponDefinition>(w).Damage : 0;
        var defence = t.Armour is { } a ? _content.Get<ArmourDefinition>(a).Defence : 0;
        var u = new UnitDefinition
        {
            Id = t.Id,
            Name = t.Name,
            Side = "player",
            Hp = b.Hp + defence * HpPerDefence,
            ManaCost = b.ManaCost,
            SummonWork = b.SummonWork,
            Actions = b.Actions.Select(x => x.Effect == "attack"
                ? new UnitAction { Id = x.Id, Name = x.Name, Cooldown = x.Cooldown, Effect = x.Effect, Value = x.Value + damage, Target = x.Target, Range = x.Range }
                : x).ToList(),
        };
        _battleCache[typeId] = u;
        return u;
    }

    public UnitDefinitionBase BaseOf(string typeId) => _content.Get<UnitDefinitionBase>(Type(typeId)!.Base);

    // ---- Formations ----

    public Formation? GetFormation(string id) => _formations.FirstOrDefault(f => f.Id == id);

    public Formation CreateFormation(string name)
    {
        var n = 1;
        while (GetFormation($"formation_{n}") is not null) n++;
        var f = new Formation($"formation_{n}", string.IsNullOrWhiteSpace(name) ? $"Formation {n}" : name);
        _formations.Add(f);
        FormationsChanged?.Invoke();
        return f;
    }

    public bool DeleteFormation(string id)
    {
        if (GetFormation(id) is not { } f) return false;
        _formations.Remove(f);
        FormationsChanged?.Invoke();
        return true;
    }

    /// <summary>Puts a unit type and count in a slot (null type clears it). False for a bad slot or unknown type.</summary>
    public bool SetSlot(string formationId, int slot, string? typeId, long count)
    {
        if (GetFormation(formationId) is not { } f || slot < 0 || slot >= Formation.SlotCount) return false;
        if (typeId is not null && Type(typeId) is null) return false;
        f.Slots[slot].UnitType = typeId;
        f.Slots[slot].Count = typeId is null ? 0 : Math.Max(0, count);
        FormationsChanged?.Invoke();
        return true;
    }

    // ---- Save ----

    public SaveData Save() => new SaveData()
        .Set("types", _types.Select(t => new TypeDto { Id = t.Id, Name = t.Name, Base = t.Base, Weapon = t.Weapon, Armour = t.Armour, Items = t.Items.ToList() }).ToList())
        .Set("roster", _roster.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value))
        .Set("formations", _formations.Select(f => new FormationDto
        {
            Id = f.Id,
            Name = f.Name,
            Slots = f.Slots.Select(s => new SlotDto { Unit = s.UnitType, Count = s.Count }).ToList(),
        }).ToList());

    /// <summary>Types whose base or gear no longer exists are dropped, with their roster and slots.</summary>
    public void Load(SaveData data)
    {
        _types.Clear();
        _roster.Clear();
        _formations.Clear();
        _battleCache.Clear();
        foreach (var d in data.Get("types", new List<TypeDto>()))
        {
            if (d.Id is null || d.Base is null || Type(d.Id) is not null) continue;
            if (!_content.TryGet<UnitDefinitionBase>(d.Base, out var b) || b.Side != "player") continue;
            if (d.Weapon is { } w && !_content.Contains<WeaponDefinition>(w)) continue;
            if (d.Armour is { } a && !_content.Contains<ArmourDefinition>(a)) continue;
            var items = d.Items ?? new();
            if (items.Any(i => !_content.Contains<ItemDefinitionBase>(i))) continue;
            _types.Add(new UnitType(d.Id, d.Name ?? d.Id, d.Base, d.Weapon, d.Armour, items));
        }
        foreach (var (id, n) in data.Get("roster", new Dictionary<string, long>()))
            if (n > 0 && Type(id) is not null) _roster[id] = n;
        foreach (var d in data.Get("formations", new List<FormationDto>()))
        {
            if (d.Id is null || GetFormation(d.Id) is not null) continue;
            var f = new Formation(d.Id, d.Name ?? d.Id);
            var slots = d.Slots ?? new();
            for (var i = 0; i < Formation.SlotCount && i < slots.Count; i++)
                if (slots[i].Unit is { } u && Type(u) is not null)
                {
                    f.Slots[i].UnitType = u;
                    f.Slots[i].Count = Math.Max(0, slots[i].Count);
                }
            _formations.Add(f);
        }
        TypesChanged?.Invoke();
        FormationsChanged?.Invoke();
        foreach (var (id, n) in _roster) RosterChanged?.Invoke(id, n);
    }

    private string DefaultName(UnitDefinitionBase b, string? weapon, string? armour)
    {
        var gear = new[] { weapon, armour }.Where(x => !string.IsNullOrEmpty(x)).Select(x => StockIds.NameOf(_content, x!)).ToList();
        return gear.Count == 0 ? b.Name : $"{b.Name} ({string.Join(", ", gear)})";
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    public sealed class TypeDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Base { get; set; }
        public string? Weapon { get; set; }
        public string? Armour { get; set; }
        public List<string>? Items { get; set; }
    }

    public sealed class SlotDto
    {
        public string? Unit { get; set; }
        public long Count { get; set; }
    }

    public sealed class FormationDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public List<SlotDto>? Slots { get; set; }
    }
}
