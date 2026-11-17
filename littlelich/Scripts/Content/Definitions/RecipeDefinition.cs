using System.Collections.Generic;
using System.Linq;
using LittleLich.Core;

namespace LittleLich.Content.Definitions;

/// <summary>Family base for crafting recipes.</summary>
public abstract class RecipeDefinitionBase : Definition
{
    /// <summary>Crafting skill id (smithing, alchemy, enchanting).</summary>
    public string Skill { get; init; } = "";

    public int RequiredLevel { get; init; } = 1;

    public List<ItemAmount> Inputs { get; init; } = new();

    public ItemAmount Output { get; init; } = new();

    /// <summary>Ticks one necromancer needs per craft, before speed modifiers.</summary>
    public long Ticks { get; init; } = 1;

    /// <summary>Skill XP per craft.</summary>
    public double Xp { get; init; }

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (!SkillIds.Crafting.Contains(Skill))
            errors.Add("skill", $"'{Skill}' is not a crafting skill ({string.Join(", ", SkillIds.Crafting)})");
        if (RequiredLevel < 1 || RequiredLevel > 999) errors.Add("required_level", "must be 1 to 999");
        if (Inputs.Count == 0) errors.Add("inputs", "needs at least one input");
        for (var i = 0; i < Inputs.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(Inputs[i].Id)) errors.Add($"inputs[{i}].id", "is missing");
            if (Inputs[i].Count < 1) errors.Add($"inputs[{i}].count", "must be 1 or more");
        }
        if (Inputs.GroupBy(x => x.Id).Any(g => g.Count() > 1)) errors.Add("inputs", "lists the same id twice");
        if (string.IsNullOrWhiteSpace(Output.Id)) errors.Add("output.id", "is missing");
        if (Output.Count < 1) errors.Add("output.count", "must be 1 or more");
        if (Ticks < 1 || Ticks > 1_000_000) errors.Add("ticks", "must be 1 to 1,000,000");
        if (!(Xp >= 0)) errors.Add("xp", "must be 0 or more");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        for (var i = 0; i < Inputs.Count; i++)
            if (!StockIds.Exists(db, Inputs[i].Id))
                errors.Add($"inputs[{i}].id", $"no resource or item with id '{Inputs[i].Id}'");
        if (!StockIds.Exists(db, Output.Id))
            errors.Add("output.id", $"no resource or item with id '{Output.Id}'");
    }
}

/// <summary>JSON type "recipe".</summary>
public sealed class RecipeDefinition : RecipeDefinitionBase { }
