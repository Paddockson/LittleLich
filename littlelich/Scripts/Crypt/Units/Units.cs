using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>The base has no weapon slot (weapon_slots is empty).</summary>
    NoWeaponSlot,
    /// <summary>The base has no armour slot (armour_slots is empty).</summary>
    NoArmourSlot,
}

/// <summary>
/// The Unit Builder (save section "units"), owned by the CryptManager: custom unit types (a base body plus a
/// loadout) and the army formation (adjustment 03, <see cref="Army"/>, save section "army").
///
/// Units are named by id: a player base id ("skeleton", a base unit, available once unlocked) or a custom type id
/// built here ("skeleton_copper_sword"). The lich summons figures into the formation's squads
/// (<see cref="LichSummoner"/>); each figure pays, from the stock: the base's summon_cost, one weapon, one armour and
/// one of each item. There is no roster: the formation is the army.
///
/// Battle stats for a unit (placeholder rules): HP = base hp + armour defence x 5; every damage action does
/// + weapon damage. A base takes a weapon / armour only if it has a weapon / armour slot. Items are used up but have
/// no battle effect yet (runes later).
/// </summary>
public sealed class UnitBuilder : ISaveable
{
    public const string Mana = "mana";
    public const double HpPerDefence = 5;

    private readonly ContentDatabase _content;
    private readonly Stockpile _stock;
    private readonly Unlocks _unlocks;
    private readonly List<UnitType> _types = new();
    private readonly Dictionary<string, World.BattleUnit> _battleCache = new(StringComparer.Ordinal);

    public UnitBuilder(ContentDatabase content, Stockpile stock, Unlocks unlocks)
    {
        _content = content;
        _stock = stock;
        _unlocks = unlocks;
        Army = new ArmyFormation { Exists = IsKnownUnit };
        Army.Loaded = NotifyArmyLoaded;
        unlocks.Unlocked += (kind, _) => { if (kind == Unlocks.Unit) UnlockedUnitsChanged?.Invoke(); };
    }

    public string SaveKey => "units";

    public IReadOnlyList<UnitType> Types => _types;

    /// <summary>The 3 by 3 formation (its own saveable; register it after this one so unit types exist when it loads).</summary>
    public ArmyFormation Army { get; }

    public event Action? TypesChanged;
    /// <summary>A slot's unit changed (set, cleared, swapped, loaded): redraw the slots and the details box.</summary>
    public event Action? FormationChanged;
    /// <summary>The unlocked unit lists changed (research unlocked a base, a custom type was built or removed).</summary>
    public event Action? UnlockedUnitsChanged;
    /// <summary>Figures in any squad changed (summons, battle losses).</summary>
    public event Action? FiguresChanged;
    /// <summary>(slot, figures added) after the lich summons into a slot; the world adds them to a running battle.</summary>
    public event Action<int, long>? FiguresSummoned;

    /// <summary>Player base bodies, in content order.</summary>
    public IEnumerable<UnitDefinitionBase> Bases => _content.All<UnitDefinitionBase>().Where(u => u.Side == "player");

    public bool IsBaseLocked(string baseId) => _unlocks.IsLocked(Unlocks.Unit, baseId);

    public UnitType? Type(string id) => _types.FirstOrDefault(t => t.Id == id);

    /// <summary>Set by the world: true while the army is out fighting (the formation cannot be changed then).</summary>
    public Func<bool> ArmyLocked { get; set; } = () => false;

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
        if (weapon is { Length: > 0 } && b.WeaponSlots.Count == 0) return (BuildResult.NoWeaponSlot, null);
        if (armour is { Length: > 0 } && b.ArmourSlots.Count == 0) return (BuildResult.NoArmourSlot, null);
        if (itemList.Any(i => !_content.Contains<ItemDefinitionBase>(i) || _content.Contains<EquippableDefinitionBase>(i))) return (BuildResult.BadItem, null);

        var id = UnitType.IdFor(baseId, weapon, armour, itemList);
        if (Type(id) is { } existing) return (BuildResult.Ok, existing);
        var t = new UnitType(id, name ?? DefaultName(b, weapon, armour), baseId, NullIfEmpty(weapon), NullIfEmpty(armour), itemList);
        _types.Add(t);
        TypesChanged?.Invoke();
        UnlockedUnitsChanged?.Invoke();
        return (BuildResult.Ok, t);
    }

    /// <summary>Deletes a type that no formation slot holds. False otherwise.</summary>
    public bool Remove(string typeId)
    {
        if (Type(typeId) is not { } t || Army.Slots.Contains(typeId)) return false;
        _types.Remove(t);
        _battleCache.Remove(typeId);
        TypesChanged?.Invoke();
        UnlockedUnitsChanged?.Invoke();
        return true;
    }

    // ---- Units (base ids and custom types) ----

    /// <summary>A built type, or the gear-less loadout of a player base (not stored as a type).</summary>
    private UnitType? TypeOrBare(string id)
    {
        if (Type(id) is { } t) return t;
        return _content.TryGet<UnitDefinitionBase>(id, out var b) && b.IsPlayer ? new UnitType(id, b.Name, id, null, null, Array.Empty<string>()) : null;
    }

    /// <summary>A built type or a player base (saved slots holding anything else are dropped).</summary>
    public bool IsKnownUnit(string id) => TypeOrBare(id) is not null;

    /// <summary>The unit exists and its base body is not locked by research.</summary>
    public bool IsUnlockedUnit(string id) => TypeOrBare(id) is { } t && !IsBaseLocked(t.Base);

    /// <summary>Built in the Unit Builder (it has gear or items), as opposed to a base unit.</summary>
    public bool IsCustom(string id) => Type(id) is { } t && t.Id != t.Base;

    /// <summary>Unlocked base unit ids (custom = false) or unlocked custom unit ids (custom = true), in content / build order.</summary>
    public IReadOnlyList<string> GetUnlockedUnitIds(bool custom) => custom
        ? _types.Where(t => t.Id != t.Base && !IsBaseLocked(t.Base)).Select(t => t.Id).ToList()
        : Bases.Where(b => !IsBaseLocked(b.Id)).Select(b => b.Id).ToList();

    public string UnitName(string id) => TypeOrBare(id)?.Name ?? id;

    /// <summary>Melee, Ranged or Magic, from the base body's role.</summary>
    public string UnitClass(string id) => TypeOrBare(id) is { } t && _content.TryGet<UnitDefinitionBase>(t.Base, out var b) ? ClassOf(b.Role) : "";

    public static string ClassOf(string role) => role switch
    {
        "melee" or "tank" or "swarm" => "Melee",
        "ranged" => "Ranged",
        _ => "Magic",
    };

    /// <summary>The Details box view of a unit (base id or custom type id).</summary>
    public UnitDetails GetUnitDetails(string unitId)
    {
        var t = TypeOrBare(unitId) ?? throw new ArgumentException($"No unit '{unitId}'.", nameof(unitId));
        var b = _content.Get<UnitDefinitionBase>(t.Base);
        var battle = BattleUnit(unitId);
        var attack = battle.Actions.FirstOrDefault(a => a.Effect == "damage");
        var custom = IsCustom(unitId);
        var gear = new[] { t.Weapon, t.Armour }.OfType<string>().Concat(t.Items).Select(x => StockIds.NameOf(_content, x).ToLowerInvariant());
        return new UnitDetails
        {
            Id = unitId,
            Name = t.Name,
            Type = ClassOf(b.Role),
            IsCustom = custom,
            BuiltFrom = custom ? string.Join(" + ", gear.Prepend(b.Name)) : null,
            Health = battle.Hp,
            Damage = attack?.Value ?? 0,
            AttackSpeed = attack?.Cooldown ?? 0,
            Actions = battle.Actions.Take(UnitDefinitionBase.MaxActions)
                .Select(a => new UnitAction { Name = a.Name, CooldownSeconds = a.Cooldown, Description = Describe(a) }).ToList(),
            SummonCostText = string.Join(", ", CostPerUnit(unitId).Select(c =>
            {
                var name = StockIds.NameOf(_content, c.Id).ToLowerInvariant();
                return c.Count == 1 ? name : $"{c.Count} {name}";
            })),
            Stock = Affordable(unitId).Count,
        };
    }

    /// <summary>One-line action description from its effect (placeholder until action JSON carries its own text).</summary>
    public static string Describe(World.BattleUnitAction a)
    {
        var v = a.Value.ToString("0.##", CultureInfo.InvariantCulture);
        return a.Effect switch
        {
            "damage" => $"{v} damage to {TargetText(a.Target)}{(a.IsMelee ? "" : ", from range")}",
            "reduce_next_incoming" => $"Next hit taken does {(a.Value * 100).ToString("0.#", CultureInfo.InvariantCulture)}% less",
            "heal" => a.Target == "self" ? $"Heals itself for {v}" : $"Heals the most wounded ally for {v}",
            _ => a.Effect,
        };
    }

    private static string TargetText(string target) => target switch
    {
        "front" => "the front line",
        "back" => "the back line",
        "lowest" => "the weakest enemy",
        _ => target,
    };

    // ---- Formation (adjustment 03) ----

    /// <summary>The unit id in a slot, or null (also for a bad index).</summary>
    public string? GetSlot(int index) => ArmyFormation.IsSlot(index) ? Army.Slots[index] : null;

    /// <summary>Figures standing in a slot's squad.</summary>
    public long Figures(int index) => ArmyFormation.IsSlot(index) ? Army.Figures[index] : 0;

    /// <summary>How many slots hold this unit.</summary>
    public int CountInFormation(string unitId) => Army.Slots.Count(s => s == unitId);

    /// <summary>
    /// Puts a unit in a slot (the same unit may fill several slots). A new unit starts with 0 figures; the lich fills
    /// the squad. False for a bad slot, a unit that is not unlocked, or while the army is fighting.
    /// </summary>
    public bool SetSlot(int index, string unitId)
    {
        if (!ArmyFormation.IsSlot(index) || string.IsNullOrEmpty(unitId) || !IsUnlockedUnit(unitId) || ArmyLocked()) return false;
        if (Army.Slots[index] == unitId) return true;
        Army.Slots[index] = unitId;
        Army.Figures[index] = 0;
        FormationChanged?.Invoke();
        FiguresChanged?.Invoke();
        return true;
    }

    /// <summary>Empties a slot (its figures go). Nothing while the army is fighting.</summary>
    public void ClearSlot(int index)
    {
        if (!ArmyFormation.IsSlot(index) || Army.Slots[index] is null || ArmyLocked()) return;
        Army.Slots[index] = null;
        Army.Figures[index] = 0;
        FormationChanged?.Invoke();
        FiguresChanged?.Invoke();
    }

    /// <summary>Swaps two slots, squads and all (an empty slot is a valid value, so this also moves). Nothing while fighting.</summary>
    public void SwapSlots(int a, int b)
    {
        if (!ArmyFormation.IsSlot(a) || !ArmyFormation.IsSlot(b) || a == b || ArmyLocked()) return;
        if (Army.Slots[a] is null && Army.Slots[b] is null) return;
        (Army.Slots[a], Army.Slots[b]) = (Army.Slots[b], Army.Slots[a]);
        (Army.Figures[a], Army.Figures[b]) = (Army.Figures[b], Army.Figures[a]);
        FormationChanged?.Invoke();
        FiguresChanged?.Invoke();
    }

    /// <summary>
    /// The lich summons up to <paramref name="count"/> figures into a slot, paying each from the stock. Limited by the
    /// squad's room and the stock. Returns how many were added.
    /// </summary>
    public long SummonIntoSlot(int index, long count)
    {
        if (!ArmyFormation.IsSlot(index) || Army.Slots[index] is not { } unit || !IsUnlockedUnit(unit)) return 0;
        var n = Math.Min(Math.Min(count, Army.Missing(index)), Affordable(unit).Count);
        if (n <= 0) return 0;
        Pay(unit, n);
        Army.Figures[index] += n;
        FiguresSummoned?.Invoke(index, n);
        FiguresChanged?.Invoke();
        return n;
    }

    /// <summary>Debug: fills every filled slot's squad to five figures without paying.</summary>
    public void DebugFillSquads()
    {
        for (var i = 0; i < ArmyFormation.SlotCount; i++)
        {
            if (Army.Slots[i] is null || Army.Missing(i) <= 0) continue;
            var n = Army.Missing(i);
            Army.Figures[i] = ArmyFormation.SquadSize;
            FiguresSummoned?.Invoke(i, n);
        }
        FiguresChanged?.Invoke();
    }

    /// <summary>The world writes battle losses back to the squads.</summary>
    internal void SyncFigures(int index, long figures)
    {
        if (!ArmyFormation.IsSlot(index) || Army.Slots[index] is null) return;
        var n = Math.Clamp(figures, 0, ArmyFormation.SquadSize);
        if (Army.Figures[index] == n) return;
        Army.Figures[index] = n;
        FiguresChanged?.Invoke();
    }

    /// <summary>After the army section loads (it loads after this one): pages redraw.</summary>
    private void NotifyArmyLoaded()
    {
        FormationChanged?.Invoke();
        FiguresChanged?.Invoke();
    }

    // ---- Costs ----

    /// <summary>What one figure of the unit costs: the base's summon_cost, then weapon, armour and items.</summary>
    public IReadOnlyList<ItemAmount> CostPerUnit(string unitId)
    {
        var t = TypeOrBare(unitId) ?? throw new ArgumentException($"No unit '{unitId}'.", nameof(unitId));
        var cost = BaseCost(t.Base).ToList();
        foreach (var id in new[] { t.Weapon, t.Armour }.OfType<string>().Concat(t.Items).GroupBy(x => x))
        {
            var i = cost.FindIndex(c => c.Id == id.Key);
            if (i >= 0) cost[i] = new ItemAmount { Id = id.Key, Count = cost[i].Count + id.Count() };
            else cost.Add(new ItemAmount { Id = id.Key, Count = id.Count() });
        }
        return cost;
    }

    /// <summary>A base body's own summon_cost (no gear), after research cost cuts (rounded up).</summary>
    public IReadOnlyList<ItemAmount> BaseCost(string baseId)
    {
        var m = CostMultiplier(baseId);
        return _content.Get<UnitDefinitionBase>(baseId).SummonCost
            .Select(c => new ItemAmount { Id = c.Id, Count = Math.Abs(m - 1) < 1e-12 ? c.Count : ResearchStats.Scaled(c.Count, m) }).ToList();
    }

    /// <summary>Research cost multiplier per base body (Bring Bones, Cheap Bones on skeletons). Set by the CryptManager; 1 by default.</summary>
    public Func<string, double> CostMultiplier { get; set; } = _ => 1;

    /// <summary>How many figures the current stock pays for, and the first input that limits it.</summary>
    public (long Count, string? Limiting) Affordable(string unitId)
    {
        long can = long.MaxValue;
        string? limiting = null;
        foreach (var c in CostPerUnit(unitId))
        {
            var n = _stock.Get(c.Id) / c.Count;
            if (n < can) { can = n; limiting = c.Id; }
        }
        return (can, limiting);
    }

    private void Pay(string unitId, long count)
    {
        foreach (var c in CostPerUnit(unitId)) _stock.TryRemove(c.Id, c.Count * count);
    }

    // ---- Battle stats ----

    /// <summary>The unit as a battle unit: base stats plus its gear (placeholder rules, see the class summary).</summary>
    public World.BattleUnit BattleUnit(string unitId)
    {
        if (_battleCache.TryGetValue(unitId, out var cached)) return cached;
        var t = TypeOrBare(unitId) ?? throw new ArgumentException($"No unit '{unitId}'.", nameof(unitId));
        var b = _content.Get<UnitDefinitionBase>(t.Base);
        var damage = t.Weapon is { } w ? _content.Get<WeaponDefinition>(w).Damage : 0;
        var defence = t.Armour is { } a ? _content.Get<ArmourDefinition>(a).Defence : 0;
        var u = new World.BattleUnit(t.Id, t.Name, b.Hp + defence * HpPerDefence,
            b.ResolvedActions.Select(x => World.BattleUnitAction.From(x, damage)).ToList());
        _battleCache[unitId] = u;
        return u;
    }

    public UnitDefinitionBase BaseOf(string unitId) => _content.Get<UnitDefinitionBase>(TypeOrBare(unitId)!.Base);

    // ---- Save ----

    public SaveData Save() => new SaveData()
        .Set("types", _types.Select(t => new TypeDto { Id = t.Id, Name = t.Name, Base = t.Base, Weapon = t.Weapon, Armour = t.Armour, Items = t.Items.ToList() }).ToList());

    /// <summary>
    /// Types whose base or gear no longer exists are dropped. Saves from before adjustment 03 also hold a "roster"
    /// and 8-slot "formations": both are ignored (the 3 by 3 army replaces them and the lich refills it).
    /// </summary>
    public void Load(SaveData data)
    {
        _types.Clear();
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
        TypesChanged?.Invoke();
        UnlockedUnitsChanged?.Invoke();
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
}
