using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using LittleLich.Content;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Core.Saves;
using Xunit;

namespace LittleLich.Tests;

public class NecromancerPoolTests
{
    private static NecromancerPool Pool(long max, long total)
    {
        var p = new NecromancerPool();
        p.SetMax(max);
        p.AddToPool(total);
        return p;
    }

    private static void AssertInvariants(NecromancerPool p)
    {
        Assert.Equal(p.Total, p.Free + p.Assigned);
        Assert.InRange(p.Assigned, 0, p.Total);
        Assert.Equal(p.Assigned, p.AllAssignments().Sum(a => a.Count));
        Assert.All(p.AllAssignments(), a => Assert.True(a.Count > 0));
    }

    [Fact]
    public void AddToPool_respects_max()
    {
        var p = Pool(10, 0);
        Assert.Equal(7, p.AddToPool(7));
        Assert.Equal(3, p.AddToPool(100));
        Assert.Equal(0, p.AddToPool(1));
        Assert.Equal(10, p.Total);
        Assert.Equal(0, p.AddToPool(-5));
    }

    [Fact]
    public void Max_is_clamped_to_hard_cap()
    {
        var p = new NecromancerPool();
        p.SetMax(long.MaxValue);
        Assert.Equal(NecromancerPool.HardCap, p.Max);
        Assert.Equal(NecromancerPool.HardCap, p.AddToPool(long.MaxValue));
        p.SetMax(-1);
        Assert.Equal(0, p.Max);
    }

    [Fact]
    public void Assigning_more_than_free_assigns_only_the_free_ones()
    {
        var p = Pool(10, 10);
        Assert.Equal(6, p.TryAssign("mining", "iron_ore", 6));
        Assert.Equal(4, p.TryAssign("fishing", "trout", 100));
        Assert.Equal(0, p.TryAssign("mining", "iron_ore", 1));
        Assert.Equal(0, p.Free);
        AssertInvariants(p);
    }

    [Fact]
    public void Unassigning_more_than_assigned_frees_only_the_assigned_ones()
    {
        var p = Pool(10, 10);
        p.TryAssign("mining", "iron_ore", 4);
        Assert.Equal(4, p.Unassign("mining", "iron_ore", 50));
        Assert.Equal(0, p.GetAssigned("mining", "iron_ore"));
        Assert.Empty(p.AllAssignments());
        Assert.Equal(0, p.Unassign("mining", "iron_ore", 1));
        Assert.Equal(0, p.Unassign("nobody", "nothing", 1));
        AssertInvariants(p);
    }

    [Fact]
    public void Zero_and_negative_counts_do_nothing()
    {
        var p = Pool(10, 10);
        p.TryAssign("mining", "iron_ore", 5);
        Assert.Equal(0, p.TryAssign("mining", "iron_ore", 0));
        Assert.Equal(0, p.TryAssign("mining", "iron_ore", -3));
        Assert.Equal(0, p.Unassign("mining", "iron_ore", -3));
        Assert.Equal(5, p.GetAssigned("mining", "iron_ore"));
        AssertInvariants(p);
    }

    [Fact]
    public void Blank_ids_are_rejected()
    {
        var p = Pool(10, 10);
        Assert.Throws<ArgumentException>(() => p.TryAssign("", "iron_ore", 1));
        Assert.Throws<ArgumentException>(() => p.TryAssign("mining", " ", 1));
    }

    [Fact]
    public void Pool_at_max_still_assigns_and_lowering_max_keeps_necromancers()
    {
        var p = Pool(10, 10);
        Assert.Equal(10, p.TryAssign("mana_well", "main", 10));
        p.SetMax(4);
        Assert.Equal(10, p.Total);
        Assert.Equal(0, p.AddToPool(1));
        p.Unassign("mana_well", "main", 10);
        Assert.Equal(10, p.Free);
        AssertInvariants(p);
    }

    [Fact]
    public void Assignments_are_keyed_by_owner_and_activity()
    {
        var p = Pool(100, 100);
        p.TryAssign("mining", "iron_ore", 3);
        p.TryAssign("mining", "copper_ore", 2);
        p.TryAssign("smithing", "iron_ore", 5);
        p.TryAssign("mining", "iron_ore", 1);
        Assert.Equal(4, p.GetAssigned("mining", "iron_ore"));
        Assert.Equal(5, p.GetAssigned("smithing", "iron_ore"));
        Assert.Equal(6, p.GetAssignedToOwner("mining"));
        Assert.Equal(
            new[] { "mining/copper_ore=2", "mining/iron_ore=4", "smithing/iron_ore=5" },
            p.AllAssignments().Select(a => $"{a.OwnerId}/{a.ActivityId}={a.Count}"));
        Assert.Equal(6, p.UnassignOwner("mining"));
        Assert.Equal(95, p.Free);
        AssertInvariants(p);
    }

    [Fact]
    public void Changed_is_raised_on_every_change_and_not_on_no_ops()
    {
        var p = Pool(10, 0);
        var count = 0;
        p.Changed += () => count++;
        p.AddToPool(5);
        p.TryAssign("a", "b", 2);
        p.Unassign("a", "b", 1);
        p.SetMax(20);
        Assert.Equal(4, count);
        p.TryAssign("a", "b", 0);
        p.Unassign("x", "y", 1);
        p.SetMax(20);
        Assert.Equal(4, count);
    }

    [Fact]
    public void Random_assign_and_unassign_keeps_free_plus_assigned_equal_total()
    {
        var rng = new Random(1234);
        var p = Pool(1_000, 0);
        string[] owners = { "mining", "fishing", "mana_well" };
        string[] acts = { "a", "b", "main" };
        for (var i = 0; i < 5_000; i++)
        {
            var o = owners[rng.Next(owners.Length)];
            var a = acts[rng.Next(acts.Length)];
            var n = rng.Next(-5, 200);
            switch (rng.Next(5))
            {
                case 0: p.AddToPool(n); break;
                case 1: p.SetMax(rng.Next(0, 1_500)); break;
                case 2: p.Unassign(o, a, n); break;
                default: p.TryAssign(o, a, n); break;
            }
            AssertInvariants(p);
        }
    }
}

public class StatSheetTests
{
    [Fact]
    public void Value_is_base_plus_adds_times_muls()
    {
        var s = new StatSheet();
        s.SetBase("max_health", 100);
        s.Add(new Modifier("max_health", ModifierOp.Add, 20, "gear:hood"));
        s.Add(new Modifier("max_health", ModifierOp.Add, 30, "research"));
        s.Add(new Modifier("max_health", ModifierOp.Mul, 1.5, "potion"));
        s.Add(new Modifier("max_health", ModifierOp.Mul, 2, "spell"));
        s.Add(new Modifier("max_insight", ModifierOp.Add, 99, "gear:hood"));
        Assert.Equal(new Num(450), s.Get("max_health"));
        Assert.Equal(new Num(0), s.Get("unknown"));
    }

    [Fact]
    public void RemoveBySource_removes_all_from_that_source()
    {
        var s = new StatSheet();
        s.SetBase("max_health", 100);
        s.Add(new Modifier("max_health", ModifierOp.Add, 20, "gear:hood"));
        s.Add(new Modifier("max_insight", ModifierOp.Add, 5, "gear:hood"));
        s.Add(new Modifier("max_health", ModifierOp.Mul, 2, "potion"));
        Assert.Equal(2, s.RemoveBySource("gear:hood"));
        Assert.Equal(new Num(200), s.Get("max_health"));
        Assert.Equal(new Num(0), s.Get("max_insight"));
    }

    [Fact]
    public void StatChanged_fires_only_when_the_value_changes()
    {
        var s = new StatSheet();
        var changes = new List<(string, Num)>();
        s.StatChanged += (id, v) => changes.Add((id, v));
        s.SetBase("max_health", 100);
        s.Add(new Modifier("max_health", ModifierOp.Mul, 1, "noop"));
        s.Add(new Modifier("max_health", ModifierOp.Add, 10, "gear"));
        Assert.Equal(new[] { ("max_health", new Num(100)), ("max_health", new Num(110)) }, changes);
    }
}

public class LichStateTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeClock _clock = new();

    public void Dispose() => _dir.Dispose();

    private (GameSession session, LichState lich) NewSession()
    {
        var session = new GameSession(ContentDatabase.Empty, new SaveService(_dir.Path, _clock.Get), _clock.Get);
        var lich = new LichState();
        session.Register(lich);
        return (session, lich);
    }

    [Fact]
    public void New_game_has_placeholder_stats_full_health_and_no_necromancers()
    {
        var (session, lich) = NewSession();
        session.NewGame(1);
        Assert.Equal(new Num(LichStats.BaseMaxHealth), lich.Health);
        Assert.Equal(new Num(LichStats.BaseMaxInsight), lich.MaxInsight);
        Assert.Equal((long)LichStats.BaseMaxNecromancers, lich.Pool.Max);
        Assert.Equal(0, lich.Pool.Total);
        Assert.Empty(lich.Stats.Modifiers);
    }

    [Fact]
    public void Max_necromancers_stat_drives_the_pool_max()
    {
        var lich = new LichState();
        lich.AddModifier(new Modifier(LichStats.MaxNecromancers, ModifierOp.Add, 90, "research"));
        Assert.Equal(100, lich.Pool.Max);
        lich.AddModifier(new Modifier(LichStats.MaxNecromancers, ModifierOp.Mul, 1e12, "silly"));
        Assert.Equal(NecromancerPool.HardCap, lich.Pool.Max);
        lich.RemoveModifiersFrom("silly");
        Assert.Equal(100, lich.Pool.Max);
    }

    [Fact]
    public void Timed_modifiers_expire_on_the_tick_they_end()
    {
        var lich = new LichState();
        lich.AddTimedModifier(LichStats.MaxInsight, ModifierOp.Add, 5, "potion", 3);
        Assert.Equal(new Num(15), lich.MaxInsight);
        lich.Tick(2);
        Assert.Equal(new Num(15), lich.MaxInsight);
        lich.Tick(1);
        Assert.Equal(new Num(10), lich.MaxInsight);
        Assert.Empty(lich.Stats.Modifiers);
    }

    [Fact]
    public void Tick_batching_matches_single_ticks()
    {
        LichState Make()
        {
            var l = new LichState();
            l.AddTimedModifier(LichStats.MaxHealth, ModifierOp.Add, 50, "a", 10);
            l.AddTimedModifier(LichStats.MaxHealth, ModifierOp.Mul, 2, "b", 40);
            l.AddTimedModifier(LichStats.MaxInsight, ModifierOp.Add, 3, "c", 100);
            l.AddModifier(new Modifier(LichStats.MaxInsight, ModifierOp.Add, 1, "permanent"));
            return l;
        }
        var batched = Make();
        var single = Make();
        batched.Tick(45);
        for (var i = 0; i < 45; i++) single.Tick(1);
        Assert.Equal(single.CurrentTick, batched.CurrentTick);
        Assert.Equal(single.MaxHealth, batched.MaxHealth);
        Assert.Equal(single.MaxInsight, batched.MaxInsight);
        Assert.Equal(single.Stats.Modifiers, batched.Stats.Modifiers);
    }

    [Fact]
    public void Health_has_no_regen_and_clamps_when_max_drops()
    {
        var lich = new LichState();
        lich.Damage(30);
        lich.Tick(1_000);
        Assert.Equal(new Num(70), lich.Health);
        lich.AddModifier(new Modifier(LichStats.MaxHealth, ModifierOp.Add, 100, "gear"));
        Assert.Equal(new Num(70), lich.Health);
        lich.AddModifier(new Modifier(LichStats.MaxHealth, ModifierOp.Mul, 0.5, "curse"));
        Assert.Equal(new Num(70), lich.Health);
        lich.AddModifier(new Modifier(LichStats.MaxHealth, ModifierOp.Mul, 0.5, "curse2"));
        Assert.Equal(new Num(50), lich.Health);
        lich.Heal(1_000);
        Assert.Equal(new Num(50), lich.Health);
        lich.Damage(500);
        Assert.True(lich.IsDead);
        lich.RestoreFullHealth();
        Assert.Equal(new Num(50), lich.Health);
    }

    [Fact]
    public void Debug_add_raises_max_when_needed()
    {
        var lich = new LichState();
        Assert.Equal(25, lich.DebugAddNecromancers(25));
        Assert.Equal(25, lich.Pool.Total);
        Assert.Equal(25, lich.Pool.Max);
        lich.DebugAddNecromancers(5);
        Assert.Equal(30, lich.Pool.Total);
        Assert.Single(lich.Stats.Modifiers);
    }

    [Fact]
    public void Events_fire_for_ui()
    {
        var lich = new LichState();
        int pool = 0, health = 0;
        var stats = new List<string>();
        lich.PoolChanged += () => pool++;
        lich.HealthChanged += () => health++;
        lich.StatChanged += (id, _) => stats.Add(id);
        lich.DebugAddNecromancers(15);
        lich.TryAssign("mining", "iron_ore", 2);
        lich.Damage(1);
        lich.AddModifier(new Modifier(LichStats.MaxInsight, ModifierOp.Add, 1, "gear"));
        Assert.True(pool >= 2);
        Assert.Equal(1, health);
        Assert.Contains(LichStats.MaxInsight, stats);
        Assert.Contains(LichStats.MaxNecromancers, stats);
    }

    [Fact]
    public void Save_quit_reload_restores_every_assignment_exactly()
    {
        var (s1, lich) = NewSession();
        s1.NewGame(1);
        lich.DebugAddNecromancers(1_000);
        lich.TryAssign("mining", "iron_ore", 300);
        lich.TryAssign("mining", "copper_ore", 7);
        lich.TryAssign("mana_well", "main", 11);
        lich.TryAssign("smithing", "iron_bar", 1);
        lich.Unassign("mining", "iron_ore", 50);
        lich.AddModifier(new Modifier(LichStats.MaxHealth, ModifierOp.Mul, 1.5, "gear:robe"));
        lich.AddTimedModifier(LichStats.MaxInsight, ModifierOp.Add, 4, "potion:clarity", 20);
        lich.Tick(5);
        lich.Damage(42.5);
        s1.Save();

        var (s2, reloaded) = NewSession();
        s2.LoadSlot(1);

        Assert.Equal(lich.AllAssignments(), reloaded.AllAssignments());
        Assert.Equal(lich.Pool.Total, reloaded.Pool.Total);
        Assert.Equal(lich.Pool.Free, reloaded.Pool.Free);
        Assert.Equal(lich.Pool.Max, reloaded.Pool.Max);
        Assert.Equal(lich.Stats.Modifiers, reloaded.Stats.Modifiers);
        Assert.Equal(lich.CurrentTick, reloaded.CurrentTick);
        Assert.Equal(lich.Health, reloaded.Health);
        Assert.Equal(lich.MaxHealth, reloaded.MaxHealth);
        Assert.Equal(lich.MaxInsight, reloaded.MaxInsight);
    }

    [Fact]
    public void Load_raises_ui_events_once()
    {
        var (s1, lich) = NewSession();
        s1.NewGame(1);
        lich.DebugAddNecromancers(5);
        s1.Save();

        var (s2, reloaded) = NewSession();
        int pool = 0, health = 0;
        reloaded.PoolChanged += () => pool++;
        reloaded.HealthChanged += () => health++;
        s2.LoadSlot(1);
        Assert.Equal(1, pool);
        Assert.Equal(1, health);
    }

    [Fact]
    public void Load_repairs_bad_data()
    {
        var lich = new LichState();
        var node = JsonNode.Parse("""
        {
          "current_tick": 10,
          "health": 9999,
          "necromancers": {
            "total": 3,
            "assignments": [
              { "owner_id": "mining", "activity_id": "iron_ore", "count": 4 },
              { "owner_id": "mining", "activity_id": "iron_ore", "count": 1 },
              { "owner_id": "", "activity_id": "x", "count": 5 },
              { "owner_id": "fishing", "activity_id": "trout", "count": -2 }
            ]
          },
          "modifiers": [
            { "stat_id": "max_insight", "op": "add", "value": 1, "source": "old", "expires_at_tick": 10 },
            { "stat_id": "max_insight", "op": "pow", "value": 2, "source": "bad" },
            { "stat_id": "max_insight", "op": "add", "value": 2, "source": "kept", "expires_at_tick": 11 }
          ]
        }
        """)!.AsObject();
        lich.Load(new SaveData(node));

        Assert.Equal(new[] { new Assignment("mining", "iron_ore", 5) }, lich.AllAssignments());
        Assert.Equal(5, lich.Pool.Total);
        Assert.Equal(0, lich.Pool.Free);
        Assert.Equal(lich.MaxHealth, lich.Health);
        Assert.Equal(new Num(12), lich.MaxInsight);
    }

    [Fact]
    public void Loading_an_old_save_without_a_lich_section_gives_defaults()
    {
        var lich = new LichState();
        lich.DebugAddNecromancers(5);
        lich.Load(new SaveData());
        Assert.Equal(0, lich.Pool.Total);
        Assert.Empty(lich.Stats.Modifiers);
        Assert.Equal(lich.MaxHealth, lich.Health);
        Assert.Equal(0, lich.CurrentTick);
    }
}
