using System;

namespace LittleLich.Core;

/// <summary>Time-away maths. Always converts with the base tick length, never a sped-up one.</summary>
public static class OfflineTime
{
    /// <summary>Base tick length at startup (seconds). Speed-up must not change offline conversion.</summary>
    public const double BaseTickSeconds = 2.0;

    /// <summary>Offline progress cap: 24 hours of time away. Anything beyond is not processed (reported in the recap).</summary>
    public const double MaxOfflineSeconds = 24 * 60 * 60;

    /// <summary><see cref="MaxOfflineSeconds"/> in base ticks (43,200).</summary>
    public const long MaxOfflineTicks = (long)(MaxOfflineSeconds / BaseTickSeconds);

    /// <summary>Seconds between the save time and now. Never negative (clock moved backwards = 0).</summary>
    public static double ElapsedSeconds(DateTime savedAtUtc, DateTime nowUtc)
    {
        var seconds = (nowUtc.ToUniversalTime() - savedAtUtc.ToUniversalTime()).TotalSeconds;
        return seconds > 0 ? seconds : 0;
    }

    /// <summary>Whole ticks in <paramref name="elapsedSeconds"/>, optionally capped (phase 3 sets the cap).</summary>
    public static long ToTicks(double elapsedSeconds, double tickSeconds = BaseTickSeconds, long maxTicks = long.MaxValue)
    {
        if (tickSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(tickSeconds));
        if (elapsedSeconds <= 0 || double.IsNaN(elapsedSeconds)) return 0;
        double ticks = Math.Floor(elapsedSeconds / tickSeconds);
        return ticks >= maxTicks ? maxTicks : (long)ticks;
    }
}
