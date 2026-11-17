using System.Collections.Generic;

namespace LittleLich.Content.Definitions;

/// <summary>
/// Family base for items held in the inventory (gear, potions, bombs, runes). Resources live in Storage instead.
/// Placeholder schema from phase 4 (crafting outputs); phase 6 adds slots/stats via subclasses and phase 11 finalises it.
/// Item ids must not clash with resource ids: recipes refer to "stock" by id without saying which.
/// </summary>
public abstract class ItemDefinitionBase : Definition
{
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string> { "weapon", "armour", "potion", "bomb", "rune", "material" };

    /// <summary>weapon, armour, potion, bomb, rune or material.</summary>
    public string Kind { get; init; } = "";

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (!Kinds.Contains(Kind))
            errors.Add("kind", $"'{Kind}' is not one of: {string.Join(", ", Kinds)}");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        if (db.Contains<ResourceDefinitionBase>(Id))
            errors.Add("id", $"'{Id}' is also a resource id; items and resources share one id space for recipes");
    }
}

/// <summary>JSON type "item".</summary>
public sealed class ItemDefinition : ItemDefinitionBase { }

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

    public static string NameOf(ContentDatabase db, string id) =>
        db.TryGet<ResourceDefinitionBase>(id, out var r) ? r.Name :
        db.TryGet<ItemDefinitionBase>(id, out var i) ? i.Name : id;
}
