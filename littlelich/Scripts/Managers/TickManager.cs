using System;
using Godot;
using LittleLich.Core;
using LittleLich.Core.Ticks;

namespace LittleLich.Managers;

/// <summary>
/// Autoload singleton (after GameManager and LichManager). Thin Godot adapter over
/// <see cref="TickScheduler"/> (fixed-order processing) and <see cref="TickClock"/> (real time to ticks, tick bar, pause, speed).
///
/// - Live: _Process feeds real time to the clock; whole ticks due are run as one batch.
/// - Offline: when a game starts or loads, takes the GameSession's time away once and runs it batched (capped at 24 h),
///   producing an <see cref="AwayRecap"/>.
/// - After every batch: GameManager.OnTicksProcessed (total ticks + autosave every <see cref="AutosaveEveryTicks"/>).
/// - If a feature throws, the error is logged and ticking pauses so the state can be inspected.
/// </summary>
public partial class TickManager : Node
{
    public const long AutosaveEveryTicks = 30; // 60 s at 2 s per tick

    public static TickManager Instance { get; private set; } = null!;

    [Signal] public delegate void TickStartedEventHandler(long tick, long count);
    [Signal] public delegate void TickEndedEventHandler(long tick, long count);
    /// <summary>Every frame while a game runs: seconds into the current tick and the tick length (tick bar).</summary>
    [Signal] public delegate void TickProgressEventHandler(double elapsed, double tickSeconds);
    [Signal] public delegate void PausedChangedEventHandler(bool paused);
    [Signal] public delegate void SpeedChangedEventHandler(double speed);
    [Signal] public delegate void OfflineProcessedEventHandler(long ticks, double seconds);
    [Signal] public delegate void TickErrorEventHandler(string message);

    public TickScheduler Scheduler { get; } = new();
    public TickClock Clock { get; } = new();

    /// <summary>The recap of the last offline run (load or `offline` command).</summary>
    public AwayRecap? LastRecap { get; private set; }

    /// <summary>C# event with the full recap (signals cannot carry it).</summary>
    public event Action<AwayRecap>? OfflineRecap;

    /// <summary>
    /// Every frame while a game runs and ticking is not paused: elapsed game seconds (real seconds x speed).
    /// The battle clock (phase 8) runs on this; it is the one thing faster than the tick. Not raised offline.
    /// </summary>
    public event Action<double>? GameSecondsElapsed;

    public long TickCount => Scheduler.TickCount;
    public bool Paused => Clock.Paused;
    public double Speed => Clock.Speed;

    public TickManager()
    {
        // Set in the constructor so other autoloads can register features in their _Ready.
        Instance = this;
        Scheduler.TickStarted += (t, n) => EmitSignal(SignalName.TickStarted, t, n);
        Scheduler.TickEnded += (t, n) =>
        {
            GameManager.Instance.OnTicksProcessed(n);
            EmitSignal(SignalName.TickEnded, t, n);
        };
    }

    public override void _Ready()
    {
        var gm = GameManager.Instance;
        if (gm.Session is { } s) s.AutosaveIntervalTicks = AutosaveEveryTicks;
        gm.GameStarted += (_, _) => OnGameStarted();
        // The boot game started before this node was ready; pick it up now (registrations are done by now).
        OnGameStarted();
    }

    private bool HasGame => GameManager.Instance.Session?.CurrentSlot is not null;

    private void OnGameStarted()
    {
        if (GameManager.Instance.Session is not { } s || s.CurrentSlot is null) return;
        s.AutosaveIntervalTicks = AutosaveEveryTicks;
        Scheduler.TickCount = s.Meta.TotalTicks;
        Clock.Reset();
        var away = s.TakePendingOfflineSeconds();
        if (away > 0) RunOffline(away);
    }

    public override void _Process(double delta)
    {
        if (!HasGame) return;
        var due = Clock.Advance(delta);
        if (due > 0) RunTicks(due);
        if (!Clock.Paused) GameSecondsElapsed?.Invoke(delta * Clock.Speed);
        EmitSignal(SignalName.TickProgress, Clock.Elapsed, Clock.TickSeconds);
    }

    // ---- Commands ----

    /// <summary>Runs n ticks now as one batch (same path as live ticks).</summary>
    public void RunTicks(long ticks)
    {
        if (!HasGame || ticks <= 0) return;
        Guard(() => Scheduler.RunTicks(ticks));
    }

    /// <summary>Processes time away (used on load, and by the dev `offline` command). Returns the recap.</summary>
    public AwayRecap? RunOffline(double seconds)
    {
        if (!HasGame) return null;
        AwayRecap? recap = null;
        Guard(() => recap = Scheduler.RunOffline(seconds));
        if (recap is null) return null;
        // Carry the part-tick left over into the bar, unless time was lost to the cap anyway.
        if (recap.TicksLostToCap == 0) Clock.SetElapsed(seconds - recap.Ticks * OfflineTime.BaseTickSeconds);
        LastRecap = recap;
        GD.Print("[TickManager] " + recap.Describe());
        OfflineRecap?.Invoke(recap);
        EmitSignal(SignalName.OfflineProcessed, recap.Ticks, seconds);
        return recap;
    }

    public void SetPaused(bool paused)
    {
        if (Clock.Paused == paused) return;
        Clock.Paused = paused;
        EmitSignal(SignalName.PausedChanged, paused);
    }

    public void Pause() => SetPaused(true);
    public void Resume() => SetPaused(false);

    /// <summary>Dev time scale (0.1x to 100x). Does not change the tick length or offline maths.</summary>
    public void SetSpeed(double speed)
    {
        var before = Clock.Speed;
        Clock.Speed = speed;
        if (Clock.Speed != before) EmitSignal(SignalName.SpeedChanged, Clock.Speed);
    }

    public void Register(ITickable feature, TickGroup group, int order = 0) => Scheduler.Register(feature, group, order);
    public bool Unregister(ITickable feature) => Scheduler.Unregister(feature);

    private void Guard(Action run)
    {
        try
        {
            run();
        }
        catch (Exception e)
        {
            var msg = $"Tick failed at tick {Scheduler.TickCount}: {e.GetType().Name}: {e.Message}. Ticking paused.";
            GD.PushError("[TickManager] " + msg);
            Pause();
            EmitSignal(SignalName.TickError, msg);
        }
    }
}
