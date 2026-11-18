namespace LittleLich.Content.Definitions;

/// <summary>
/// Family base for the tracked, capped resources: mana, gold, souls and the elements fire, water, air, earth.
/// Everything gathered or crafted is an item (ItemDefinitionBase) in the inventory instead.
/// Phase 11 finalises the schema; phase 1 needs just enough to load sample data.
/// </summary>
public abstract class ResourceDefinitionBase : Definition
{
    /// <summary>Storage cap before Magical Storage upgrades.</summary>
    public long BaseCap { get; init; }

    /// <summary>Gold per unit at the Undead Market. 0 = cannot be sold. Placeholder until phase 11.</summary>
    public long SellPrice { get; init; }

    /// <summary>UI colour (hex, e.g. "#7B9CFF") for the resource box and its icon. Optional.</summary>
    public string? Color { get; init; }

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (BaseCap < 0) errors.Add("base_cap", "must be 0 or more");
        if (SellPrice < 0) errors.Add("sell_price", "must be 0 or more");
        if (Color is { } c && !System.Text.RegularExpressions.Regex.IsMatch(c, "^#[0-9A-Fa-f]{6}$")) errors.Add("color", "must be a hex colour like #7B9CFF");
    }
}

/// <summary>JSON type "resource".</summary>
public sealed class ResourceDefinition : ResourceDefinitionBase { }
