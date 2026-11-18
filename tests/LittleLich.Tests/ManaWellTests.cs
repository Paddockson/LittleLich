using System;
using System.IO;
using System.Linq;
using LittleLich.Content;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Crypt.Buildings;
using Xunit;

namespace LittleLich.Tests;

/// <summary>Adjustment 02: the Mana Well with clicks, a level table and per-necromancer output.</summary>
public class ManaWellTests
{
    private static World Fresh()
    {
        var w = new World();
        foreach (var s in w.Saveables) s.Load(new SaveData());
        w.Buildings.OfType<SummoningCircle>().Single().ClearSlot(0);   // keep the circle from spending mana
        return w;
    }

    private static ManaWell Well(World w) => w.Buildings.OfType<ManaWell>().Single();

    [Fact]
    public void Level_one_holds_two_necromancers_and_makes_nothing_on_its_own()
    {
        var w = Fresh();
        var well = Well(w);
        Assert.Equal(1, well.ClickPower);
        Assert.Equal(2.0, well.OutputPerNecromancer);
        Assert.Equal(2, well.MaxNecromancers);
        Assert.Equal(0, well.OutputPerTick());
        w.Ticks.RunTicks(10);
        Assert.Equal(0, w.Storage.Get("mana"));
    }

    [Fact]
    public void A_click_channels_click_power_mana_and_stops_at_the_cap()
    {
        var w = Fresh();
        var well = Well(w);
        Assert.Equal(1, well.Click());
        Assert.Equal(1, w.Storage.Get("mana"));
        w.Storage.Add("mana", w.Storage.Cap("mana"));
        Assert.Equal(w.Storage.Cap("mana"), w.Storage.Get("mana"));
        Assert.Equal(0, well.Click());
        Assert.Equal(w.Storage.Cap("mana"), w.Storage.Get("mana"));
    }

    [Fact]
    public void Assigned_necromancers_raise_output_and_leave_the_free_pool()
    {
        var w = Fresh();
        var well = Well(w);
        w.Lich.DebugAddNecromancers(5);
        Assert.Equal(2, well.AddNecromancer(5));          // holds 2 at level 1
        Assert.Equal(3, w.Lich.Pool.Free);
        Assert.Equal(2, w.Lich.Pool.GetAssigned("mana_well", "main"));
        Assert.Equal(4.0, well.OutputPerTick(), 9);
        w.Ticks.RunTicks(10);
        Assert.Equal(40, w.Storage.Get("mana"));
        Assert.Equal(BuildingStatus.Running, well.Status);
        Assert.Equal(1, well.RemoveNecromancer(1));
        Assert.Equal(4, w.Lich.Pool.Free);
    }

    [Fact]
    public void Output_uses_the_level_bonus_and_the_output_stat()
    {
        var w = Fresh();
        var well = Well(w);
        well.DebugSetLevel(4);
        w.Lich.DebugAddNecromancers(4);
        well.AddNecromancer(4);
        Assert.Equal(6, well.MaxNecromancers);
        Assert.Equal(0.0003, well.LevelBonusAt(4), 9);
        Assert.Equal(0.0004, well.LevelBonusAt(5), 9);
        Assert.Equal(4 * 2 * 1.0003, well.OutputPerTick(), 9);
        w.Lich.Stats.Add(new Modifier(ManaWell.OutputStat, ModifierOp.Mul, 1.5, "test"));
        Assert.Equal(4 * 2 * 1.0003 * 1.5, well.OutputPerTick(), 9);
    }

    [Fact]
    public void Upgrade_is_refused_with_a_reason_until_every_cost_is_met()
    {
        var w = Fresh();
        var well = Well(w);
        Assert.Equal(new[] { "mana:60", "gold:30" }, well.UpgradeCosts.Select(c => $"{c.Id}:{c.Count}"));
        Assert.False(well.CanUpgrade(out var why));
        Assert.Equal("Not enough mana yet", why);
        w.Storage.Add("mana", 60);
        Assert.False(well.CanUpgrade(out why));
        Assert.Equal("Not enough gold yet", why);
        Assert.False(well.TryUpgrade());
        Assert.Equal(60, w.Storage.Get("mana"));            // nothing spent

        w.Storage.Add("gold", 30);
        Assert.True(well.CanUpgrade(out _));
        Assert.True(well.TryUpgrade());
        Assert.Equal(2, well.Level);
        Assert.Equal(3, well.MaxNecromancers);
        Assert.Equal(0, w.Storage.Get("mana"));
        Assert.Equal(0, w.Storage.Get("gold"));
        Assert.Equal(new[] { "mana:81", "gold:45" }, well.UpgradeCosts.Select(c => $"{c.Id}:{c.Count}"));
    }

    [Fact]
    public void The_level_table_caps_the_max_level_and_research_raises_it()
    {
        var w = Fresh();
        var well = Well(w);
        Assert.Equal(10, well.MaxLevel);                    // upgrades.json max_level
        w.Research.DebugComplete("upgraded_infusions");    // +10 max levels
        Assert.Equal(20, well.MaxLevel);                    // the table has 20 rows
        well.DebugSetLevel(20);
        Assert.True(well.IsMaxLevel);
        Assert.False(well.CanUpgrade(out var why));
        Assert.Equal("Highest level reached", why);
    }

    private static World Busy()
    {
        var w = Fresh();
        var well = Well(w);
        well.DebugSetLevel(7);
        w.Lich.DebugAddNecromancers(7);
        well.AddNecromancer(7);                             // 7 x 2 x 1.0006 = 14.0084 mana/tick
        well.Click();
        return w;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(100)]
    public void Ticks_in_one_call_equal_single_ticks(int n)
    {
        var live = Busy();
        for (var i = 0; i < n; i++) live.Ticks.RunTicks(1);
        var batched = Busy();
        batched.Ticks.RunTicks(n);
        Assert.Equal(live.Storage.Get("mana"), batched.Storage.Get("mana"));
        Assert.Equal(Well(live).Save().Node.ToJsonString(), Well(batched).Save().Node.ToJsonString());
        Assert.Equal(Well(live).Status, Well(batched).Status);
    }

    [Fact]
    public void Content_checks_the_level_table()
    {
        var sources = ContentSource.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Data"))
            .Select(s => s.Path == "buildings.json"
                ? s with
                {
                    Json = s.Json
                        .Replace("""{ "level": 1, "max_necromancers": 2, "output_bonus": 0.0000 }""", """{ "level": 1, "max_necromancers": 2, "cost": { "mana": 1 } }""")
                        .Replace("""{ "level": 2, "max_necromancers": 3,""", """{ "level": 3, "max_necromancers": 1,"""),
                }
                : s);
        var e = Assert.Throws<ContentLoadException>(() => ContentLoader.CreateDefault().Load(sources));
        Assert.Contains(e.Errors, x => x.Field == "levels[0].cost");
        Assert.Contains(e.Errors, x => x.Field == "levels[1].level");
        Assert.Contains(e.Errors, x => x.Field == "levels[1].max_necromancers");
    }
}
