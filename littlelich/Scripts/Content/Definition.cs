using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LittleLich.Content;

/// <summary>
/// Base of every piece of content loaded from JSON (resources, items, units, recipes ...).
///
/// Content "families" are the abstract classes directly below Definition
/// (e.g. ResourceDefinition, ItemDefinition). Ids are unique within a family,
/// so "iron_ore" can be both a resource and a mining activity.
/// Concrete subclasses are picked by the JSON "type" field.
/// </summary>
public abstract class Definition
{
    private static readonly Regex IdPattern = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>Stable lowercase text id, e.g. "iron_ore". Never changes once shipped.</summary>
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>Icon key; resolved to a texture by the UI. Optional.</summary>
    public string? Icon { get; init; }

    /// <summary>True for numbers that are not balanced yet; must not ship unnoticed.</summary>
    public bool Placeholder { get; init; }

    /// <summary>The JSON "type" this entry was loaded as (set by the loader).</summary>
    [JsonIgnore]
    public string Type { get; internal set; } = "";

    /// <summary>Source file this entry came from (set by the loader, for error messages).</summary>
    [JsonIgnore]
    public string SourceFile { get; internal set; } = "";

    public static bool IsValidId(string? id) => id is not null && IdPattern.IsMatch(id);

    /// <summary>
    /// Field-level checks on this entry alone. Report problems through <paramref name="errors"/>.
    /// Subclasses call base.Validate first.
    /// </summary>
    public virtual void Validate(ContentErrors errors)
    {
        if (string.IsNullOrWhiteSpace(Id))
            errors.Add("id", "is missing");
        else if (!IsValidId(Id))
            errors.Add("id", $"'{Id}' must be lowercase letters, digits and underscores, starting with a letter");

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("name", "is missing");
    }

    /// <summary>
    /// Cross-reference checks, run after every file is loaded (e.g. recipe inputs exist).
    /// Filled in by later phases.
    /// </summary>
    public virtual void ValidateReferences(ContentDatabase db, ContentErrors errors) { }
}
