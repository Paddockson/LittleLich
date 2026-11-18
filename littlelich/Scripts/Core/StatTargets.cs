using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleLich.Core;

/// <summary>
/// The modifier target filter (research revamp). A targeted modifier is stored under "stat@target"; a system reads
/// a stat for one subject (a building id, "research_bench", a unit id, an item id) and gets the untargeted
/// modifiers plus every targeted one whose target covers that subject.
///
/// Subjects: Workshop building ids, <see cref="ResearchBench"/>, unit ids (tagged "skeleton" / "wisp" through
/// <see cref="UnitTags"/>), item ids, and spell kinds. Targets starting "per_necromancer_in_" are read per assigned
/// necromancer by the building (the value is multiplied by its staff).
/// </summary>
public static class StatTargets
{
    public const string ResearchBench = "research_bench";
    public const char Separator = '@';

    private static readonly string[] Buildings =
        { "mana_well", "infuser", "soul_well", "transmuter", "summoning_circle", "undead_market", "magical_storage" };

    /// <summary>Unit id -> tags (skeleton, wisp). Set at boot from the unit content.</summary>
    public static Func<string, IReadOnlyCollection<string>> UnitTags { get; set; } = _ => Array.Empty<string>();

    /// <summary>Is a subject a unit? Set at boot from the unit content.</summary>
    public static Func<string, bool> IsUnit { get; set; } = _ => false;

    private static readonly Dictionary<string, Func<string, bool>> Covers = new(StringComparer.Ordinal)
    {
        ["mana_well"] = s => s == "mana_well",
        ["infuser"] = s => s == "infuser",
        ["soul_well"] = s => s == "soul_well",
        ["transmuter"] = s => s == "transmuter",
        ["summoning_circle"] = s => s == "summoning_circle",
        ["undead_market"] = s => s == "undead_market",
        ["magical_storage"] = s => s == "magical_storage",
        ["mana_well_and_infuser"] = s => s is "mana_well" or "infuser",
        ["workshops_and_benches"] = s => s == ResearchBench || Buildings.Contains(s),
        ["every_workshop_building_and_bench"] = s => s == ResearchBench || Buildings.Contains(s),
        ["per_necromancer_in_mana_well"] = s => s == "mana_well",
        ["per_necromancer_in_mana_well_or_infuser"] = s => s is "mana_well" or "infuser",
        ["all_units"] = s => IsUnit(s),
        ["skeleton_units"] = s => UnitTags(s).Contains("skeleton"),
        ["wisp_units"] = s => UnitTags(s).Contains("wisp"),
        ["bones_and_skeleton_units"] = s => s == "bones" || UnitTags(s).Contains("skeleton"),
        ["bones_and_flesh"] = s => s is "bones" or "flesh",
        ["damage_healing_buff_debuff"] = s => s is "damage" or "healing" or "buff" or "debuff",
    };

    public static IEnumerable<string> All => Covers.Keys;

    public static bool Known(string target) => Covers.ContainsKey(target);

    /// <summary>The stat key a modifier is stored under.</summary>
    public static string Key(string stat, string? target) => target is null ? stat : stat + Separator + target;

    /// <summary>Splits "stat@target" (target null for an untargeted key).</summary>
    public static (string Stat, string? Target) Split(string key)
    {
        var i = key.IndexOf(Separator);
        return i < 0 ? (key, null) : (key[..i], key[(i + 1)..]);
    }

    /// <summary>Does a target cover a subject? (Unknown targets cover nothing.)</summary>
    public static bool Matches(string target, string subject) => Covers.TryGetValue(target, out var f) && f(subject);

    /// <summary>Targets that are read per assigned necromancer.</summary>
    public static bool IsPerNecromancer(string target) => target.StartsWith("per_necromancer_in_", StringComparison.Ordinal);
}
