using System;

namespace LittleLich.Core.Ticks;

/// <summary>
/// Turns real time into whole ticks for live play, and holds the tick bar's progress.
/// Pause freezes it. <see cref="Speed"/> is a dev-only time scale: it changes how fast real time fills the bar,
/// never the tick length, so offline maths (always <see cref="OfflineTime.BaseTickSeconds"/>) is unaffected.
/// Not saved: every session starts unpaused at 1x with an empty bar.
/// </summary>
public sealed class TickClock
{
    public const double MinSpeed = 0.1;
    public const double MaxSpeed = 100;

    private double _speed = 1;

    public TickClock(double tickSeconds = OfflineTime.BaseTickSeconds)
    {
        if (tickSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(tickSeconds));
        TickSeconds = tickSeconds;
    }

    /// <summary>Length of one tick in game seconds (2 s at startup).</summary>
    public double TickSeconds { get; }

    public bool Paused { get; set; }

    public double Speed
    {
        get => _speed;
        set => _speed = double.IsFinite(value) ? Math.Clamp(value, MinSpeed, MaxSpeed) : 1;
    }

    /// <summary>Game seconds into the current tick, 0 to <see cref="TickSeconds"/> (the "0.7s" of "0.7s / 2.0s").</summary>
    public double Elapsed { get; private set; }

    public double Progress => Elapsed / TickSeconds;

    /// <summary>Adds real time. Returns how many whole ticks are now due (0 when paused). Several are run as one batch.</summary>
    public long Advance(double realSeconds)
    {
        if (Paused || !(realSeconds > 0)) return 0;
        Elapsed += realSeconds * _speed;
        if (Elapsed < TickSeconds) return 0;
        var ticks = (long)Math.Floor(Elapsed / TickSeconds);
        Elapsed -= ticks * TickSeconds;
        if (Elapsed < 0) Elapsed = 0;
        return ticks;
    }

    /// <summary>Sets the bar position (e.g. the leftover seconds after an offline run). Clamped into one tick.</summary>
    public void SetElapsed(double seconds) =>
        Elapsed = double.IsFinite(seconds) ? Math.Clamp(seconds, 0, TickSeconds - 1e-9) : 0;

    public void Reset() => Elapsed = 0;
}
