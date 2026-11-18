using System;
using System.IO;
using System.Linq;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Core.Saves;
using LittleLich.Crypt.Buildings;
using Xunit;

namespace LittleLich.Tests;

public class FixedPointTests
{
    [Fact]
    public void Run_carries_remainders_and_drops_progress_when_limited()
    {
        Assert.Equal((2L, 500_000L, false), FixedPoint.Run(0, 2_500_000, 1, 1_000_000, long.MaxValue));
        Assert.Equal((3L, 0L, true), FixedPoint.Run(0, 2_500_000, 2, 1_000_000, 3));
        Assert.Equal((0L, 0L, true), FixedPoint.Run(900_000, 1, 1, 1_000_000, 0));
        Assert.Equal((0L, 400_000L, false), FixedPoint.Run(0, 200_000, 2, 1_000_000, 5));
    }
}

public class BuildingTests
{
    private static World Fresh()
    {
        var w = new World();
        w.Lich.Load(new SaveData());            // new-game defaults
        foreach (var b in w.Buildings) b.Load(new SaveData());
        w.OpenAll();                            // research gates buildings; these tests are about the buildings themselves
        return w;
    }

    private static T B<T>(World w) where T : BuildingBase => w.Buildings.OfType<T>().Single();

    [Fact]
    public void All_seven_buildings_load_in_flow_order_and_only_storage_does_not_tick()
    {
        var w = Fresh();
        Assert.Equal(BuildingIds.All, w.Buildings.Select(b => b.Id));
        Assert.Equal(new[] { "mana_well", "infuser", "soul_well", "transmuter", "summoning_circle", "undead_market" },
            w.Ticks.Registered.Where(r => r.Group == Core.Ticks.TickGroup.Buildings).Where(r => r.Feature is BuildingBase).Select(r => ((BuildingBase)r.Feature).Id));
    }

    [Fact]
    public void The_opening_research_summoning_then_clicks_raise_the_first_necromancer()
    {
        // A new game (no OpenAll): only the Mana Well is open and there are no necromancers.
        var w = new World();
        foreach (var x in w.Saveables) x.Load(new SaveData());
        var r = w.Research;
        var well = B<ManaWell>(w);
        var circle = B<SummoningCircle>(w);
        Assert.Equal(0, w.Lich.Pool.Total);
        Assert.Equal(5, w.Lich.Pool.Max);
        Assert.False(circle.IsUnlocked);

        // The free bench works 1 per tick with nobody on it: Summoning (4 work), then Soul Harvesting (7).
        Assert.Equal(Crypt.Research.StartResult.Ok, r.Start("summoning"));
        w.Ticks.RunTicks(4);
        Assert.True(circle.IsUnlocked);
        Assert.Equal("necromancer", circle.SummonSlots[0].UnitId);
        Assert.Equal(Crypt.Research.StartResult.Ok, r.Start("soul_harvesting"));
        w.Ticks.RunTicks(7);
        w.Ticks.RunTicks(13);                               // 2 souls per tick
        Assert.Equal(26, w.Storage.Get("souls"));
        Assert.Equal(0, w.Storage.Get("mana"));             // nothing makes mana on its own

        // Click the well for 50 mana, then the circle 5 times (5 power, 50 mana and 25 souls per necromancer).
        for (var i = 0; i < 50; i++) Assert.Equal(1, well.Click());
        for (var i = 0; i < 4; i++) Assert.True(circle.Click());
        Assert.Equal(0.8, circle.SummonSlots[0].Fraction, 9);
        Assert.True(circle.Click());                        // the bar fills and summons at once
        Assert.Equal(1, w.Lich.Pool.Free);                  // first necromancer, free and ready
        Assert.Equal(0, w.Storage.Get("mana"));
        Assert.Equal(1, w.Storage.Get("souls"));

        // Staff the well with it (2 mana per tick) and the circle with another (0.2 power: one every 25 ticks).
        Assert.Equal(1, well.AddNecromancer(1));
        Assert.Equal(1, w.Lich.Pool.GetAssigned("mana_well", "main"));
        w.Lich.DebugAddNecromancers(1);
        Assert.Equal(1, circle.AddNecromancer(1));
        w.Ticks.RunTicks(100);
        Assert.Equal(3, w.Lich.Pool.Free);                  // the pool is full at 5
        Assert.Equal(BuildingStatus.Running, well.Status);
        Assert.Equal(BuildingStatus.PoolFull, circle.Status);
    }

    [Fact]
    public void Summoning_waits_with_a_full_bar_at_the_pool_max()
    {
        var w = Fresh();
        var circle = B<SummoningCircle>(w);
        w.Storage.Add("mana", 1_000);
        w.Storage.Add("souls", 100);
        w.Lich.DebugAddNecromancers(3);
        circle.AddNecromancer(3);                   // 3 x 0.2 = 0.6 power/tick
        w.Ticks.RunTicks(1_000);
        Assert.Equal(w.Lich.Pool.Max, w.Lich.Pool.Total);  // 5
        Assert.Equal(BuildingStatus.PoolFull, circle.Status);
        Assert.Equal("max", circle.StatusDetail);
        Assert.Equal(1, circle.SummonSlots[0].Fraction);
        Assert.Equal(SummoningCircle.PoolFull, circle.SummonSlots[0].WaitingFor);
        Assert.Equal(1_000 - 2 * 50, w.Storage.Get("mana"));   // 2 summoned (50 mana, 25 souls), nothing spent while waiting
        Assert.Equal(100 - 2 * 25, w.Storage.Get("souls"));
        Assert.False(circle.Click());                       // the only slot is full: the click goes nowhere
    }

    [Fact]
    public void Circle_levels_raise_max_necromancers()
    {
        var w = Fresh();
        var circle = B<SummoningCircle>(w);
        Assert.Equal(5, w.Lich.Pool.Max);
        circle.DebugSetLevel(3);
        Assert.Equal(25, w.Lich.Pool.Max);
        Assert.Single(w.Lich.Stats.Modifiers, m => m.Source == SummoningCircle.Source);
    }

    [Fact]
    public void A_full_resource_stops_production_without_losing_staff()
    {
        var w = Fresh();
        var well = B<ManaWell>(w);
        var circle = B<SummoningCircle>(w);
        circle.ClearSlot(0); // keep the circle from eating the mana
        well.DebugSetLevel(5);                          // holds 9
        w.Lich.DebugAddNecromancers(5);
        Assert.Equal(5, well.AddNecromancer(5));        // 5 x 2 x 1.0004 = 10.002 mana/tick
        w.Storage.Add("mana", 990);
        w.Ticks.RunTicks(10);
        Assert.Equal(1_000, w.Storage.Get("mana"));
        Assert.Equal(BuildingStatus.OutputFull, well.Status);
        Assert.Equal("mana", well.StatusDetail);
        Assert.Equal(5, well.Staffed);

        w.Storage.TryRemove("mana", 7);
        w.Ticks.RunTicks(2);
        Assert.Equal(1_000, w.Storage.Get("mana"));
        Assert.Equal(5, well.Staffed);
    }

    [Fact]
    public void Staff_is_limited_by_slots()
    {
        var w = Fresh();
        var well = B<Infuser>(w);                       // formula places: 3 + 1 per level
        w.Lich.DebugAddNecromancers(100);
        Assert.Equal(3, well.Slots);
        Assert.Equal(3, well.AddNecromancer(100));
        Assert.Equal(0, well.AddNecromancer(1));
        well.DebugSetLevel(3);
        Assert.Equal(5, well.Slots);
        Assert.Equal(2, well.AddNecromancer(100));
        Assert.Equal(95, w.Lich.Pool.Free);
        Assert.Equal(5, well.RemoveNecromancer(50));
    }

    [Fact]
    public void Upgrades_cost_coins_and_grow()
    {
        var w = Fresh();
        var well = B<SoulWell>(w);                      // formula costs: 100 gold x 1.5 per level
        Assert.Equal(100, well.UpgradeCost);
        Assert.False(well.TryUpgrade());                // no coins
        w.Storage.Add("gold", 1_000);
        Assert.True(well.TryUpgrade());
        Assert.Equal(2, well.Level);
        Assert.Equal(900, w.Storage.Get("gold"));
        Assert.Equal(150, well.UpgradeCost);
        Assert.Equal(1.1, well.LevelMultiplier, 9);
    }

    [Fact]
    public void Magical_storage_raises_every_cap()
    {
        var w = Fresh();
        var st = B<MagicalStorage>(w);
        Assert.Equal(1_000, w.Storage.Cap("mana"));
        st.DebugSetLevel(3);
        Assert.Equal(2_000, w.Storage.Cap("mana"));
        Assert.Equal(200, w.Storage.Cap("souls"));
        Assert.Equal(BuildingStatus.Passive, st.Status);
    }

    [Fact]
    public void Infuser_and_soul_well_chain_mana_into_souls()
    {
        var w = Fresh();
        B<SummoningCircle>(w).ClearSlot(0);
        w.Lich.DebugAddNecromancers(10);
        var infuser = B<Infuser>(w);
        infuser.AddNecromancer(2);                     // 1 infusion/tick, 2 mana each
        B<SoulWell>(w).AddNecromancer(3);              // 0.75 work/tick, 2 work per soul, 1 of each element per soul
        Assert.Equal("fire", infuser.Element);         // the first element by default
        infuser.Select("water");
        w.Storage.Add("mana", 1_000);
        w.Storage.Add("fire", 50);
        w.Storage.Add("air", 50);
        w.Storage.Add("earth", 50);
        w.Ticks.RunTicks(1);
        Assert.Equal(1, w.Storage.Get("water"));       // the chosen element, from 2 mana
        Assert.Equal(50, w.Storage.Get("fire"));            // not made: water was chosen
        w.Ticks.RunTicks(39);
        Assert.True(w.Storage.Get("souls") > 0);
        Assert.Equal(w.Storage.Get("fire"), w.Storage.Get("air"));   // one of each element per soul
        Assert.Throws<ArgumentException>(() => infuser.Select("lightning"));
    }

    [Fact]
    public void Infuser_element_survives_reload()
    {
        var w = Fresh();
        B<Infuser>(w).Select("earth");
        var saved = B<Infuser>(w).Save();
        var v = Fresh();
        B<Infuser>(v).Load(saved);
        Assert.Equal("earth", B<Infuser>(v).Element);
    }

    [Fact]
    public void Converters_pause_with_the_missing_input()
    {
        var w = Fresh();
        var soul = B<SoulWell>(w);
        w.Ticks.RunTicks(1);
        Assert.Equal(BuildingStatus.Idle, soul.Status);   // unstaffed, no base work: idle, not "missing"
        w.Lich.DebugAddNecromancers(3);
        soul.AddNecromancer(3);
        w.Ticks.RunTicks(5);
        Assert.Equal(BuildingStatus.MissingInput, soul.Status);
        Assert.Equal("fire", soul.StatusDetail);
    }

    [Fact]
    public void Transmuter_runs_the_chosen_resource_conversion()
    {
        var w = Fresh();
        var t = B<Transmuter>(w);
        w.Lich.DebugAddNecromancers(2);
        t.AddNecromancer(2);                           // 1 work/tick
        w.Ticks.RunTicks(3);
        Assert.Equal(BuildingStatus.NothingToDo, t.Status);
        t.Select("copper_to_iron");                    // 3 work per conversion
        w.Stock.Add("copper_ore", 10);
        w.Ticks.RunTicks(9);
        Assert.Equal(2, w.Stock.Get("iron_ore"));
        Assert.Equal(2, w.Stock.Get("copper_ore"));
        Assert.Equal(BuildingStatus.MissingInput, t.Status);
        Assert.Throws<ArgumentException>(() => t.Select("lead_to_gold"));
    }

    [Fact]
    public void Market_sells_down_to_the_reserve_for_coins()
    {
        var w = Fresh();
        var m = B<UndeadMarket>(w);
        w.Stock.Add("iron_bar", 20);
        w.Inventory.Add("copper_sword", 2);
        m.SetSell("copper_sword", true, 0);
        m.SetSell("iron_bar", true, 15);
        Assert.Throws<ArgumentException>(() => m.SetSell("mana", true, 0)); // no price
        w.Ticks.RunTicks(10);                          // 1 sale/tick: 2 swords, then 5 bars, then nothing
        Assert.Equal(0, w.Inventory.Get("copper_sword"));
        Assert.Equal(15, w.Stock.Get("iron_bar"));
        Assert.Equal(2 * 40 + 5 * 12, w.Storage.Get("gold"));
        Assert.Equal(BuildingStatus.NothingToDo, m.Status);
    }

    [Fact]
    public void Market_stops_when_coins_are_full_and_keeps_the_goods()
    {
        var w = Fresh();
        var m = B<UndeadMarket>(w);
        w.Storage.Add("gold", 999_990);
        w.Stock.Add("iron_bar", 10);
        m.SetSell("iron_bar", true, 0);
        w.Ticks.RunTicks(5);
        Assert.Equal(999_990, w.Storage.Get("gold"));
        Assert.Equal(10, w.Stock.Get("iron_bar"));
        Assert.Equal(BuildingStatus.OutputFull, m.Status);
    }

    private static World Workshop()
    {
        var w = Fresh();
        w.Lich.DebugAddNecromancers(40);
        B<ManaWell>(w).AddNecromancer(5);
        B<Infuser>(w).AddNecromancer(3);
        B<SoulWell>(w).AddNecromancer(3);
        var t = B<Transmuter>(w);
        t.AddNecromancer(3);
        t.Select("copper_to_iron");
        w.Stock.Add("copper_ore", 333);
        B<SummoningCircle>(w).AddNecromancer(3);
        var m = B<UndeadMarket>(w);
        m.AddNecromancer(2);
        w.Stock.Add("iron_bar", 77);
        w.Inventory.Add("rot_bomb", 9);
        m.SetSell("rot_bomb", true, 2);
        m.SetSell("iron_bar", true, 10);
        return w;
    }

    private static string Snapshot(World w) =>
        string.Join("|", w.Storage.Save().Node.ToJsonString(), w.Inventory.Save().Node.ToJsonString(),
            w.Lich.Pool.Total, string.Join(";", w.Buildings.Select(b => b.Save().Node.ToJsonString() + b.Status + b.StatusDetail)));

    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(400)]
    public void Each_building_on_its_own_matches_live_and_batched(int n)
    {
        // Buildings in one batch run in flow order, each processing all n ticks (phase 3 contract), so the
        // chain (well -> infuser -> soul well) can differ from tick-by-tick. Each building alone must match exactly.
        foreach (var id in BuildingIds.All)
        {
            var live = Workshop();
            var batched = Workshop();
            foreach (var w in new[] { live, batched })
                foreach (var b in w.Buildings.Where(b => b.Id != id && b.Ticks)) w.Ticks.Unregister(b);
            for (var i = 0; i < n; i++) live.Ticks.RunTicks(1);
            batched.Ticks.RunTicks(n);
            Assert.True(Snapshot(live) == Snapshot(batched), $"{id} differs after {n} ticks");
        }
    }
}

public class BuildingSaveTests : IDisposable
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
    public void Levels_staff_choices_and_effects_survive_reload()
    {
        var (s1, a) = Session();
        s1.NewGame(1);
        // Research opens what this test uses (and is saved with it): the circle and wisps, Storage, Transmuter, Market.
        foreach (var n in new[] { "summoning", "basic_infusion", "mana_storage", "transmute", "undead_market" }) Assert.True(a.Research.DebugComplete(n));
        a.Lich.DebugAddNecromancers(20);
        var circle = a.Buildings.OfType<SummoningCircle>().Single();
        circle.DebugSetLevel(4);
        Assert.Equal(3, circle.SummonSlots.Count);          // slots_by_level: 3 from level 3
        Assert.True(circle.SetUnit(1, "wisp"));            // wisp: from Summoning
        a.Buildings.OfType<ManaWell>().Single().DebugSetLevel(4);   // holds 6
        a.Buildings.OfType<ManaWell>().Single().AddNecromancer(4);
        a.Buildings.OfType<Transmuter>().Single().Select("bones_to_coal");
        a.Buildings.OfType<UndeadMarket>().Single().SetSell("coal", true, 50);
        a.Buildings.OfType<MagicalStorage>().Single().DebugSetLevel(2);
        a.Storage.Add("mana", 1_400);
        a.Ticks.RunTicks(7);
        s1.Save();

        var (s2, b) = Session();
        s2.LoadSlot(1);
        foreach (var (x, y) in a.Buildings.Zip(b.Buildings))
            Assert.Equal(x.Save().Node.ToJsonString(), y.Save().Node.ToJsonString());
        Assert.Equal(50, b.Lich.Pool.Max);   // 10 base + 10 debug (DebugAddNecromancers) + 30 from circle level 4
        Assert.Equal(1_500, b.Storage.Cap("mana"));
        Assert.Equal(a.Storage.Get("mana"), b.Storage.Get("mana"));   // above the base cap, kept on load
        Assert.Equal(4, b.Buildings.OfType<ManaWell>().Single().Staffed);
        Assert.Equal(new[] { "necromancer", "wisp", null }, b.Buildings.OfType<SummoningCircle>().Single().SummonSlots.Select(s => s.UnitId));
        Assert.Equal("bones_to_coal", b.Buildings.OfType<Transmuter>().Single().SelectedRecipe);
        Assert.Equal(new SellEntry("coal", true, 50), b.Buildings.OfType<UndeadMarket>().Single().Entry("coal"));

        a.Ticks.RunTicks(50);
        b.Ticks.RunTicks(50);
        Assert.Equal(a.Lich.Pool.Total, b.Lich.Pool.Total);
        Assert.Equal(a.Storage.Get("mana"), b.Storage.Get("mana"));
    }
}

public class BuildingContentTests
{
    private static ContentLoadException Fails(string json) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.CreateDefault().Load(
            ContentSource.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Data")).Append(new ContentSource("x.json", json))));

    [Fact]
    public void Transmutations_are_resources_only() =>
        Assert.Contains(Fails("""[{ "type": "transmutation", "id": "x", "name": "X", "inputs": { "coal": 1 }, "output": { "rot_bomb": 1 }, "work": 1 }]""").Errors,
            e => e.Message.Contains("resources and materials only"));

    [Fact]
    public void Unknown_building_ids_and_summons_are_rejected()
    {
        Assert.Contains(Fails("""[{ "type": "mana_well", "id": "mana_tower", "name": "X" }]""").Errors, e => e.Field == "id");
        Assert.Contains(Fails("""[{ "type": "summon", "id": "skeleton", "name": "S", "summons": "skeleton", "summon_cost": {}, "summon_required": 1 }]""").Errors, e => e.Field == "summons");
    }
}
