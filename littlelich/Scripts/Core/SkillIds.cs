using System.Collections.Generic;
using System.Linq;

namespace LittleLich.Core;

/// <summary>The ten v0.1 skills. Content (activities, recipes) refers to skills by these ids.</summary>
public static class SkillIds
{
    public const string Health = "health";
    public const string Necromancy = "necromancy";
    public const string Magic = "magic";

    public const string Mining = "mining";
    public const string Fishing = "fishing";
    public const string Hunting = "hunting";
    public const string Herbalism = "herbalism";

    public const string Smithing = "smithing";
    public const string Alchemy = "alchemy";
    public const string Enchanting = "enchanting";

    /// <summary>Level-only lich skills (no page, no automation in v0.1).</summary>
    public static readonly IReadOnlyList<string> Lich = new[] { Health, Necromancy, Magic };
    public static readonly IReadOnlyList<string> Gathering = new[] { Mining, Fishing, Hunting, Herbalism };
    public static readonly IReadOnlyList<string> Crafting = new[] { Smithing, Alchemy, Enchanting };

    /// <summary>All ten, in sidebar order (Lich, Gathering, Crafting).</summary>
    public static readonly IReadOnlyList<string> All = Lich.Concat(Gathering).Concat(Crafting).ToList();

    /// <summary>Lich stat that multiplies a gathering skill's output or a crafting skill's speed (base 1).</summary>
    public static string SpeedStat(string skillId) => skillId + "_speed";
}
