using System;
using System.Linq;
using System.Text.Json.Nodes;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Saves;
using LittleLich.Crypt.Buildings;
using LittleLich.Crypt.Units;
using LittleLich.World;
using Xunit;

namespace LittleLich.Tests;

public class UnitBuilderTests
{
    private static World Fresh(bool skeletonsResearched = true)
    {
        var w = new World();
        foreach (var s in w.Saveables) s.Load(new SaveData());
        if (skeletonsResearched)
        {
            w.Research.DebugComplete("nec1_grave_lore");
            w.Research.DebugComplete("nec1_raise_skeletons");
        }
        return w;
    }

    [Fact]
    public void Build_a_skeleton_with_a_sword_summon_100_put_them_in_front_and_take_the_graveyard()
    {
        var w = Fresh();
        var (r, type) = w.Units.Build("skeleton", "copper_sword");
        Assert.Equal(BuildResult.Ok, r);
        Assert.Equal("skeleton_copper_sword", type!.Id);

        w.Inventory.Add("copper_sword", 130);
        w.Storage.Add("mana", 1_000);
        Assert.True(w.Units.TrySummon(type.Id, 100));
        Assert.Equal(30, w.Inventory.Get("copper_sword"));        // exactly 100 swords used
        Assert.Equal(500, w.Storage.Get("mana"));                 // 5 mana each
        Assert.Equal(100, w.Units.Count(type.Id));

        var f = w.Units.CreateFormation("Graveyard push");
        Assert.True(w.Units.SetSlot(f.Id, 0, type.Id, 100));     // slot 1: front row
        Assert.Equal(Sections.Front, f.Slots[0].Section);
        Assert.Equal(SendResult.Ok, w.WorldMap.Send("graveyard", f));
        Assert.Equal(0, w.Units.Count(type.Id));
        for (var i = 0; i < 300 && w.WorldMap.Battle is not null; i++) w.WorldMap.Advance(1);

        var result = w.WorldMap.LastResult!;
        Assert.Equal(BattleOutcome.Victory, result.Outcome);
        Assert.Equal(result.Survivors[type.Id], w.Units.Count(type.Id));
        Assert.Equal(AreaState.Cleared, w.WorldMap.State("graveyard"));
    }

    [Fact]
    public void Bases_need_their_research_and_gear_must_be_unit_gear()
    {
        var w = Fresh(skeletonsResearched: false);
        Assert.Equal(BuildResult.BaseLocked, w.Units.Build("skeleton").Result);
        w.Research.DebugComplete("nec1_raise_skeletons");
        Assert.Equal(BuildResult.Ok, w.Units.Build("skeleton").Result);
        Assert.Equal(BuildResult.NotAPlayerUnit, w.Units.Build("rival_skeleton").Result);
        Assert.Equal(BuildResult.UnknownBase, w.Units.Build("dragon").Result);
        Assert.Equal(BuildResult.BadWeapon, w.Units.Build("skeleton", "bone_staff").Result);    // a lich staff
        Assert.Equal(BuildResult.BadWeapon, w.Units.Build("skeleton", "iron_armour").Result);   // not a weapon
        Assert.Equal(BuildResult.BadArmour, w.Units.Build("skeleton", null, "copper_sword").Result);
        Assert.Equal(BuildResult.BadItem, w.Units.Build("skeleton", null, null, new[] { "grave_hood" }).Result);
        Assert.Equal(BuildResult.BadItem, w.Units.Build("skeleton", null, null, new[] { "coal" }).Result);   // a resource
    }

    [Fact]
    public void One_loadout_per_type_and_building_again_returns_the_same_type()
    {
        var w = Fresh();
        var a = w.Units.Build("skeleton", "copper_sword").Type!;
        var b = w.Units.Build("skeleton", "copper_sword").Type!;
        var c = w.Units.Build("skeleton", "bone_spear", "iron_armour", new[] { "rot_bomb" }).Type!;
        Assert.Same(a, b);
        Assert.Equal("skeleton_bone_spear_iron_armour_rot_bomb", c.Id);
        Assert.Equal("Skeleton (Bone Spear, Iron Armour)", c.Name);
        Assert.Equal(2, w.Units.Types.Count);
    }

    [Fact]
    public void Each_summon_costs_mana_plus_one_of_each_piece_of_gear_and_item()
    {
        var w = Fresh();
        var t = w.Units.Build("skeleton", "bone_spear", "iron_armour", new[] { "rot_bomb", "rot_bomb" }).Type!;
        Assert.Equal(new[] { "mana:5", "bone_spear:1", "iron_armour:1", "rot_bomb:2" },
            w.Units.CostPerUnit(t.Id).Select(c => $"{c.Id}:{c.Count}"));
    }

    [Fact]
    public void Summoning_is_all_or_nothing()
    {
        var w = Fresh();
        var t = w.Units.Build("skeleton", "copper_sword").Type!;
        w.Inventory.Add("copper_sword", 10);
        w.Storage.Add("mana", 1_000);
        Assert.Equal((10L, (string?)"copper_sword"), w.Units.Affordable(t.Id));
        Assert.False(w.Units.TrySummon(t.Id, 11));
        Assert.Equal(10, w.Inventory.Get("copper_sword"));
        Assert.Equal(1_000, w.Storage.Get("mana"));
        Assert.Equal(0, w.Units.Count(t.Id));
        Assert.True(w.Units.TrySummon(t.Id, 10));
        Assert.False(w.Units.TrySummon(t.Id, 0));
        Assert.False(w.Units.TrySummon("nope", 1));
    }

    [Fact]
    public void Gear_changes_battle_stats()
    {
        var w = Fresh();
        var bare = w.Units.BattleUnit(w.Units.Build("skeleton").Type!.Id);
        var armed = w.Units.BattleUnit(w.Units.Build("skeleton", "copper_sword", "iron_armour").Type!.Id);
        Assert.Equal(20, bare.Hp);
        Assert.Equal(20 + 6 * UnitBuilder.HpPerDefence, armed.Hp);
        Assert.Equal(3, bare.Actions.Single(a => a.Id == "slash").Value);
        Assert.Equal(7, armed.Actions.Single(a => a.Id == "slash").Value);          // + sword damage 4
        Assert.Equal(0.1, armed.Actions.Single(a => a.Id == "brace").Value);       // brace unchanged
    }

    [Fact]
    public void Formations_have_eight_slots_2_front_3_middle_3_back()
    {
        var w = Fresh();
        var t = w.Units.Build("skeleton").Type!;
        var f = w.Units.CreateFormation("");
        Assert.Equal("Formation 1", f.Name);
        Assert.Equal(new[] { "front", "front", "middle", "middle", "middle", "back", "back", "back" }, f.Slots.Select(s => s.Section));
        Assert.False(w.Units.SetSlot(f.Id, 8, t.Id, 1));
        Assert.False(w.Units.SetSlot(f.Id, 0, "ghost", 1));
        Assert.True(w.Units.SetSlot(f.Id, 2, t.Id, 10));
        Assert.True(w.Units.SetSlot(f.Id, 3, t.Id, 5));
        Assert.True(w.Units.SetSlot(f.Id, 7, t.Id, 1));
        var plan = w.WorldMap.PlanFor(f);
        Assert.Equal(15, plan.Sections[Sections.Middle][t.Id]);   // two middle slots of one type merge
        Assert.Equal(1, plan.Sections[Sections.Back][t.Id]);
        Assert.Equal(SendResult.NotEnoughUnits, w.WorldMap.Send("woods", f));
        Assert.True(w.Units.SetSlot(f.Id, 2, null, 0));
        Assert.Null(f.Slots[2].UnitType);
    }

    [Fact]
    public void A_type_cannot_be_deleted_while_in_use()
    {
        var w = Fresh();
        var t = w.Units.Build("skeleton").Type!;
        var f = w.Units.CreateFormation("F");
        w.Units.SetSlot(f.Id, 0, t.Id, 5);
        Assert.False(w.Units.Remove(t.Id));                         // in a formation
        w.Units.SetSlot(f.Id, 0, null, 0);
        w.WorldMap.AddUnits(t.Id, 5);
        Assert.False(w.Units.Remove(t.Id));                         // has units
        w.WorldMap.Send("woods", new ArmyPlan().Set(Sections.Front, t.Id, 5));
        Assert.False(w.Units.Remove(t.Id));                         // units out fighting
        w.WorldMap.Retreat();
        w.Units.TakeFromRoster(t.Id, w.Units.Count(t.Id));
        Assert.True(w.Units.Remove(t.Id));
    }
}

public class CircleUnitTests
{
    private static World Make(long swords, long mana)
    {
        var w = new World();
        foreach (var s in w.Saveables) s.Load(new SaveData());
        w.Research.DebugComplete("nec1_raise_skeletons");
        var t = w.Units.Build("skeleton", "copper_sword").Type!;
        w.Inventory.Add("copper_sword", swords);
        w.Storage.Add("mana", mana);
        w.Lich.DebugAddNecromancers(3);
        var circle = w.Buildings.OfType<SummoningCircle>().Single();
        circle.AddNecromancer(3);                                   // 0.2 + 3 x 0.2 = 0.8 work/tick; skeleton work 1
        circle.SetUnitTarget(t.Id, 20);
        // Keep the Mana Well from refilling mana so gear and mana limits are what we set.
        w.Ticks.Unregister(w.Buildings.OfType<ManaWell>().Single());
        return w;
    }

    [Fact]
    public void The_circle_tops_a_stack_up_to_its_target_and_stops()
    {
        var w = Make(100, 1_000);
        var circle = w.Buildings.OfType<SummoningCircle>().Single();
        w.Ticks.RunTicks(10);
        Assert.Equal(8, w.Units.Count("skeleton_copper_sword"));   // 0.8 per tick
        Assert.Equal(92, w.Inventory.Get("copper_sword"));
        w.Ticks.RunTicks(100);
        Assert.Equal(20, w.Units.Count("skeleton_copper_sword"));
        Assert.Equal(BuildingStatus.PoolFull, circle.Status);
        Assert.Equal("target", circle.StatusDetail);
        Assert.Equal(80, w.Inventory.Get("copper_sword"));
        Assert.Equal(900, w.Storage.Get("mana"));
    }

    [Fact]
    public void The_circle_pauses_when_gear_runs_out()
    {
        var w = Make(6, 1_000);
        var circle = w.Buildings.OfType<SummoningCircle>().Single();
        w.Ticks.RunTicks(50);
        Assert.Equal(6, w.Units.Count("skeleton_copper_sword"));
        Assert.Equal(BuildingStatus.MissingInput, circle.Status);
        Assert.Equal("copper_sword", circle.StatusDetail);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(40)]
    public void Batched_equals_single_ticks(int n)
    {
        var live = Make(15, 1_000);
        for (var i = 0; i < n; i++) live.Ticks.RunTicks(1);
        var batched = Make(15, 1_000);
        batched.Ticks.RunTicks(n);
        Assert.Equal(live.Units.Count("skeleton_copper_sword"), batched.Units.Count("skeleton_copper_sword"));
        Assert.Equal(live.Buildings.OfType<SummoningCircle>().Single().Save().Node.ToJsonString(),
                     batched.Buildings.OfType<SummoningCircle>().Single().Save().Node.ToJsonString());
    }
}

public class UnitSaveTests : IDisposable
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
    public void Types_roster_formations_and_circle_target_survive_reload()
    {
        var (s1, a) = Session();
        s1.NewGame(1);
        a.Research.DebugComplete("nec1_raise_skeletons");
        a.Research.DebugComplete("nec1_raise_zombies");
        var sk = a.Units.Build("skeleton", "copper_sword").Type!;
        var zo = a.Units.Build("zombie", null, "iron_armour").Type!;
        a.WorldMap.AddUnits(sk.Id, 40);
        a.WorldMap.AddUnits(zo.Id, 7);
        var f = a.Units.CreateFormation("Main");
        a.Units.SetSlot(f.Id, 0, sk.Id, 30);
        a.Units.SetSlot(f.Id, 3, zo.Id, 7);
        a.Buildings.OfType<SummoningCircle>().Single().SetUnitTarget(sk.Id, 60);
        s1.Save();

        var (s2, b) = Session();
        s2.LoadSlot(1);
        Assert.Equal(a.Units.Save().Node.ToJsonString(), b.Units.Save().Node.ToJsonString());
        Assert.Equal(40, b.Units.Count(sk.Id));
        Assert.Equal(zo.Id, b.Units.GetFormation(f.Id)!.Slots[3].UnitType);
        var circle = b.Buildings.OfType<SummoningCircle>().Single();
        Assert.Equal(sk.Id, circle.UnitType);
        Assert.Equal(60, circle.UnitTarget);
        Assert.Equal(a.Units.BattleUnit(zo.Id).Hp, b.Units.BattleUnit(zo.Id).Hp);
    }

    [Fact]
    public void A_phase_8_garrison_moves_into_the_roster()
    {
        var w = new World();
        w.Units.Load(new SaveData());
        w.WorldMap.Load(new SaveData(JsonNode.Parse("""
            { "cleared": [], "garrison": { "skeleton": 21, "zombie": 6, "dragon": 3 },
              "battle": { "area": "woods", "army": { "front": { "skeleton": 5 } } } }
            """)!.AsObject()));
        Assert.Equal(21, w.Units.Count("skeleton"));
        Assert.Equal(6, w.Units.Count("zombie"));
        Assert.Equal("Skeleton", w.Units.Type("skeleton")!.Name);
        Assert.Equal(5, w.WorldMap.Battle!.Survivors(BattleSide.Attacker)["skeleton"]);   // the battle restarts with its army
    }
}
