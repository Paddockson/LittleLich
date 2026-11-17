using System;
using LittleLich.Core;

namespace LittleLich.Skills;

/// <summary>
/// v0.1 XP curve: each level needs 10% more XP than the last, levels 1 to 999, no easing.
///   XP to go from level L to L+1 = Base x 1.1^(L-1)
///   Total XP to reach level L     = Base x (1.1^(L-1) - 1) / 0.1
/// With Base = 10 (placeholder) level 999 needs about 1.8e43 total XP, in line with the design doc's ~1e43.
/// </summary>
public static class XpCurve
{
    public const int MaxLevel = 999;
    public const double Growth = 1.1;
    /// <summary>XP from level 1 to 2. Placeholder.</summary>
    public const double Base = 10;

    // Total XP needed to reach each level (index = level; [0] unused, [1] = 0).
    private static readonly double[] Thresholds = Build();

    private static double[] Build()
    {
        var t = new double[MaxLevel + 1];
        double step = Base;
        for (var level = 2; level <= MaxLevel; level++)
        {
            t[level] = t[level - 1] + step;
            step *= Growth;
        }
        return t;
    }

    /// <summary>Total XP needed to reach <paramref name="level"/> (clamped to 1..999).</summary>
    public static Num XpForLevel(int level) => Thresholds[Math.Clamp(level, 1, MaxLevel)];

    /// <summary>The level a total XP amount gives (1..999).</summary>
    public static int LevelForXp(Num xp)
    {
        var x = xp.ToDouble();
        if (!(x >= Thresholds[2])) return 1;
        // Largest level whose threshold is <= x.
        int lo = 2, hi = MaxLevel;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (Thresholds[mid] <= x) lo = mid; else hi = mid - 1;
        }
        return lo;
    }
}
