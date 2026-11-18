using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleLich.Content.Definitions;

/// <summary>
/// Family base for purchasable upgrades (Data/upgrades.json): Workshop building levels,
/// bought with gold (and other stock). Level 1 is free; the cost of going from level L to L + 1 is
/// cost x cost_growth^(L - 1) for each entry, rounded. <see cref="MaxLevel"/> is the cap before research bonuses.
/// </summary>
public abstract class UpgradeDefinitionBase : Definition
{
    public int MaxLevel { get; init; } = 10;

    /// <summary>Cost of the first upgrade (level 1 to 2): id -> count (resources or items).</summary>
    public Dictionary<string, long> Cost { get; init; } = new();

    public double CostGrowth { get; init; } = 1.5;

    /// <summary>What going from <paramref name="level"/> to level + 1 costs.</summary>
    public IReadOnlyList<ItemAmount> CostAt(int level)
    {
        var f = Math.Pow(CostGrowth, Math.Max(0, level - 1));
        return Cost.Select(c =>
        {
            var v = c.Value * f;
            return new ItemAmount { Id = c.Key, Count = v >= long.MaxValue ? long.MaxValue : (long)Math.Round(v) };
        }).ToList();
    }

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (MaxLevel < 1) errors.Add("max_level", "must be 1 or more");
        if (Cost.Count == 0) errors.Add("cost", "needs at least one cost");
        foreach (var (id, n) in Cost) if (n < 1) errors.Add($"cost.{id}", "must be 1 or more");
        if (!(CostGrowth >= 1)) errors.Add("cost_growth", "must be 1 or more");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        foreach (var id in Cost.Keys.Where(id => !StockIds.Exists(db, id)))
            errors.Add($"cost.{id}", $"no resource or item with id '{id}'");
    }
}

/// <summary>JSON type "building_upgrade": the level of one Workshop building (each building has exactly one).</summary>
public sealed class BuildingUpgradeDefinition : UpgradeDefinitionBase
{
    public string Building { get; init; } = "";

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        base.ValidateReferences(db, errors);
        if (!db.Contains<BuildingDefinitionBase>(Building)) errors.Add("building", $"no building '{Building}'");
        else if (db.All<BuildingUpgradeDefinition>().Count(u => u.Building == Building) > 1)
            errors.Add("building", $"'{Building}' has more than one building_upgrade");
    }
}

