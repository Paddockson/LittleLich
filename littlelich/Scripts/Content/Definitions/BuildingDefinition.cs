using System.Collections.Generic;
using System.Linq;

namespace LittleLich.Content.Definitions;

/// <summary>Workshop building ids. Each has exactly one class and one definition in Data/buildings.json.</summary>
public static class BuildingIds
{
    public const string ManaWell = "mana_well";
    public const string Infuser = "infuser";
    public const string SoulWell = "soul_well";
    public const string Transmuter = "transmuter";
    public const string SummoningCircle = "summoning_circle";
    public const string UndeadMarket = "undead_market";
    public const string MagicalStorage = "magical_storage";

    /// <summary>Flow order (also the Workshop list order). Magical Storage does not tick.</summary>
    public static readonly IReadOnlyList<string> All = new[] { ManaWell, Infuser, SoulWell, Transmuter, SummoningCircle, UndeadMarket, MagicalStorage };
}

/// <summary>
/// Family base for Workshop buildings. Shared tuning (placeholder numbers in JSON):
///   work per tick = (base_work + staffed necromancers x work_per_necromancer) x (1 + level_bonus x (level - 1))
///   necromancer slots = base_slots + slots_per_level x (level - 1)
///   upgrade cost (gold) = upgrade_cost x upgrade_growth^(level - 1)
/// What one unit of "work" does is up to the building type.
/// </summary>
public abstract class BuildingDefinitionBase : Definition
{
    public int MaxLevel { get; init; } = 100;
    public long UpgradeCost { get; init; }
    public double UpgradeGrowth { get; init; } = 1.5;
    public long BaseSlots { get; init; }
    public long SlotsPerLevel { get; init; }
    public double BaseWork { get; init; }
    public double WorkPerNecromancer { get; init; }
    public double LevelBonus { get; init; }

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (!BuildingIds.All.Contains(Id)) errors.Add("id", $"'{Id}' is not a Workshop building ({string.Join(", ", BuildingIds.All)})");
        if (MaxLevel < 1) errors.Add("max_level", "must be 1 or more");
        if (UpgradeCost < 0) errors.Add("upgrade_cost", "must be 0 or more");
        if (!(UpgradeGrowth >= 1)) errors.Add("upgrade_growth", "must be 1 or more");
        if (BaseSlots < 0 || SlotsPerLevel < 0) errors.Add("base_slots", "slots must be 0 or more");
        if (!(BaseWork >= 0) || !(WorkPerNecromancer >= 0) || !(LevelBonus >= 0)) errors.Add("base_work", "work values must be 0 or more");
    }
}

/// <summary>JSON type "mana_well": one unit of work makes one unit of <see cref="Output"/>.</summary>
public sealed class ManaWellDefinition : BuildingDefinitionBase
{
    public string Output { get; init; } = "mana";

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        if (!db.Contains<ResourceDefinitionBase>(Output)) errors.Add("output", $"no resource '{Output}'");
    }
}

/// <summary>JSON type "converter" (Infuser, Soul Well): <see cref="WorkPerOp"/> work turns the inputs into the output.</summary>
public sealed class ConverterDefinition : BuildingDefinitionBase
{
    public List<ItemAmount> Inputs { get; init; } = new();
    public ItemAmount Output { get; init; } = new();
    public double WorkPerOp { get; init; } = 1;

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (Inputs.Count == 0) errors.Add("inputs", "needs at least one input");
        if (!(WorkPerOp > 0)) errors.Add("work_per_op", "must be above 0");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        foreach (var i in Inputs.Append(Output))
            if (!db.Contains<ResourceDefinitionBase>(i.Id)) errors.Add("inputs", $"no resource '{i.Id}'");
    }
}

/// <summary>
/// JSON type "infuser": <see cref="WorkPerOp"/> work turns <see cref="Input"/> (mana) into one unit of the element the
/// player picks (<see cref="Elements"/>: fire, water, air, earth).
/// </summary>
public sealed class InfuserDefinition : BuildingDefinitionBase
{
    public ItemAmount Input { get; init; } = new();
    public List<string> Elements { get; init; } = new();
    public double WorkPerOp { get; init; } = 1;

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (Elements.Count == 0) errors.Add("elements", "needs at least one element");
        if (!(WorkPerOp > 0)) errors.Add("work_per_op", "must be above 0");
        if (Input.Count < 1) errors.Add("input.count", "must be 1 or more");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        foreach (var id in Elements.Append(Input.Id))
            if (!db.Contains<ResourceDefinitionBase>(id)) errors.Add("elements", $"no resource '{id}'");
    }
}

/// <summary>JSON type "transmuter": runs one chosen <see cref="TransmutationDefinition"/> at a time.</summary>
public sealed class TransmuterDefinition : BuildingDefinitionBase { }

/// <summary>JSON type "summoning_circle": runs one <see cref="SummonDefinition"/>; each level raises max necromancers.</summary>
public sealed class SummoningCircleDefinition : BuildingDefinitionBase
{
    public double MaxNecromancersPerLevel { get; init; }
}

/// <summary>JSON type "undead_market": one unit of work sells one unit of stock at its sell_price.</summary>
public sealed class UndeadMarketDefinition : BuildingDefinitionBase { }

/// <summary>
/// JSON type "magical_storage": no work. Each level adds <see cref="CapBonusPerLevel"/> x base cap to every resource
/// and <see cref="InventorySlotsPerLevel"/> inventory slots.
/// </summary>
public sealed class MagicalStorageDefinition : BuildingDefinitionBase
{
    public double CapBonusPerLevel { get; init; }

    /// <summary>Inventory slots at level 1 (phase 6).</summary>
    public long BaseInventorySlots { get; init; } = 40;

    /// <summary>Extra inventory slots per level above 1.</summary>
    public long InventorySlotsPerLevel { get; init; }
}

/// <summary>Family base for Transmuter recipes (resources and materials only in v0.1).</summary>
public abstract class TransmutationDefinitionBase : Definition
{
    public List<ItemAmount> Inputs { get; init; } = new();
    public ItemAmount Output { get; init; } = new();
    /// <summary>Work needed per conversion.</summary>
    public double Work { get; init; } = 1;

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (Inputs.Count == 0) errors.Add("inputs", "needs at least one input");
        if (Inputs.Any(i => i.Count < 1) || Output.Count < 1) errors.Add("inputs", "counts must be 1 or more");
        if (!(Work > 0)) errors.Add("work", "must be above 0");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        // v0.1 decision: the Transmuter converts resources and materials only (gear, potions, bombs and runes come
        // from Smithing, Alchemy and Enchanting).
        foreach (var i in Inputs.Append(Output))
            if (!db.Contains<ResourceDefinitionBase>(i.Id) && !db.Contains<MaterialDefinition>(i.Id))
                errors.Add("inputs", $"'{i.Id}' is not a resource or material (the Transmuter converts resources and materials only in v0.1)");
    }
}

/// <summary>JSON type "transmutation".</summary>
public sealed class TransmutationDefinition : TransmutationDefinitionBase { }

/// <summary>Family base for Summoning Circle recipes. v0.1 summons necromancers only; units come in phase 9.</summary>
public abstract class SummonDefinitionBase : Definition
{
    public static readonly IReadOnlySet<string> Produces = new HashSet<string> { "necromancer" };

    public List<ItemAmount> Costs { get; init; } = new();
    /// <summary>What it summons: "necromancer" (units later).</summary>
    public string Summons { get; init; } = "necromancer";
    /// <summary>How many per summon.</summary>
    public long Count { get; init; } = 1;
    /// <summary>Work needed per summon.</summary>
    public double Work { get; init; } = 1;

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (!Produces.Contains(Summons)) errors.Add("summons", $"'{Summons}' cannot be summoned yet (necromancer only until phase 9)");
        if (Count < 1) errors.Add("count", "must be 1 or more");
        if (!(Work > 0)) errors.Add("work", "must be above 0");
        if (Costs.Any(c => c.Count < 1)) errors.Add("costs", "counts must be 1 or more");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        foreach (var c in Costs)
            if (!StockIds.Exists(db, c.Id)) errors.Add("costs", $"no resource or item '{c.Id}'");
    }
}

/// <summary>JSON type "summon".</summary>
public sealed class SummonDefinition : SummonDefinitionBase { }
