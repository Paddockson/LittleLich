using System;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Content;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Core.Saves;
using LittleLich.Core.Ticks;
using Xunit;

namespace LittleLich.Tests;

/// <summary>Dummy feature: adds 1 per tick (batched).</summary>
internal sealed class Counter : ITickable
{
    public long Total;
    public int Calls;
    public void Tick(long ticks)
    {
        Total += ticks;
        Calls++;
    }
}

internal sealed class Recorder : ITickable
{
    private readonly string _name;
    private readonly List<string> _log;
    public Action? OnTick;
    public Recorder(string name, List<string> log) { _name = name; _log = log; }
    public void Tick(long ticks)
    {
        _log.Add(_name);
        OnTick?.Invoke();
    }
}

public class TickSchedulerTests
{
    [Fact]
    public void Same_total_after_100_live_ticks_and_100_offline_ticks()
    {
        // Live: real time in ~60 fps frames through the clock, ticks run as they fall due.
        var live = new TickScheduler();
        var liveCounter = new Counter();
        live.Register(liveCounter, TickGroup.Skills);
        var clock = new TickClock();
        var frames = 0;
        while (live.TickCount < 100)
        {
            var due = clock.Advance(1.0 / 60);
            live.RunTicks(due);
            frames++;
        }

        // Offline: 200 s away at 2 s per tick, one batched call.
        var offline = new TickScheduler();
        var offCounter = new Counter();
        offline.Register(offCounter, TickGroup.Skills);
        var recap = offline.RunOffline(200);

        Assert.Equal(100, liveCounter.Total);
        Assert.Equal(100, offCounter.Total);
        Assert.Equal(100, recap.Ticks);
        Assert.Equal(1, offCounter.Calls);          // batched, not looped
        Assert.InRange(frames, 11_990, 12_010);     // ~200 s of frames
    }

    [Fact]
    public void Order_is_fixed_by_group_then_order_then_registration()
    {
        var log = new List<string>();
        var s = new TickScheduler();
        // Registered deliberately out of order.
        s.Register(new Recorder("research", log), TickGroup.Research);
        s.Register(new Recorder("smithing", log), TickGroup.Skills, TickOrder.Crafting);
        s.Register(new Recorder("summoning_circle", log), TickGroup.Buildings, TickOrder.SummoningCircle);
        s.Register(new Recorder("world", log), TickGroup.World);
        s.Register(new Recorder("mining", log), TickGroup.Skills, TickOrder.Gathering);
        s.Register(new Recorder("mana_well", log), TickGroup.Buildings, TickOrder.ManaWell);
        s.Register(new Recorder("lich", log), TickGroup.Lich);
        s.Register(new Recorder("fishing", log), TickGroup.Skills, TickOrder.Gathering); // same order as mining: registration order

        s.RunTicks(1);
        s.RunTicks(5);

        var expected = new[] { "lich", "mana_well", "summoning_circle", "mining", "fishing", "smithing", "research", "world" };
        Assert.Equal(expected.Concat(expected), log);
    }

    [Fact]
    public void Events_wrap_the_features_and_carry_count_and_tick()
    {
        var log = new List<string>();
        var s = new TickScheduler { TickCount = 10 };
        s.Register(new Recorder("feature", log), TickGroup.Lich);
        s.TickStarted += (t, n) => log.Add($"started {t} {n}");
        s.TickEnded += (t, n) => log.Add($"ended {t} {n}");
        s.RunTicks(3);
        Assert.Equal(new[] { "started 13 3", "feature", "ended 13 3" }, log);
    }

    [Fact]
    public void Zero_or_negative_ticks_do_nothing()
    {
        var s = new TickScheduler();
        var c = new Counter();
        s.Register(c, TickGroup.Skills);
        var events = 0;
        s.TickEnded += (_, _) => events++;
        s.RunTicks(0);
        s.RunTicks(-5);
        Assert.Equal(0, c.Calls);
        Assert.Equal(0, events);
        Assert.Equal(0, s.TickCount);
    }

    [Fact]
    public void Register_twice_throws_and_unregister_stops_calls()
    {
        var s = new TickScheduler();
        var c = new Counter();
        s.Register(c, TickGroup.Skills);
        Assert.Throws<InvalidOperationException>(() => s.Register(c, TickGroup.Lich));
        s.RunTicks(1);
        Assert.True(s.Unregister(c));
        Assert.False(s.Unregister(c));
        s.RunTicks(1);
        Assert.Equal(1, c.Total);
        Assert.Empty(s.Registered);
    }

    [Fact]
    public void Unregistering_a_later_feature_during_a_tick_skips_it()
    {
        var log = new List<string>();
        var s = new TickScheduler();
        var first = new Recorder("first", log);
        var second = new Recorder("second", log);
        var late = new Recorder("late", log);
        first.OnTick = () =>
        {
            s.Unregister(second);
            if (!s.IsRegistered(late)) s.Register(late, TickGroup.World);
        };
        s.Register(first, TickGroup.Lich);
        s.Register(second, TickGroup.Skills);

        s.RunTicks(1); // late was registered mid-tick: it starts next tick
        s.RunTicks(1);
        Assert.Equal(new[] { "first", "first", "late" }, log);
    }

    [Fact]
    public void RunTicks_is_not_reentrant()
    {
        var s = new TickScheduler();
        var r = new Recorder("r", new List<string>());
        r.OnTick = () => s.RunTicks(1);
        s.Register(r, TickGroup.Lich);
        Assert.Throws<InvalidOperationException>(() => s.RunTicks(1));
        Assert.False(s.IsRunning);
    }

    [Fact]
    public void Offline_is_capped_at_24_hours_and_reports_the_loss()
    {
        var s = new TickScheduler();
        var c = new Counter();
        s.Register(c, TickGroup.Skills);
        var recap = s.RunOffline(3 * 24 * 3600);
        Assert.Equal(OfflineTime.MaxOfflineTicks, recap.Ticks);
        Assert.Equal(43_200, recap.Ticks);
        Assert.Equal(2 * 43_200, recap.TicksLostToCap);
        Assert.Equal(43_200, c.Total);
    }

    [Fact]
    public void Offline_ignores_partial_ticks_and_negative_time()
    {
        var s = new TickScheduler();
        Assert.Equal(2, s.RunOffline(5.9).Ticks);
        Assert.Equal(0, s.RunOffline(-100).Ticks);
        Assert.Equal(0, s.RunOffline(double.NaN).Ticks);
    }

    [Fact]
    public void Recap_is_active_only_during_offline_runs()
    {
        var s = new TickScheduler();
        var seen = new List<AwayRecap?>();
        var r = new Recorder("r", new List<string>());
        r.OnTick = () =>
        {
            seen.Add(s.ActiveRecap);
            s.ActiveRecap?.AddGained("resource", "iron_ore", 5);
            s.ActiveRecap?.AddGained("resource", "iron_ore", 2);
            s.ActiveRecap?.MarkCapped("iron_ore");
        };
        s.Register(r, TickGroup.Skills);
        s.RunTicks(1);
        var recap = s.RunOffline(20);
        Assert.Null(seen[0]);
        Assert.Same(recap, seen[1]);
        Assert.Null(s.ActiveRecap);
        Assert.Equal(new Num(7), recap.Gained[("resource", "iron_ore")]);
        Assert.Contains("iron_ore", recap.Capped);
        Assert.Contains("+7 resource iron_ore", recap.Describe());
    }

    [Fact]
    public void Lich_buffs_expire_the_same_live_and_offline()
    {
        LichState Make()
        {
            var l = new LichState();
            l.AddTimedModifier(LichStats.MaxInsight, ModifierOp.Add, 5, "potion", 30);
            l.AddTimedModifier(LichStats.MaxHealth, ModifierOp.Add, 50, "spell", 120);
            return l;
        }
        var liveLich = Make();
        var live = new TickScheduler();
        live.Register(liveLich, TickGroup.Lich);
        for (var i = 0; i < 100; i++) live.RunTicks(1);

        var offLich = Make();
        var off = new TickScheduler();
        off.Register(offLich, TickGroup.Lich);
        off.RunOffline(200);

        Assert.Equal(liveLich.CurrentTick, offLich.CurrentTick);
        Assert.Equal(liveLich.Stats.Modifiers, offLich.Stats.Modifiers);
        Assert.Equal(new Num(10), offLich.MaxInsight);
        Assert.Equal(new Num(150), offLich.MaxHealth);
    }
}

public class TickClockTests
{
    [Fact]
    public void Ticks_fall_due_every_two_seconds_and_progress_tracks_the_bar()
    {
        var c = new TickClock();
        Assert.Equal(0, c.Advance(1.4));
        Assert.Equal(0.7, c.Progress, 6);
        Assert.Equal(1, c.Advance(0.6));
        Assert.Equal(0, c.Elapsed, 6);
        Assert.Equal(3, c.Advance(6.5));          // a long frame becomes one batch of 3
        Assert.Equal(0.5, c.Elapsed, 6);
    }

    [Fact]
    public void Pausing_stops_the_bar_and_the_ticks()
    {
        var c = new TickClock();
        c.Advance(1.0);
        c.Paused = true;
        Assert.Equal(0, c.Advance(100));
        Assert.Equal(1.0, c.Elapsed, 6);
        c.Paused = false;
        Assert.Equal(1, c.Advance(1.0));
    }

    [Fact]
    public void Speed_scales_real_time_but_not_tick_length_or_offline_maths()
    {
        var c = new TickClock { Speed = 5 };
        Assert.Equal(5, c.Advance(2.0));
        Assert.Equal(2.0, c.TickSeconds);
        Assert.Equal(100, new TickScheduler().RunOffline(200).Ticks); // offline never sees the speed
        c.Speed = 1000;
        Assert.Equal(TickClock.MaxSpeed, c.Speed);
        c.Speed = 0;
        Assert.Equal(TickClock.MinSpeed, c.Speed);
        c.Speed = double.NaN;
        Assert.Equal(1, c.Speed);
    }

    [Fact]
    public void SetElapsed_carries_a_part_tick_into_the_bar()
    {
        var c = new TickClock();
        c.SetElapsed(1.5);
        Assert.Equal(1, c.Advance(0.5));
        c.SetElapsed(99);
        Assert.True(c.Elapsed < c.TickSeconds);
    }
}

/// <summary>The TickManager's wiring, reproduced with plain classes: autosave and total ticks through GameSession.</summary>
public class TickAutosaveTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeClock _clock = new();
    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Autosave_every_30_ticks_and_total_ticks_survive_reload()
    {
        var session = new GameSession(ContentDatabase.Empty, new SaveService(_dir.Path, _clock.Get), _clock.Get)
        {
            AutosaveIntervalTicks = 30,
        };
        session.NewGame(1);
        var saves = 0;
        session.Saved += _ => saves++;
        var s = new TickScheduler { TickCount = session.Meta.TotalTicks };
        s.TickEnded += (_, n) => session.NotifyTicksProcessed(n);

        for (var i = 0; i < 29; i++) s.RunTicks(1);
        Assert.Equal(0, saves);
        s.RunTicks(1);
        Assert.Equal(1, saves);
        s.RunOffline(3600); // 1,800 ticks in one batch: one save
        Assert.Equal(2, saves);
        Assert.Equal(1_830, session.Meta.TotalTicks);
        Assert.Equal(s.TickCount, session.Meta.TotalTicks);

        var reloaded = new GameSession(ContentDatabase.Empty, new SaveService(_dir.Path, _clock.Get), _clock.Get);
        reloaded.LoadSlot(1);
        Assert.Equal(1_830, reloaded.Meta.TotalTicks);
    }
}
