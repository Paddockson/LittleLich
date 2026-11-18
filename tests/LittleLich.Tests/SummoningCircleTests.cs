using System;
using System.IO;
using System.Linq;
using LittleLich.Content;
using LittleLich.Core;
using LittleLich.Core.Saves;
using LittleLich.Crypt.Buildings;
using Xunit;

namespace LittleLich.Tests;

/// <summary>Adjustment 01: the Summoning Circle with click power, summon slots and unlocked-only choices.</summary>
public class SummoningCircleTests
{
    private static World Fresh()
    {
        var w = new World();
        foreach (var s in w.Saveables) s.Load(new SaveData());
        Assert.True(w.Research.DebugComplete("summoning"));   // research revamp: Summoning opens the circle, necromancers and wisps
        return w;
    }

    private static SummoningCircle Circle(World w) => w.Buildings.OfType<SummoningCircle>().Single();

    [Fact]
    public void The_circle_is_locked_and_empty_until_summoning_is_researched()
    {
        var w = new World();
        foreach (var s in w.Saveables) s.Load(new SaveData());
        var c = Circle(w);
        Assert.False(c.IsUnlocked);
        Assert.Empty(c.AvailableUnits());
        Assert.True(c.SummonSlots.All(x => x.IsEmpty));
        Assert.Equal(0, c.AddNecromancer(1));
        w.Research.DebugComplete("summoning");
        Assert.True(c.IsUnlocked);
        Assert.Equal("necromancer", c.SummonSlots[0].UnitId);   // filled when research opens the necromancer
    }

    [Fact]
    public void A_new_circle_has_two_slots_with_necromancers_in_the_first()
    {
        var c = Circle(Fresh());
        Assert.Equal(2, c.SummonSlots.Count);
        Assert.Equal("necromancer", c.SummonSlots[0].UnitId);
        Assert.True(c.SummonSlots[1].IsEmpty);
        Assert.Equal(5, c.SummonSlots[0].Required);
        Assert.Equal(1, c.ClickPower);
        Assert.Equal(0.2, c.PowerPerNecromancerPerTick, 9);
    }

    [Fact]
    public void Only_necromancer_summons_are_offered_army_units_come_from_the_lich()
    {
        var w = Fresh();
        var c = Circle(w);
        Assert.Equal(new[] { "necromancer" }, c.AvailableUnits().Select(o => o.Id));
        Assert.False(c.SetUnit(1, "wisp"));                          // adjustment 03: the lich summons army units

        w.Research.DebugComplete("animate_bones");
        w.Research.DebugComplete("elemental_wisps");
        w.Units.Build("skeleton", "copper_sword");
        Assert.Equal(new[] { "necromancer" }, c.AvailableUnits().Select(o => o.Id));
        Assert.False(c.SetUnit(1, "skeleton"));
        Assert.False(c.SetUnit(1, "skeleton_copper_sword"));
        Assert.True(c.SetUnit(1, "necromancer"));                    // the same summon may fill two slots
        Assert.Equal("necromancer", c.SummonSlots[1].UnitId);
    }

    [Fact]
    public void Slots_appear_with_circle_level_and_research_with_no_placeholders()
    {
        var w = Fresh();
        var c = Circle(w);
        var changed = 0;
        c.SlotsChanged += () => changed++;
        c.DebugSetLevel(2);
        Assert.Equal(2, c.SummonSlots.Count);
        c.DebugSetLevel(3);
        Assert.Equal(3, c.SummonSlots.Count);
        c.DebugSetLevel(5);
        Assert.Equal(4, c.SummonSlots.Count);
        Assert.True(changed >= 2);
        w.Research.DebugComplete("summoning_capacity");   // research: +1 summon slot (stat summon_slots@summoning_circle)
        Assert.Equal(5, c.SummonSlots.Count);
        Assert.Equal(Enumerable.Range(0, 5), c.SummonSlots.Select(s => s.Index));
    }

    [Fact]
    public void Clicks_fill_the_first_slot_that_is_not_full_and_a_full_bar_waits_for_its_cost()
    {
        var w = Fresh();
        var c = Circle(w);
        w.Ticks.Unregister(w.Buildings.OfType<ManaWell>().Single());
        w.Storage.Add("mana", 60);                    // enough for one necromancer (50 mana, 25 souls)
        w.Storage.Add("souls", 30);
        for (var i = 0; i < 5; i++) Assert.True(c.Click());
        Assert.Equal(1, w.Lich.Pool.Free);
        Assert.Equal(10, w.Storage.Get("mana"));
        Assert.Equal(5, w.Storage.Get("souls"));
        for (var i = 0; i < 5; i++) Assert.True(c.Click());
        Assert.Equal(1, w.Lich.Pool.Free);            // cannot pay: the bar stays full
        Assert.Equal(1, c.SummonSlots[0].Fraction);
        Assert.Equal("mana", c.SummonSlots[0].WaitingFor);
        Assert.False(c.Click());                      // full and the other slot is empty

        w.Storage.Add("mana", 40);
        w.Storage.Add("souls", 20);
        w.Ticks.RunTicks(1);                          // the waiting bar summons on the next tick, with no power
        Assert.Equal(2, w.Lich.Pool.Free);
        Assert.Null(c.SummonSlots[0].WaitingFor);
        Assert.Equal(0, c.SummonSlots[0].Progress);
    }

    [Fact]
    public void Clearing_a_slot_loses_its_progress_and_an_empty_circle_does_nothing()
    {
        var w = Fresh();
        var c = Circle(w);
        c.Click();
        c.ClearSlot(0);
        Assert.True(c.SummonSlots.All(s => s.IsEmpty));
        Assert.Equal(0, c.SummonSlots[0].Progress);
        Assert.False(c.Click());
        w.Ticks.RunTicks(3);
        Assert.Equal(BuildingStatus.NothingToDo, c.Status);
    }

    private static World Busy()
    {
        var w = Fresh();
        w.Lich.DebugAddNecromancers(3);
        var c = Circle(w);
        c.DebugSetLevel(3);                           // 20 more max necromancers, 3 slots
        c.AddNecromancer(3);                          // 3 x 0.2 x 1.2 = 0.72 power/tick, split three ways
        // Ample stock: slots that share scarce stock are served in slot order within a batch (documented),
        // so this test keeps every cost affordable (three necromancer slots share mana and souls). Storage level 20.
        w.OpenAll();
        w.Buildings.OfType<MagicalStorage>().Single().DebugSetLevel(20);
        w.Research.DebugComplete("animate_bones");
        Assert.True(c.SetUnit(1, "necromancer"));
        Assert.True(c.SetUnit(2, "necromancer"));
        w.Storage.Add("mana", 2_000);
        w.Storage.Add("souls", 1_000);
        w.Inventory.Add("bones", 500);
        w.Ticks.Unregister(w.Buildings.OfType<ManaWell>().Single());
        c.Click();
        c.Click();
        return w;
    }

    private static string Snapshot(World w) => string.Join("|", Circle(w).Save().Node.ToJsonString(), w.Lich.Pool.Total,
        w.Storage.Get("mana"), Circle(w).Status, Circle(w).StatusDetail);

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
        Assert.Equal(Snapshot(live), Snapshot(batched));
    }

    [Fact]
    public void One_hundred_ticks_fill_three_slots_of_necromancers()
    {
        var w = Busy();
        w.Ticks.RunTicks(100);
        Assert.True(w.Lich.Pool.Total > 3);
    }

    [Fact]
    public void Content_checks_the_new_summon_fields()
    {
        static ContentLoadException Fails(string json) => Assert.Throws<ContentLoadException>(() =>
            ContentLoader.CreateDefault().Load(ContentSource.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Data"))
                .Append(new ContentSource("extra.json", json))));

        var e = Fails("""[{ "type": "summon", "id": "bad_necro", "name": "B", "summon_required": 0, "summon_level": 0 }]""");
        Assert.Contains(e.Errors, x => x.Field == "summon_required");
        Assert.Contains(e.Errors, x => x.Field == "summon_level");
        e = Fails("""[{ "type": "summon", "id": "bad_necro", "name": "B", "summon_cost": { "nothing": 1 } }]""");
        Assert.Contains(e.Errors, x => x.Field == "summon_cost");
    }
}

public class SummoningCircleSaveTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeClock _clock = new();
    public void Dispose() => _dir.Dispose();

    private (GameSession, World) Session()
    {
        var w = new World();
        var s = new GameSession(World.SampleContent, new SaveService(_dir.Path, _clock.Get), _clock.Get);
        foreach (var x in w.Saveables) s.Register(x);
        return (s, w);
    }

    [Fact]
    public void Level_slot_units_progress_and_assigned_count_survive_reload()
    {
        var (s1, a) = Session();
        s1.NewGame(1);
        var c = a.Buildings.OfType<SummoningCircle>().Single();
        a.Research.DebugComplete("summoning");
        c.DebugSetLevel(2);
        a.Lich.DebugAddNecromancers(2);
        c.AddNecromancer(2);
        Assert.True(c.SetUnit(1, "necromancer"));
        c.Click();
        c.Click();
        a.Ticks.Unregister(a.Buildings.OfType<ManaWell>().Single());
        a.Ticks.RunTicks(3);
        var progress = c.SummonSlots.Select(s => s.Progress).ToArray();
        s1.Save();

        var (s2, b) = Session();
        s2.LoadSlot(1);
        var d = b.Buildings.OfType<SummoningCircle>().Single();
        Assert.Equal(2, d.Level);
        Assert.Equal(new[] { "necromancer", "necromancer" }, d.SummonSlots.Select(s => s.UnitId));
        Assert.Equal(progress, d.SummonSlots.Select(s => s.Progress).ToArray());
        Assert.Equal(2, d.AssignedNecromancers);
        Assert.Equal(c.Save().Node.ToJsonString(), d.Save().Node.ToJsonString());
    }

    [Fact]
    public void A_save_from_before_the_rework_starts_with_necromancers_in_slot_one()
    {
        var w = new World();
        foreach (var s in w.Saveables.Where(x => x is not SummoningCircle)) s.Load(new SaveData());
        w.Research.DebugComplete("summoning");
        var c = w.Buildings.OfType<SummoningCircle>().Single();
        c.Load(new SaveData().Set("level", 1).Set("carry", 123L).Set("target", 5L).Set("unit_type", "skeleton_copper_sword"));
        Assert.Equal("necromancer", c.SummonSlots[0].UnitId);
        Assert.True(c.SummonSlots[1].IsEmpty);        // the old unit type no longer exists: dropped
        Assert.False(c.Save().Has("target"));
    }
}
