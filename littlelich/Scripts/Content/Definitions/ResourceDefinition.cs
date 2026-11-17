namespace LittleLich.Content.Definitions;

/// <summary>
/// Family base for stored, capped resources (mana, souls, iron_ore, coins ...).
/// Phase 11 finalises the schema; phase 1 needs just enough to load sample data.
/// </summary>
public abstract class ResourceDefinitionBase : Definition
{
    /// <summary>Storage cap before Magical Storage upgrades.</summary>
    public long BaseCap { get; init; }

    /// <summary>Coins per unit at the Undead Market. 0 = cannot be sold. Placeholder until phase 11.</summary>
    public long SellPrice { get; init; }

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (BaseCap < 0) errors.Add("base_cap", "must be 0 or more");
        if (SellPrice < 0) errors.Add("sell_price", "must be 0 or more");
    }
}

/// <summary>JSON type "resource".</summary>
public sealed class ResourceDefinition : ResourceDefinitionBase { }
