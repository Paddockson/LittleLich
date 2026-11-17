using System;
using System.IO;
using System.Linq;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Saves;
using LittleLich.Crypt.Buildings;
using LittleLich.Crypt.Research;
using LittleLich.Skills;
using Xunit;

namespace LittleLich.Tests;

public class ResearchTests
{
    private static World Fresh()
    {
        var w = new World();
        foreach (var s in w.Saveables) s.Load(new SaveData());   // new-game state
        return w;
    }

    /// <summary>Starts a node on bench 0 with 1 necromancer, paying with debug stock. Returns ticks until it completes.</summary>
    private static void Staff(World w, int bench = 0, long n = 1)
    {
        w.Lich.DebugAddNecromancers(n);
        Assert.Equal(n, w.Research.AddNecromancer(bench, n));
    }

    [Fact]
    public void Necromancy_1_has_about_ten_nodes_and_one_root()
    {
        var t = World.SampleContent.All<ResearchNodeDefinition>().Where(n => n.Tome == "necromancy_1").ToList();
        Assert.InRange(t.Count, 8, 14);
        Assert.Single(t, n => n.Requires.Count == 0);
        var w = Fresh();
        Assert.Equal(NodeState.Available, w.Research.State("nec1_grave_lore"));
        Assert.Equal(NodeState.Locked, w.Research.State("nec1_raise_skeletons"));
        Assert.Single(w.Research.Benches);
    }

    [Fact]
    public void Researching_a_node_spends_its_cost_and_completes_after_its_work()
    {
        var w = Fresh();
        Staff(w);
        Assert.Equal(StartResult.CannotAfford, w.Research.Start("nec1_grave_lore"));
        w.Storage.Add("mana", 50);
        Assert.Equal(StartResult.Ok, w.Research.Start("nec1_grave_lore"));
        Assert.Equal(30, w.Storage.Get("mana"));   // 20 spent up front
        Assert.Equal(NodeState.Researching, w.Research.State("nec1_grave_lore"));
        Assert.Equal(StartResult.NotAvailable, w.Research.Start("nec1_grave_lore"));

        var completed = new System.Collections.Generic.List<string>();
        var available = new System.Collections.Generic.List<string>();
        w.Research.NodeCompleted += completed.Add;
        w.Research.NodeAvailable += available.Add;
        for (var i = 0; i < 9; i++) w.Research.Tick(1);
        Assert.Empty(completed);
        Assert.Equal(0.9, w.Research.Progress(w.Research.Benches[0]), 9);
        w.Research.Tick(1);                                           // 10 ticks of 1 necromancer
        Assert.Equal(new[] { "nec1_grave_lore" }, completed);
        Assert.Equal(NodeState.Done, w.Research.State("nec1_grave_lore"));
        Assert.Null(w.Research.Benches[0].NodeId);
        Assert.Contains("nec1_raise_skeletons", available);
        Assert.Contains("nec1_second_bench", available);
    }

    [Fact]
    public void Completing_a_node_applies_its_effects()
    {
        var w = Fresh();
        Assert.Equal(10, w.Lich.Pool.Max);
        w.Research.DebugComplete("nec1_grave_lore");
        Assert.Equal(15, w.Lich.Pool.Max);                            // stat_mod
        Assert.Equal(new Num(20), w.Skill<NecromancySkill>().Xp);     // tome skill XP

        Assert.True(w.Unlocks.IsLocked(Unlocks.Unit, "skeleton"));
        w.Research.DebugComplete("nec1_raise_skeletons");
        Assert.False(w.Unlocks.IsLocked(Unlocks.Unit, "skeleton"));   // recorded for phase 9

        w.Research.DebugComplete("nec1_second_bench");
        Assert.Equal(2, w.Research.Benches.Count);                    // unlock_bench

        var well = w.Buildings.OfType<ManaWell>().Single();
        Assert.Equal(10, well.MaxLevel);
        w.Research.DebugComplete("nec1_deep_channels");
        Assert.Equal(20, well.MaxLevel);                              // building_level
    }

    [Fact]
    public void Unlocked_content_is_locked_until_researched()
    {
        var w = Fresh();
        var hunting = w.Skill<HuntingManager>();
        var boneyard = hunting.Activity("boneyard");
        w.Lich.DebugAddNecromancers(5);
        Assert.False(hunting.IsUnlocked(boneyard));
        Assert.Equal(0, hunting.AddNecromancer("boneyard", 1));
        Assert.Equal(0, w.Smithing.AddNecromancer("bone_spear", 1));
        var transmuter = w.Buildings.OfType<Transmuter>().Single();
        Assert.Throws<ArgumentException>(() => transmuter.Select("hide_to_bones"));

        w.Research.DebugComplete("nec1_ossuary");
        Assert.Equal(1, hunting.AddNecromancer("boneyard", 1));
        transmuter.Select("hide_to_bones");
        w.Smithing.DebugSetLevel(5);
        Assert.Equal(0, w.Smithing.AddNecromancer("bone_spear", 1));  // needs Bone Weapons
        w.Research.DebugComplete("nec1_bone_weapons");
        Assert.Equal(1, w.Smithing.AddNecromancer("bone_spear", 1));
    }

    [Fact]
    public void Deeper_nodes_need_the_tome_skill_level()
    {
        var w = Fresh();
        foreach (var id in new[] { "nec1_grave_lore", "nec1_raise_zombies", "nec1_deep_channels" }) w.Research.DebugComplete(id);
        var necro = w.Skill<NecromancySkill>();
        if (necro.Level < 5)
        {
            Assert.Equal(NodeState.Locked, w.Research.State("nec1_hunger_of_the_grave"));
            Assert.Equal("needs necromancy level 5", w.Research.WhyLocked("nec1_hunger_of_the_grave"));
        }
        necro.DebugSetLevel(5);
        Assert.Equal(NodeState.Available, w.Research.State("nec1_hunger_of_the_grave"));
        Assert.StartsWith("needs ", w.Research.WhyLocked("nec1_lord_of_the_grave"));
    }

    [Fact]
    public void Necromancers_speed_up_a_bench_up_to_its_slots()
    {
        var w = Fresh();
        w.Lich.DebugAddNecromancers(20);
        Assert.Equal(5, w.Research.AddNecromancer(0, 20));
        w.Storage.Add("mana", 20);
        w.Research.Start("nec1_grave_lore");
        Assert.Equal(5, w.Research.WorkPerTick(w.Research.Benches[0]));
        w.Research.Tick(2);
        Assert.Equal(NodeState.Done, w.Research.State("nec1_grave_lore"));
    }

    [Fact]
    public void An_unstaffed_bench_makes_no_progress()
    {
        var w = Fresh();
        w.Storage.Add("mana", 20);
        w.Research.Start("nec1_grave_lore");
        w.Research.Tick(1_000);
        Assert.Equal(NodeState.Researching, w.Research.State("nec1_grave_lore"));
        Assert.Null(w.Research.TicksLeft(w.Research.Benches[0]));
    }

    [Fact]
    public void Two_benches_research_two_nodes_at_once_and_cancel_refunds()
    {
        var w = Fresh();
        w.Research.DebugComplete("nec1_grave_lore");
        w.Research.DebugComplete("nec1_second_bench");
        w.Storage.Add("mana", 1_000);
        w.Storage.Add("raw_eel", 10);
        Assert.Equal(StartResult.Ok, w.Research.Start("nec1_raise_skeletons"));
        Assert.Equal(StartResult.Ok, w.Research.Start("nec1_raise_zombies"));
        Assert.Equal(StartResult.NoFreeBench, w.Research.Start("nec1_deep_channels"));
        Assert.Equal(1, w.Research.Benches[1].Index);
        Assert.Equal("nec1_raise_zombies", w.Research.Benches[1].NodeId);
        Assert.Equal(860, w.Storage.Get("mana"));
        Assert.True(w.Research.Cancel(1));
        Assert.Equal(940, w.Storage.Get("mana"));
        Assert.Equal(10, w.Storage.Get("raw_eel"));
        Assert.Equal(NodeState.Available, w.Research.State("nec1_raise_zombies"));
        Assert.False(w.Research.Cancel(1));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(25)]
    [InlineData(400)]
    public void Batched_ticks_complete_exactly_what_single_ticks_do(int n)
    {
        World Make()
        {
            var w = Fresh();
            w.Research.DebugComplete("nec1_grave_lore");
            w.Research.DebugComplete("nec1_second_bench");
            w.Lich.DebugAddNecromancers(5);
            w.Research.AddNecromancer(0, 3);
            w.Research.AddNecromancer(1, 2);
            w.Storage.Add("mana", 500);
            w.Storage.Add("raw_eel", 10);
            w.Research.Start("nec1_raise_skeletons");
            w.Research.Start("nec1_raise_zombies");
            return w;
        }
        var live = Make();
        for (var i = 0; i < n; i++) live.Research.Tick(1);
        var batched = Make();
        batched.Research.Tick(n);
        Assert.Equal(live.Research.Save().Node.ToJsonString(), batched.Research.Save().Node.ToJsonString());
        Assert.Equal(live.Skill<NecromancySkill>().Xp, batched.Skill<NecromancySkill>().Xp);
    }

    [Fact]
    public void Offline_time_completes_research()
    {
        var w = Fresh();
        Staff(w);
        w.Storage.Add("mana", 20);
        w.Research.Start("nec1_grave_lore");
        var recap = w.Ticks.RunOffline(19);         // 9 ticks: not yet
        Assert.Equal(NodeState.Researching, w.Research.State("nec1_grave_lore"));
        recap = w.Ticks.RunOffline(3_600);          // 1,800 ticks
        Assert.Equal(NodeState.Done, w.Research.State("nec1_grave_lore"));
        Assert.Equal(new Num(1), recap.Gained[("research", "nec1_grave_lore")]);
        Assert.Equal(15, w.Lich.Pool.Max);
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
    public void Researched_effects_and_progress_survive_save_and_reload()
    {
        var (s1, a) = Session();
        s1.NewGame(1);
        a.Research.DebugComplete("nec1_grave_lore");
        a.Research.DebugComplete("nec1_second_bench");
        a.Research.DebugComplete("nec1_deep_channels");
        a.Research.DebugComplete("nec1_raise_skeletons");
        a.Research.DebugComplete("nec1_ossuary");
        var well = a.Buildings.OfType<ManaWell>().Single();
        well.DebugSetLevel(15);                                   // only possible with Deep Channels
        a.Lich.DebugAddNecromancers(4);
        a.Research.AddNecromancer(1, 4);
        a.Storage.Add("mana", 500);
        a.Storage.Add("raw_eel", 10);
        a.Research.Start("nec1_raise_zombies", 1);
        a.Research.Tick(3);
        s1.Save();

        var (s2, b) = Session();
        s2.LoadSlot(1);
        Assert.Equal(a.Research.Save().Node.ToJsonString(), b.Research.Save().Node.ToJsonString());
        Assert.Equal(NodeState.Done, b.Research.State("nec1_ossuary"));
        Assert.Equal(2, b.Research.Benches.Count);
        Assert.Equal("nec1_raise_zombies", b.Research.Benches[1].NodeId);
        Assert.Equal(4, b.Research.Staffed(b.Research.Benches[1]));
        Assert.Equal(15, b.Buildings.OfType<ManaWell>().Single().Level);   // research loaded first: level not clamped to 10
        Assert.False(b.Unlocks.IsLocked(Unlocks.Activity, "boneyard"));
        Assert.True(b.Unlocks.IsLocked(Unlocks.Recipe, "bone_spear"));
        Assert.Equal(a.Lich.Pool.Max, b.Lich.Pool.Max);
        Assert.Single(b.Lich.Stats.Modifiers, m => m.Source == "research:nec1_grave_lore");

        a.Research.Tick(10);
        b.Research.Tick(10);
        Assert.Equal(NodeState.Done, b.Research.State("nec1_raise_zombies"));
        Assert.Equal(a.Research.Save().Node.ToJsonString(), b.Research.Save().Node.ToJsonString());
    }

    [Fact]
    public void Load_removes_research_modifiers_for_nodes_that_are_not_done()
    {
        var w = new World();
        w.Lich.Stats.Add(new Core.Lich.Modifier(Core.Lich.LichStats.MaxNecromancers, Core.Lich.ModifierOp.Add, 99, "research:nec1_grave_lore"));
        w.Research.Load(new SaveData());
        Assert.DoesNotContain(w.Lich.Stats.Modifiers, m => m.Source.StartsWith("research:"));
    }
}

public class ResearchContentTests
{
    private static ContentLoadException Fails(string json) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.CreateDefault().Load(
            ContentSource.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Data")).Append(new ContentSource("x.json", json))));

    [Fact]
    public void Cycles_are_rejected()
    {
        var e = Fails("""
            [ { "type": "tome", "id": "t2", "name": "T2" },
              { "type": "research_node", "tome": "t2", "id": "r", "name": "R" },
              { "type": "research_node", "tome": "t2", "id": "a", "name": "A", "requires": ["r", "c"] },
              { "type": "research_node", "tome": "t2", "id": "b", "name": "B", "requires": ["a"] },
              { "type": "research_node", "tome": "t2", "id": "c", "name": "C", "requires": ["b"] } ]
            """);
        Assert.Contains(e.Errors, x => x.Id == "t2" && x.Message.Contains("cycle"));
    }

    [Fact]
    public void Missing_ids_and_bad_effects_are_rejected()
    {
        var e = Fails("""
            [ { "type": "research_node", "tome": "necromancy_1", "id": "x", "name": "X", "requires": ["nope"],
                "cost": { "unobtainium": 5 },
                "effects": [ { "type": "unlock_recipe", "id": "gold_bar" }, { "type": "teleport" },
                             { "type": "building_level", "building": "castle", "levels": 1 } ] } ]
            """);
        Assert.Contains(e.Errors, x => x.Field == "effects[1].type");   // field errors first; references are checked once fields are valid
        e = Fails("""
            [ { "type": "research_node", "tome": "necromancy_1", "id": "x", "name": "X", "requires": ["nope"], "cost": { "unobtainium": 5 },
                "effects": [ { "type": "unlock_recipe", "id": "gold_bar" }, { "type": "building_level", "building": "castle", "levels": 1 } ] } ]
            """);
        Assert.Contains(e.Errors, x => x.Field == "requires");
        Assert.Contains(e.Errors, x => x.Field == "cost.unobtainium");
        Assert.Contains(e.Errors, x => x.Field == "effects[0].id");
        Assert.Contains(e.Errors, x => x.Field == "effects[1].building");
    }
}
