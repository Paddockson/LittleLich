using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using LittleLich.Core;

namespace LittleLich.Content.Definitions;

/// <summary>
/// Family base for items held in the inventory (gear, potions, bombs, runes, materials). Resources live in Storage.
/// The JSON "type" picks the subclass: weapon, armour, accessory, rune, potion, bomb, material.
/// Item ids must not clash with resource ids: recipes refer to "stock" by id without saying which.
/// Phase 6 schema; phase 11 fills in the real content.
/// </summary>
public abstract class ItemDefinitionBase : Definition
{
    /// <summary>Items per inventory slot.</summary>
    public int StackSize { get; init; } = 100;

    /// <summary>Coins per item at the Undead Market. 0 = cannot be sold. Placeholder until phase 11.</summary>
    public long SellPrice { get; init; }

    /// <summary>The JSON type ("weapon", "potion" ...), for display and filtering.</summary>
    [JsonIgnore]
    public string Kind => Type;

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (StackSize < 1) errors.Add("stack_size", "must be 1 or more");
        if (SellPrice < 0) errors.Add("sell_price", "must be 0 or more");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        if (db.Contains<ResourceDefinitionBase>(Id))
            errors.Add("id", $"'{Id}' is also a resource id; items and resources share one id space for recipes");
    }
}

/// <summary>A stat change in content: { "stat": "max_health", "op": "add", "value": 15 }.</summary>
public sealed class StatModifierDef
{
    public string Stat { get; init; } = "";
    /// <summary>"add" or "mul".</summary>
    public string Op { get; init; } = "add";
    public double Value { get; init; }

    public bool IsMul => Op == "mul";

    public override string ToString() => IsMul ? $"{Stat} x{Value:0.###}" : $"{Stat} {(Value >= 0 ? "+" : "")}{Value:0.###}";

    internal static void Validate(IReadOnlyList<StatModifierDef> mods, string field, ContentErrors errors)
    {
        for (var i = 0; i < mods.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(mods[i].Stat)) errors.Add($"{field}[{i}].stat", "is missing");
            if (mods[i].Op is not ("add" or "mul")) errors.Add($"{field}[{i}].op", "must be add or mul");
            if (!double.IsFinite(mods[i].Value)) errors.Add($"{field}[{i}].value", "must be a number");
        }
    }
}

/// <summary>
/// Items the lich can wear. <see cref="Slots"/> lists the lich slots it fits (empty = unit gear only, phase 9);
/// <see cref="Modifiers"/> apply to the lich while worn.
/// </summary>
public abstract class EquippableDefinitionBase : ItemDefinitionBase
{
    public List<string> Slots { get; init; } = new();
    public List<StatModifierDef> Modifiers { get; init; } = new();

    public bool FitsLich => Slots.Count > 0;

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        foreach (var s in Slots.Where(s => !LichSlots.All.Contains(s)))
            errors.Add("slots", $"'{s}' is not a lich slot ({string.Join(", ", LichSlots.All)})");
        StatModifierDef.Validate(Modifiers, "modifiers", errors);
        if (Modifiers.Count > 0 && Slots.Count == 0) errors.Add("modifiers", "lich modifiers need at least one lich slot");
    }
}

/// <summary>JSON type "weapon". <see cref="Damage"/> is for units (phase 9); lich weapons (staff) use slots + modifiers.</summary>
public sealed class WeaponDefinition : EquippableDefinitionBase
{
    public double Damage { get; init; }
}

/// <summary>JSON type "armour". <see cref="Defence"/> is for units (phase 9).</summary>
public sealed class ArmourDefinition : EquippableDefinitionBase
{
    public double Defence { get; init; }
}

/// <summary>JSON type "accessory": amulets, rings, grimoires.</summary>
public sealed class AccessoryDefinition : EquippableDefinitionBase { }

/// <summary>JSON type "rune". Attaching runes to gear is out of scope until later; <see cref="Trait"/> names its effect.</summary>
public sealed class RuneDefinition : ItemDefinitionBase
{
    /// <summary>Enchantment trait such as rot, fleet, ward.</summary>
    public string Trait { get; init; } = "";
}

/// <summary>
/// JSON type "potion": used from the inventory by the lich. Heals <see cref="Heal"/> and applies
/// <see cref="Effects"/> as timed modifiers for <see cref="DurationTicks"/> (using it again refreshes the duration).
/// </summary>
public sealed class PotionDefinition : ItemDefinitionBase
{
    public double Heal { get; init; }
    public List<StatModifierDef> Effects { get; init; } = new();
    public long DurationTicks { get; init; }

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        StatModifierDef.Validate(Effects, "effects", errors);
        if (Heal < 0) errors.Add("heal", "must be 0 or more");
        if (Effects.Count > 0 && DurationTicks < 1) errors.Add("duration_ticks", "effects need a duration of 1 tick or more");
        if (Effects.Count == 0 && Heal == 0) errors.Add("effects", "a potion needs heal or effects");
    }
}

/// <summary>JSON type "bomb": a battle item (phase 8).</summary>
public sealed class BombDefinition : ItemDefinitionBase
{
    public double Damage { get; init; }
}

/// <summary>JSON type "material": crafting-only items.</summary>
public sealed class MaterialDefinition : ItemDefinitionBase { }

/// <summary>An id and a count, used by recipes ("copper_ore" x 2).</summary>
public sealed class ItemAmount
{
    public string Id { get; init; } = "";
    public long Count { get; init; } = 1;

    public override string ToString() => $"{Id} x{Count}";
}

/// <summary>Lookups across the two "stock" families (resources in Storage, items in the inventory).</summary>
public static class StockIds
{
    public static bool Exists(ContentDatabase db, string id) =>
        db.Contains<ResourceDefinitionBase>(id) || db.Contains<ItemDefinitionBase>(id);

    public static bool IsResource(ContentDatabase db, string id) => db.Contains<ResourceDefinitionBase>(id);

    /// <summary>Undead Market price (0 = not for sale).</summary>
    public static long SellPriceOf(ContentDatabase db, string id) =>
        db.TryGet<ResourceDefinitionBase>(id, out var r) ? r.SellPrice :
        db.TryGet<ItemDefinitionBase>(id, out var i) ? i.SellPrice : 0;

    public static string NameOf(ContentDatabase db, string id) =>
        db.TryGet<ResourceDefinitionBase>(id, out var r) ? r.Name :
        db.TryGet<ItemDefinitionBase>(id, out var i) ? i.Name : id;
}
