using System;
using LittleLich.Core;
using LittleLich.Core.Lich;

namespace LittleLich.Crypt;

/// <summary>
/// How Workshop buildings and research benches read research modifiers (research revamp). One place, so every
/// building treats "room", "efficiency" and level caps the same way.
/// </summary>
public static class ResearchStats
{
    /// <summary>
    /// Necromancer room for a subject: (base + targeted max_necromancers adds) x targeted muls, then x
    /// workshop_necromancer_room (Dark Empowerment doubles it). The lich's own max_necromancers (untargeted) is not used here.
    /// </summary>
    public static long Room(StatSheet s, string subject, long baseRoom)
    {
        var room = s.GetFor("max_necromancers", subject, baseRoom, includeUntargeted: false)
                   * s.GetFor("workshop_necromancer_room", subject, Num.One, includeUntargeted: false);
        var v = room.ToDouble();
        return v >= long.MaxValue ? long.MaxValue : Math.Max(0, (long)Math.Floor(v + 1e-9));
    }

    /// <summary>
    /// Output multiplier for a building: targeted efficiency (+ per-necromancer efficiency x staff) x targeted output
    /// x necromancer_efficiency (targeted and global). 1 with no research.
    /// </summary>
    public static double Efficiency(StatSheet s, string subject, long staffed)
    {
        var eff = s.GetFor("efficiency", subject, Num.One, includeUntargeted: false).ToDouble()
                  + s.PerNecromancer("efficiency", subject) * Math.Max(0, staffed);
        var output = s.GetFor("output", subject, Num.One, includeUntargeted: false).ToDouble();
        var necro = s.GetFor("necromancer_efficiency", subject, Num.One).ToDouble();
        return Math.Max(0, eff * output * necro);
    }

    /// <summary>Extra max levels for buildings, benches and upgrade nodes (Upgraded Infusions).</summary>
    public static int LevelCapBonus(StatSheet s) => (int)Math.Floor(s.Get("building_and_node_level_cap").ToDouble());

    /// <summary>A cost multiplier for a subject (Bring Bones, Cheap Bones on skeleton units); 1 with no research.</summary>
    public static double CostMultiplier(StatSheet s, string subject) =>
        Math.Max(0, s.GetFor("cost", subject, Num.One, includeUntargeted: false).ToDouble());

    /// <summary>A count after a cost multiplier, rounded up (never below 1 for a cost that was at least 1).</summary>
    public static long Scaled(long count, double multiplier) =>
        count <= 0 ? count : Math.Max(1, (long)Math.Ceiling(count * multiplier - 1e-9));
}
