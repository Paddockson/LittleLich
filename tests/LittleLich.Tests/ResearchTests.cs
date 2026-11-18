using System;
using System.IO;
using System.Linq;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Core.Saves;
using LittleLich.Crypt.Buildings;
using LittleLich.Crypt.Research;
using Xunit;

namespace LittleLich.Tests;

/// <summary>Research revamp: tomes, visibility, benches, upgrade nodes, effects, batching.</summary>
public class ResearchTests
{
    private static World Fresh()
    {
        var w = new World();
        foreach (var s in w.Saveables) s.Load(new SaveData());
        return w;
    }

    private static T B<T>(World w) where T : BuildingBase => w.Buildings.OfType<T>().Single();

    private static void Done(World w, params string[] ids)
    {
        foreach (var id in ids) Assert.True(w.Research.DebugComplete(id), id);
    }

    [Fact]
    public void Two_tomes_load_with_one_root_each_and_only_necromancy_is_owned()
    {
        var r = Fresh().Research;
        Assert.Equal(new[] { "necromancy", "infusion" }, r.AllTomes.Select(t => t.Id));
        Assert.Equal(33, r.AllTomes[0].Nodes.Count);
        Assert.Equal(27, r.AllTomes[1].Nodes.Count);
        Assert.IsType<NecromancyTome>(r.AllTomes[0]);
        Assert.IsType<InfusionTome>(r.AllTomes[1]);
        Assert.Equal(new[] { "necromancy" }, r.Tomes.Select(t => t.Id));
        Assert.Equal(21, r.AllTomes.SelectMany(t => t.Nodes).Count(n => n.IsUpgrade));   // the two Research Assistance nodes are one-off
        Assert.Equal(2, r.Node("mage_hand_1")!.TimeMultiplier);
    }

    [Fact]
    public void Every_node_of_a_found_tome_is_shown_and_locked_ones_wait_for_their_requirements()
    {
        var r = Fresh().Research;
        var necromancy = r.AllTomes[0].Nodes.Select(n => n.Id).ToList();
        Assert.All(necromancy, id => Assert.True(r.IsVisible(id), id));            // plan ahead: the whole tree
        Assert.Equal(NodeState.Available, r.State("summoning"));
        Assert.Equal(NodeState.Locked, r.State("dark_recruitment"));
        Assert.Equal(new[] { "soul_harvesting" }, r.MissingRequirements("dark_recruitment"));
        Assert.Equal(StartResult.NotAvailable, r.Start("dark_recruitment"));
        Assert.Equal(NodeState.Hidden, r.State("basic_infusion"));                 // Infusion not found yet
        Assert.False(r.IsVisible("basic_infusion"));

        var w = Fresh();
        Done(w, "summoning");
        Assert.Equal(NodeState.Done, w.Research.State("summoning"));
        Assert.Equal(NodeState.Available, w.Research.State("soul_harvesting"));
        Assert.Equal(NodeState.Available, w.Research.State("necrotic_blast"));
        Assert.Equal(NodeState.Locked, w.Research.State("dark_recruitment"));
        w.Research.Reveal = RevealMode.AvailableOnly;                               // the old reveal rule, still available
        Assert.False(w.Research.IsVisible("dark_recruitment"));
        Assert.True(w.Research.IsVisible("soul_harvesting"));
    }

    [Fact]
    public void An_unstaffed_bench_works_one_per_tick_and_summoning_opens_the_circle()
    {
        var w = Fresh();
        var r = w.Research;
        var circle = B<SummoningCircle>(w);
        Assert.Single(r.Benches);
        Assert.Equal(2, r.Slots);
        Assert.False(circle.IsUnlocked);
        Assert.Equal(StartResult.NotAvailable, r.Start("soul_harvesting"));
        Assert.Equal(StartResult.Ok, r.Start("summoning"));
        Assert.Equal(StartResult.NotAvailable, r.Start("summoning"));         // already on a bench
        Assert.Equal(1, r.WorkPerTick(r.Benches[0]));                 // (1 + 0.5 x 0) x 1.1^0 x 1
        w.Ticks.RunTicks(3);
        Assert.Equal(NodeState.Researching, r.State("summoning"));
        Assert.Equal(0.75, r.Progress(r.Benches[0]), 9);
        w.Ticks.RunTicks(1);
        Assert.Equal(NodeState.Done, r.State("summoning"));
        Assert.True(r.Benches[0].IsFree);
        Assert.True(circle.IsUnlocked);
        Assert.False(w.Unlocks.IsLocked(Unlocks.Unit, "necromancer"));
        Assert.False(w.Units.IsBaseLocked("wisp"));
        Assert.Equal(new[] { "necromancer", "wisp" }, circle.AvailableUnits().Select(o => o.Id));
    }

    [Fact]
    public void Necromancers_speed_up_a_bench_up_to_its_room()
    {
        var w = Fresh();
        var r = w.Research;
        w.Lich.DebugAddNecromancers(5);
        Assert.Equal(2, r.AddNecromancer(0, 5));                          // a bench holds 2
        Assert.Equal(2.0, r.WorkPerTick(r.Benches[0]), 9);                // 1 + 0.5 x 2
        Done(w, "necro_capacity");                                         // +1 room per level
        Assert.Equal(3, r.Slots);
        Done(w, "dark_empowerment");                                       // doubles room in workshops and benches
        Assert.Equal(6, r.Slots);
    }

    [Fact]
    public void Upgrade_nodes_level_up_on_the_same_bench_with_growing_work()
    {
        var w = Fresh();
        var r = w.Research;
        Done(w, "summoning", "soul_harvesting", "dark_recruitment");
        Assert.Equal(15, w.Lich.Pool.Max);                                 // 5 + 10
        w.Lich.DebugAddNecromancers(2);
        r.AddNecromancer(0, 2);                                            // 2 work per tick
        Assert.Equal(StartResult.Ok, r.Start("dark_expansion_1"));
        Assert.Equal(300, r.WorkNeeded("dark_expansion_1"));
        w.Ticks.RunTicks(150);
        Assert.Equal(1, r.Level("dark_expansion_1"));
        Assert.Equal(345, r.WorkNeeded("dark_expansion_1"), 6);           // 300 x 1.15
        Assert.Equal(NodeState.Researching, r.State("dark_expansion_1"));  // keeps going
        Assert.Equal(16, w.Lich.Pool.Max);                                 // +1 per level
        w.Ticks.RunTicks(173);
        Assert.Equal(2, r.Level("dark_expansion_1"));
        Assert.Equal(new Modifier("max_necromancers", ModifierOp.Add, 2, "research:dark_expansion_1"),
            w.Lich.Stats.Modifiers.Single(m => m.Source == "research:dark_expansion_1"));
        Assert.Equal(100, r.MaxLevel("dark_expansion_1"));
    }

    [Fact]
    public void Cancelling_keeps_progress_for_later()
    {
        var w = Fresh();
        var r = w.Research;
        Done(w, "summoning");
        r.Start("necrotic_blast");                                          // 32 work
        w.Ticks.RunTicks(10);
        Assert.True(r.Cancel(0));
        Assert.Equal(10, r.WorkDone("necrotic_blast"), 9);
        Assert.Equal(NodeState.Available, r.State("necrotic_blast"));
        r.Start("soul_harvesting");
        w.Ticks.RunTicks(7);
        Assert.Equal(NodeState.Done, r.State("soul_harvesting"));
        r.Start("necrotic_blast");
        w.Ticks.RunTicks(21);
        Assert.Equal(NodeState.Researching, r.State("necrotic_blast"));
        w.Ticks.RunTicks(1);
        Assert.Equal(NodeState.Done, r.State("necrotic_blast"));
        Assert.Contains("necrotic_blast", w.Unlocks.UnlockedOf(Unlocks.Spell));
    }

    [Fact]
    public void Dropping_a_node_on_a_bench_starts_swaps_or_moves_it_and_keeps_progress()
    {
        var w = Fresh();
        var r = w.Research;
        Done(w, "summoning", "research_assistance_1");                       // two benches
        Assert.Equal(2, r.Benches.Count);
        Assert.False(r.CanPlace("dark_recruitment", 0));                     // locked: needs Soul Harvesting
        Assert.Equal(StartResult.NotAvailable, r.Place("dark_recruitment", 0));

        Assert.Equal(StartResult.Ok, r.Place("necrotic_blast", 1));          // a chosen bench, not the first free one
        Assert.True(r.Benches[0].IsFree);
        Assert.Equal("necrotic_blast", r.Benches[1].NodeId);
        w.Ticks.RunTicks(10);

        Assert.Equal(StartResult.Ok, r.Place("soul_harvesting", 1));         // swap: necrotic blast comes off, progress kept
        Assert.Equal("soul_harvesting", r.Benches[1].NodeId);
        Assert.Equal(NodeState.Available, r.State("necrotic_blast"));
        Assert.Equal(10, r.WorkDone("necrotic_blast"), 9);

        w.Ticks.RunTicks(3);
        Assert.Equal(StartResult.Ok, r.Place("soul_harvesting", 0));         // move to another bench with its progress
        Assert.True(r.Benches[1].IsFree);
        Assert.Equal(3, r.WorkDone("soul_harvesting"), 9);
        Assert.False(r.CanPlace("soul_harvesting", 0));                      // already there
        w.Ticks.RunTicks(4);
        Assert.Equal(NodeState.Done, r.State("soul_harvesting"));
        Assert.False(r.CanPlace("summoning", 1));                            // done nodes cannot be placed
    }

    [Fact]
    public void Infusion_is_found_by_clearing_the_graveyard()
    {
        var w = Fresh();
        var r = w.Research;
        Assert.False(r.IsOwned("infusion"));
        w.WorldMap.AddUnits("skeleton", 40);
        Assert.Equal(LittleLich.World.SendResult.Ok, w.WorldMap.Send("graveyard", new LittleLich.World.ArmyPlan().Set(Sections.Front, "skeleton", 40)));
        for (var i = 0; i < 600 && w.WorldMap.Battle is not null; i++) w.WorldMap.Advance(1);
        Assert.Equal(LittleLich.World.AreaState.Cleared, w.WorldMap.State("graveyard"));
        Assert.True(r.IsOwned("infusion"));
        Assert.Equal(new[] { "necromancy", "infusion" }, r.Tomes.Select(t => t.Id));
        Assert.Equal(NodeState.Available, r.State("basic_infusion"));
    }

    [Fact]
    public void Research_opens_workshop_buildings_units_and_recipes()
    {
        var w = Fresh();
        Assert.True(B<ManaWell>(w).IsUnlocked);                          // open from the start
        Assert.False(B<Infuser>(w).IsUnlocked);
        Assert.Equal(0, B<Infuser>(w).AddNecromancer(1));
        Done(w, "basic_infusion");
        Assert.True(B<Infuser>(w).IsUnlocked);
        Assert.False(B<MagicalStorage>(w).IsUnlocked);
        Done(w, "mana_storage");
        Assert.True(B<MagicalStorage>(w).IsUnlocked);
        Done(w, "transmute", "soul_well", "undead_market");
        Assert.True(w.Buildings.All(b => b.IsUnlocked || b is SummoningCircle));

        var t = B<Transmuter>(w);
        Assert.True(t.IsLocked("forge_sword"));
        Assert.False(t.IsLocked("bones_to_coal"));                        // not gated by research
        Done(w, "summon_gear");
        Assert.False(t.IsLocked("forge_sword"));
        Assert.False(t.IsLocked("carve_staff"));

        Assert.True(w.Units.IsBaseLocked("skeleton"));
        Done(w, "animate_bones");
        Assert.False(w.Units.IsBaseLocked("skeleton"));
        Assert.True(w.Units.IsBaseLocked("skeleton_golem"));
    }

    [Fact]
    public void Capacity_and_cost_research_changes_the_numbers_the_game_uses()
    {
        var w = Fresh();
        var circle = B<SummoningCircle>(w);
        Done(w, "summoning");
        Assert.Equal(new[] { "mana:50", "souls:25" }, circle.Option("necromancer")!.Cost.Select(c => $"{c.Id}:{c.Count}"));
        Done(w, "dark_recruitment");                                       // +10 max, x0.75 cost (rounded up)
        Assert.Equal(15, w.Lich.Pool.Max);
        Assert.Equal(new[] { "mana:38", "souls:19" }, circle.Option("necromancer")!.Cost.Select(c => $"{c.Id}:{c.Count}"));
        Done(w, "dark_efficiency");                                        // x0.5 more: 50 x 0.375 = 18.75
        Assert.Equal(new[] { "mana:19", "souls:10" }, circle.Option("necromancer")!.Cost.Select(c => $"{c.Id}:{c.Count}"));
        Done(w, "dark_legion");                                            // doubles max necromancers
        Assert.Equal(30, w.Lich.Pool.Max);

        Done(w, "animate_bones", "bring_bones");                           // skeletons 1% cheaper per level, rounded up
        Assert.Equal(new[] { "mana:149", "souls:10", "bones:10" }, circle.Option("skeleton")!.Cost.Select(c => $"{c.Id}:{c.Count}"));
        Assert.Equal(149, w.Units.BaseCost("skeleton")[0].Count);         // the circle pays what it shows

        Assert.Equal(2, circle.SummonSlots.Count);
        Done(w, "summoning_capacity");
        Assert.Equal(3, circle.SummonSlots.Count);
    }

    [Fact]
    public void Output_research_reaches_the_mana_well_infuser_and_souls()
    {
        var w = Fresh();
        var well = B<ManaWell>(w);
        w.Lich.DebugAddNecromancers(1);
        well.AddNecromancer(1);
        Assert.Equal(2.0, well.OutputPerTick(), 9);
        Done(w, "embodied_mana");                                          // +0.05% per level
        Assert.Equal(2 * 1.0005, well.OutputPerTick(), 9);
        Done(w, "necro_mana");                                             // +1 mana per necromancer per level
        Assert.Equal(3 * 1.0005, well.OutputPerTick(), 9);
        Done(w, "mana_proficiency");                                       // output x1.1
        Assert.Equal(3 * 1.0005 * 1.1, well.OutputPerTick(), 9);
        Done(w, "mana_stream");                                            // +0.05% efficiency per necromancer per level
        Assert.Equal(3 * (1.0005 + 0.0005) * 1.1, well.OutputPerTick(), 9);

        Assert.Equal(1, well.ClickPower);
        Done(w, "mage_hand_1");
        Assert.Equal(2, well.ClickPower);
        Assert.Equal(2, well.MaxNecromancers);
        Done(w, "magical_efficiency");                                     // doubles the well's room
        Assert.Equal(4, well.MaxNecromancers);

        Assert.Equal(1, B<Infuser>(w).ElementsPerOp);
        Done(w, "elemental_proficiency");
        Assert.Equal(2, B<Infuser>(w).ElementsPerOp);

        Done(w, "soul_harvesting");                                        // 2 souls per tick
        w.Ticks.RunTicks(10);
        Assert.Equal(20, w.Storage.Get("souls"));
    }

    [Fact]
    public void Level_caps_and_benches_come_from_research()
    {
        var w = Fresh();
        var r = w.Research;
        Assert.Equal(10, B<ManaWell>(w).MaxLevel);
        Assert.Equal(20, r.BenchMaxLevel);
        Done(w, "upgraded_infusions");                                     // +10 max level for nodes and buildings
        Assert.Equal(20, B<ManaWell>(w).MaxLevel);
        Assert.Equal(30, r.BenchMaxLevel);
        Assert.Equal(110, r.MaxLevel("dark_expansion_1"));
        Assert.Equal(1, r.MaxLevel("research_assistance_1"));             // one-off nodes do not grow
        Assert.Single(r.Benches);
        Done(w, "research_assistance_1");
        Assert.Equal(2, r.Benches.Count);
        Done(w, "research_assistance_2");
        Assert.Equal(3, r.Benches.Count);
        Assert.Equal(10, w.Lich.Stats.Get(LichStats.MaxInsight).ToDouble(), 9);   // increased_focus not done: base 10
        Done(w, "increased_focus");                                        // "insight" in the data
        Assert.Equal(14, w.Lich.Stats.Get(LichStats.MaxInsight).ToDouble(), 9);
    }

    [Fact]
    public void Trigger_and_chance_effects_are_listed_for_their_systems()
    {
        var w = Fresh();
        Assert.Empty(w.Research.ActiveEffects("trigger"));
        Done(w, "necrotic_harvest", "forge_master");
        Assert.Equal("necrotic_harvest", w.Research.ActiveEffects("trigger").Single().NodeId);
        var c = w.Research.ActiveEffects("chance").Single();
        Assert.Equal(("forge_master", 1), (c.NodeId, c.Level));
    }

    private static World Busy()
    {
        var w = Fresh();
        var r = w.Research;
        Done(w, "summoning", "soul_harvesting", "dark_recruitment");
        r.GrantTome("infusion");
        Done(w, "basic_infusion", "mana_storage", "increased_focus", "research_assistance_1");
        w.Lich.DebugAddNecromancers(4);
        r.AddNecromancer(0, 2);
        r.AddNecromancer(1, 1);
        Assert.Equal(StartResult.Ok, r.Start("dark_expansion_1", 0));
        Assert.Equal(StartResult.Ok, r.Start("research_buddy", 1));   // each level raises research efficiency
        return w;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1_000)]
    [InlineData(5_000)]
    public void Ticks_in_one_call_equal_single_ticks(int n)
    {
        var live = Busy();
        for (var i = 0; i < n; i++) live.Ticks.RunTicks(1);
        var batched = Busy();
        batched.Ticks.RunTicks(n);
        Assert.Equal(live.Research.Save().Node.ToJsonString(), batched.Research.Save().Node.ToJsonString());
        Assert.Equal(live.Lich.Pool.Max, batched.Lich.Pool.Max);
        Assert.Equal(live.Storage.Get("souls"), batched.Storage.Get("souls"));
        if (n >= 1_000) Assert.True(live.Research.Level("research_buddy") > 1);
    }
}

public class ResearchSaveTests : IDisposable
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
    public void Tomes_levels_kept_progress_benches_and_bench_level_survive_reload()
    {
        var (s1, a) = Session();
        s1.NewGame(1);
        var r = a.Research;
        r.DebugComplete("summoning");
        r.DebugComplete("soul_harvesting");
        r.DebugComplete("dark_recruitment");
        r.DebugComplete("dark_expansion_1");
        r.GrantTome("infusion");
        r.DebugComplete("basic_infusion");
        r.DebugComplete("mana_storage");
        r.DebugComplete("research_assistance_1");
        a.Storage.Add("gold", 120);
        a.Storage.Add("mana", 320);
        Assert.True(r.TryUpgradeBench());
        r.Start("necrotic_blast");
        a.Ticks.RunTicks(5);
        r.Cancel(0);
        r.Start("dark_expansion_1", 1);
        a.Ticks.RunTicks(7);
        s1.Save();

        var (s2, b) = Session();
        s2.LoadSlot(1);
        var q = b.Research;
        Assert.Equal(r.Save().Node.ToJsonString(), q.Save().Node.ToJsonString());
        Assert.True(q.IsOwned("infusion"));
        Assert.Equal(1, q.Level("dark_expansion_1"));
        Assert.Equal(2, q.Benches.Count);
        Assert.Equal(2, q.BenchLevel);
        Assert.Equal("dark_expansion_1", q.Benches[1].NodeId);
        Assert.True(q.WorkDone("necrotic_blast") > 0);
        Assert.Equal(a.Lich.Pool.Max, b.Lich.Pool.Max);
        Assert.True(b.Buildings.OfType<MagicalStorage>().Single().IsUnlocked);
        Assert.Equal(a.Lich.Stats.Modifiers.Count(m => m.Source.StartsWith("research:")), b.Lich.Stats.Modifiers.Count(m => m.Source.StartsWith("research:")));
    }

    [Fact]
    public void A_save_from_the_old_research_starts_the_new_trees_fresh()
    {
        var w = new World();
        foreach (var s in w.Saveables) s.Load(new SaveData());
        w.Research.Load(new SaveData()
            .Set("done", new[] { "nec1_grave_lore", "nec1_raise_skeletons" }.ToList())
            .Set("benches", new[] { new ResearchManager.BenchDto { Node = "nec1_bone_archers", Progress = 99 } }.ToList())
            .Set("bench_level", 3));
        Assert.Equal(new[] { "necromancy" }, w.Research.Tomes.Select(t => t.Id));
        Assert.All(w.Research.AllTomes.SelectMany(t => t.Nodes), n => Assert.Equal(0, w.Research.Level(n.Id)));
        Assert.True(w.Research.Benches[0].IsFree);
        Assert.Equal(3, w.Research.BenchLevel);
        Assert.Equal(NodeState.Available, w.Research.State("summoning"));
    }
}

public class ResearchContentTests
{
    private static ContentLoadException Fails(string json) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.CreateDefault().Load(
            ContentSource.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Data")).Append(new ContentSource("x.json", json))));

    [Fact]
    public void Bad_effects_kinds_and_references_are_rejected()
    {
        var e = Fails("""
            [ { "type": "research_node", "tome": "necromancy", "id": "x1", "name": "X", "requires": [ "summoning" ], "kind": "sideways", "work": 0,
                "effects": [ { "type": "explode" }, { "type": "modify", "stat": "max_necromancers", "op": "pow", "value": 2, "target": "everyone" },
                             { "type": "unlock", "kind": "car", "id": "x" }, { "type": "chance", "on": "transmute", "chance": 2, "give": "x" },
                             { "type": "modify", "stat": "a", "value": 1, "per_level": true } ] } ]
            """);
        foreach (var f in new[] { "kind", "work", "effects[0].type", "effects[1].op", "effects[1].target", "effects[2].kind", "effects[3].chance", "effects[4].per_level" })
            Assert.Contains(e.Errors, x => x.Field == f);
        e = Fails("""[ { "type": "research_node", "tome": "necromancy", "id": "x2", "name": "X", "requires": [ "summoning" ], "work": 5, "effects": [ { "type": "unlock", "kind": "unit", "id": "dragon" } ] } ]""");
        Assert.Contains(e.Errors, x => x.Field == "effects[0].id" && x.Message.Contains("dragon"));
        e = Fails("""[ { "type": "research_node", "tome": "infusion", "id": "x3", "name": "X", "requires": [ "summoning" ], "work": 5 } ]""");
        Assert.Contains(e.Errors, x => x.Field == "requires" && x.Message.Contains("another tome"));
    }

    [Fact]
    public void A_second_root_or_a_cycle_is_rejected()
    {
        var e = Fails("""[ { "type": "research_node", "tome": "necromancy", "id": "second_root", "name": "X", "work": 5 } ]""");
        Assert.Contains(e.Errors, x => x.Id == "necromancy" && x.Message.Contains("exactly one root"));
        e = Fails("""
            [ { "type": "research_node", "tome": "necromancy", "id": "c1", "name": "C1", "requires": [ "summoning", "c2" ], "work": 5 },
              { "type": "research_node", "tome": "necromancy", "id": "c2", "name": "C2", "requires": [ "c1" ], "work": 5 } ]
            """);
        Assert.Contains(e.Errors, x => x.Message.Contains("cycle"));
    }

    [Fact]
    public void Bench_costs_round_to_two_significant_figures_with_halves_up()
    {
        Assert.Equal(120, ResearchBenchDefinition.RoundSig(50 * 2.3, 2));     // 114.999... in floating point
        Assert.Equal(58, ResearchBenchDefinition.RoundSig(25 * 2.3, 2));
        Assert.Equal(1100, ResearchBenchDefinition.RoundSig(1050, 2));
        Assert.Equal(1500000, ResearchBenchDefinition.RoundSig(1499999.9999999998, 2));
    }
}

public class StatTargetTests
{
    [Fact]
    public void Targeted_modifiers_apply_only_to_their_subjects()
    {
        var s = new StatSheet();
        s.Add(new Modifier("efficiency@mana_well_and_infuser", ModifierOp.Add, 0.5, "t"));
        s.Add(new Modifier("efficiency@soul_well", ModifierOp.Mul, 3, "t"));
        s.Add(new Modifier("efficiency", ModifierOp.Add, 10, "t"));          // untargeted
        Assert.Equal(1.5, s.GetFor("efficiency", "mana_well", Num.One, includeUntargeted: false).ToDouble(), 9);
        Assert.Equal(1.5, s.GetFor("efficiency", "infuser", Num.One, includeUntargeted: false).ToDouble(), 9);
        Assert.Equal(3, s.GetFor("efficiency", "soul_well", Num.One, includeUntargeted: false).ToDouble(), 9);
        Assert.Equal(11.5, s.GetFor("efficiency", "mana_well", Num.One).ToDouble(), 9);
        Assert.Equal(10, s.Get("efficiency").ToDouble(), 9);                  // the plain read ignores targets
    }

    [Fact]
    public void Set_replaces_the_base_and_per_necromancer_targets_are_read_separately()
    {
        var s = new StatSheet();
        s.SetBase("soul_harvest_rate", 0);
        s.Add(new Modifier("soul_harvest_rate", ModifierOp.Set, 2, "a"));
        s.Add(new Modifier("soul_harvest_rate", ModifierOp.Set, 1, "b"));
        s.Add(new Modifier("soul_harvest_rate", ModifierOp.Mul, 1.5, "c"));
        Assert.Equal(3, s.Get("soul_harvest_rate").ToDouble(), 9);           // highest set (2) x 1.5
        s.Add(new Modifier("mana_per_tick@per_necromancer_in_mana_well", ModifierOp.Add, 4, "d"));
        Assert.Equal(4, s.PerNecromancer("mana_per_tick", "mana_well"), 9);
        Assert.Equal(0, s.PerNecromancer("mana_per_tick", "infuser"), 9);
        Assert.Equal(1, s.GetFor("mana_per_tick", "mana_well", Num.One).ToDouble(), 9);   // not mixed into GetFor
    }

    [Fact]
    public void Unit_families_come_from_tags()
    {
        _ = World.SampleContent;
        var w = new World();                                                   // sets StatTargets.UnitTags
        Assert.True(StatTargets.Matches("skeleton_units", "skeleton_golem"));
        Assert.False(StatTargets.Matches("skeleton_units", "wisp"));
        Assert.True(StatTargets.Matches("wisp_units", "fire_wisp"));
        Assert.True(StatTargets.Matches("all_units", "fire_wisp"));
        Assert.True(StatTargets.Matches("bones_and_skeleton_units", "bones"));
        Assert.True(StatTargets.Matches("workshops_and_benches", StatTargets.ResearchBench));
        Assert.NotNull(w);
    }
}
