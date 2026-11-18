using System.Text.Json.Serialization;
using System.Linq;
using LittleLich.Core;

namespace LittleLich.Content.Definitions;

/// <summary>Family base for skill activities (a Mining seam, a fishing spot ...).</summary>
public abstract class ActivityDefinitionBase : Definition
{
    /// <summary>Skill id this activity belongs to (see <see cref="SkillIds"/>).</summary>
    public string Skill { get; init; } = "";

    [JsonPropertyName("level")]
    public int RequiredLevel { get; init; } = 1;

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (RequiredLevel < 1 || RequiredLevel > 999) errors.Add("level", "must be 1 to 999");
    }
}

/// <summary>
/// JSON type "gathering_activity" (Data/activities/&lt;skill&gt;.json): { id, name, skill, level, rate_per_tick, xp_per_unit, output }.
/// Each assigned necromancer gathers rate_per_tick units of <see cref="Output"/> per tick (fractions accumulate),
/// giving xp_per_unit per unit.
/// </summary>
public sealed class GatheringActivityDefinition : ActivityDefinitionBase
{
    /// <summary>Units per tick per necromancer, before modifiers (JSON rate_per_tick).</summary>
    [JsonPropertyName("rate_per_tick")]
    public double Rate { get; init; }

    /// <summary>Skill XP per unit gathered (JSON xp_per_unit).</summary>
    [JsonPropertyName("xp_per_unit")]
    public double Xp { get; init; }

    /// <summary>Resource or item id produced.</summary>
    public string Output { get; init; } = "";

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (!SkillIds.Gathering.Contains(Skill))
            errors.Add("skill", $"'{Skill}' is not a gathering skill ({string.Join(", ", SkillIds.Gathering)})");
        if (!(Rate > 0) || Rate > 1_000_000) errors.Add("rate_per_tick", "must be above 0 and at most 1,000,000");
        if (!(Xp >= 0)) errors.Add("xp_per_unit", "must be 0 or more");
        if (string.IsNullOrWhiteSpace(Output)) errors.Add("output", "is missing");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        if (!StockIds.Exists(db, Output)) errors.Add("output", $"no resource or item with id '{Output}'");
    }
}
