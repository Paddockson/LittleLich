using System;

namespace LittleLich.Core;

/// <summary>
/// Exact batched production maths shared by skills and buildings.
/// Rates are converted once to integer micro-units (1 unit = 1,000,000), progress is kept in Int128,
/// so Tick(n) gives exactly the same result as n x Tick(1) (no floating drift).
/// </summary>
public static class FixedPoint
{
    public const long Micro = 1_000_000;

    public static long ToMicro(double value) =>
        !(value > 0) ? 0 : value >= long.MaxValue / (double)Micro ? long.MaxValue / Micro : (long)Math.Round(value * Micro);

    public static long Clamp(Int128 v) => v > long.MaxValue ? long.MaxValue : v < 0 ? 0 : (long)v;

    /// <summary>
    /// One batch of an "operation" process: progress = carry + workPerTick x ticks; each operation costs
    /// <paramref name="costPerOp"/> (all in micro-units). Runs min(possible, <paramref name="maxOps"/>) operations.
    /// When limited by <paramref name="maxOps"/> (inputs, room, target), progress is dropped (carry 0) so a
    /// paused process restarts cleanly; otherwise the remainder carries.
    /// </summary>
    public static (long Ops, long Carry, bool Limited) Run(long carry, long workPerTick, long ticks, long costPerOp, long maxOps)
    {
        if (costPerOp <= 0) throw new ArgumentOutOfRangeException(nameof(costPerOp));
        var progress = (Int128)carry + (Int128)workPerTick * ticks;
        var possible = Clamp(progress / costPerOp);
        var ops = Math.Min(possible, Math.Max(0, maxOps));
        if (possible > ops || maxOps <= 0) return (ops, 0, true);
        return (ops, (long)(progress % costPerOp), false);
    }
}
