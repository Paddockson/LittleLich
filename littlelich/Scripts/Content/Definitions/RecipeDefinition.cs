using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Core;

namespace LittleLich.Content.Definitions;

/// <summary>Family base for crafting recipes.</summary>
public abstract class RecipeDefinitionBase : Definition
{
    /// <summary>Crafting skill id (smithing, alchemy, enchanting).</summary>
    public string Skill { get; init; } = "";

    [JsonPropertyName("level")]
    public int RequiredLevel { get; init; } = 1;

    [JsonConverter(typeof(ItemAmountMapConverter))]
    public List<ItemAmount> Inputs { get; init; } = new();

    [JsonConverter(typeof(SingleItemAmountConverter))]
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
        if (RequiredLevel < 1 || RequiredLevel > 999) errors.Add("level", "must be 1 to 999");
        if (Inputs.Count == 0) errors.Add("inputs", "needs at least one input");
        foreach (var i in Inputs.Where(i => i.Count < 1)) errors.Add($"inputs.{i.Id}", "must be 1 or more");
        if (string.IsNullOrWhiteSpace(Output.Id)) errors.Add("output", "is missing");
        if (Output.Count < 1) errors.Add($"output.{Output.Id}", "must be 1 or more");
        if (Ticks < 1 || Ticks > 1_000_000) errors.Add("ticks", "must be 1 to 1,000,000");
        if (!(Xp >= 0)) errors.Add("xp", "must be 0 or more");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        foreach (var i in Inputs.Where(i => !StockIds.Exists(db, i.Id)))
            errors.Add($"inputs.{i.Id}", $"no resource or item with id '{i.Id}'");
        if (!StockIds.Exists(db, Output.Id))
            errors.Add($"output.{Output.Id}", $"no resource or item with id '{Output.Id}'");
    }
}

/// <summary>JSON type "recipe".</summary>
public sealed class RecipeDefinition : RecipeDefinitionBase { }
