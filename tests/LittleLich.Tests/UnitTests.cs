using System;
using System.Linq;
using System.Text.Json.Nodes;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Core.Saves;
using LittleLich.Crypt.Buildings;
using LittleLich.Crypt.Units;
using LittleLich.World;
using Xunit;

namespace LittleLich.Tests;

public class UnitBuilderTests
{
    internal static World Fresh(bool skeletonsResearched = true)
    {
        var w = new World();
        foreach (var s in w.Saveables) s.Load(new SaveData());
        if (skeletonsResearched)
        {
            w.Research.DebugComplete("summoning");
            w.Research.DebugComplete("animate_bones");
        }
        return w;
    }

    [Fact]
    public void Build_a_skeleton_with_a_sword_let_the_lich_fill_six_squads_and_take_the_graveyard()
    {
        var w = Fresh();
        var (r, type) = w.Units.Build("skeleton", "copper_sword");
        Assert.Equal(BuildResult.Ok, r);
        Assert.Equal("skeleton_copper_sword", type!.Id);

        w.OpenAll();
        w.Buildings.OfType<MagicalStorage>().Single().DebugSetLevel(20);   // room for 10,500 mana and 1,050 souls
        // Nothing else makes or spends stock here, so the drop is exactly the summons.
        foreach (var b in w.Buildings) w.Ticks.Unregister(b);
        w.Ticks.Unregister(w.Income);
        w.Inventory.Add("copper_sword", 40);
        w.Inventory.Add("bones", 400);
        w.Storage.Add("mana", 5_000);
        w.Storage.Add("souls", 400);
        foreach (var i in WorldTests.FrontFirst.Take(6)) Assert.True(w.Units.SetSlot(i, type.Id));

        w.Ticks.RunTicks(60);                                     // 120 s: one figure every 4 s = 30 figures
        Assert.Equal(30, w.WorldMap.ArmyFigures);
        Assert.Equal(10, w.Inventory.Get("copper_sword"));        // exactly 30 swords used
        Assert.Equal(500, w.Storage.Get("mana"));                 // 150 mana, 10 souls, 10 bones and a sword each
        Assert.Equal(100, w.Storage.Get("souls"));
        Assert.Equal(100, w.Inventory.Get("bones"));

        Assert.Equal(SendResult.Ok, w.WorldMap.Send("graveyard"));
        for (var i = 0; i < 300 && w.WorldMap.Battle is not null; i++) w.WorldMap.Advance(1);
        var result = w.WorldMap.LastResult!;
        Assert.Equal(BattleOutcome.Victory, result.Outcome);
        Assert.Equal(result.Survivors[type.Id], w.WorldMap.ArmyFigures);
        Assert.Equal(AreaState.Cleared, w.WorldMap.State("graveyard"));
    }

    [Fact]
    public void Bases_need_their_research_and_gear_must_be_unit_gear()
    {
        var w = Fresh(skeletonsResearched: false);
        Assert.Equal(BuildResult.BaseLocked, w.Units.Build("skeleton").Result);
        w.Research.DebugComplete("animate_bones");
        Assert.Equal(BuildResult.Ok, w.Units.Build("skeleton").Result);
        Assert.Equal(BuildResult.NotAPlayerUnit, w.Units.Build("rival_skeleton").Result);
        Assert.Equal(BuildResult.UnknownBase, w.Units.Build("dragon").Result);
        Assert.Equal(BuildResult.BadWeapon, w.Units.Build("skeleton", "bone_staff").Result);    // a lich staff
        Assert.Equal(BuildResult.BadWeapon, w.Units.Build("skeleton", "iron_armour").Result);   // not a weapon
        Assert.Equal(BuildResult.BadArmour, w.Units.Build("skeleton", null, "copper_sword").Result);
        Assert.Equal(BuildResult.BadItem, w.Units.Build("skeleton", null, null, new[] { "grave_hood" }).Result);
        Assert.Equal(BuildResult.BadItem, w.Units.Build("skeleton", null, null, new[] { "mana" }).Result);   // a resource
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
    public void Each_figure_costs_the_body_plus_one_of_each_piece_of_gear_and_item()
    {
        var w = Fresh();
        var t = w.Units.Build("skeleton", "bone_spear", "iron_armour", new[] { "rot_bomb", "rot_bomb" }).Type!;
        Assert.Equal(new[] { "mana:150", "souls:10", "bones:10", "bone_spear:1", "iron_armour:1", "rot_bomb:2" },
            w.Units.CostPerUnit(t.Id).Select(c => $"{c.Id}:{c.Count}"));
        Assert.Equal(new[] { "mana:150", "souls:10", "bones:10" }, w.Units.CostPerUnit("skeleton").Select(c => $"{c.Id}:{c.Count}"));   // a base unit
    }

    [Fact]
    public void Gear_changes_battle_stats()
    {
        var w = Fresh();
        var bare = w.Units.BattleUnit("skeleton");
        var armed = w.Units.BattleUnit(w.Units.Build("skeleton", "copper_sword", "iron_armour").Type!.Id);
        Assert.Equal(100, bare.Hp);                                                      // old game data
        Assert.Equal(100 + 6 * UnitBuilder.HpPerDefence, armed.Hp);
        Assert.Equal(10, bare.Actions.Single(a => a.Id == "skeleton_attack").Value);
        Assert.Equal(14, armed.Actions.Single(a => a.Id == "skeleton_attack").Value);  // + sword damage 4
    }

    [Fact]
    public void A_type_cannot_be_deleted_while_a_slot_holds_it()
    {
        var w = Fresh();
        var t = w.Units.Build("skeleton", "copper_sword").Type!;
        Assert.True(w.Units.SetSlot(4, t.Id));
        Assert.False(w.Units.Remove(t.Id));
        w.Units.ClearSlot(4);
        Assert.True(w.Units.Remove(t.Id));
        Assert.Empty(w.Units.GetUnlockedUnitIds(custom: true));
    }
}

public class ArmyFormationTests
{
    [Fact]
    public void Nine_slots_indexed_row_times_three_plus_column_with_front_on_the_right()
    {
        Assert.Equal(9, ArmyFormation.SlotCount);
        Assert.Equal(new[] { "back", "middle", "front", "back", "middle", "front", "back", "middle", "front" },
            Enumerable.Range(0, 9).Select(ArmyFormation.SectionOf));
        Assert.Equal(7, ArmyFormation.Index(2, 1));
        Assert.Equal((2, 1), (ArmyFormation.Row(7), ArmyFormation.Column(7)));
    }

    [Fact]
    public void Only_unlocked_units_can_be_placed_and_one_unit_can_fill_several_slots()
    {
        var w = UnitBuilderTests.Fresh(skeletonsResearched: false);
        w.Research.DebugComplete("summoning");                          // wisps
        var u = w.Units;
        Assert.False(u.SetSlot(0, "skeleton"));                         // locked
        Assert.False(u.SetSlot(0, "rival_skeleton"));                   // an enemy
        Assert.False(u.SetSlot(0, "ghost"));
        Assert.False(u.SetSlot(9, "wisp"));
        Assert.False(u.SetSlot(-1, "wisp"));
        Assert.True(u.SetSlot(0, "wisp"));
        Assert.True(u.SetSlot(5, "wisp"));
        Assert.Equal(2, u.CountInFormation("wisp"));
        Assert.Equal("wisp", u.GetSlot(5));
        Assert.Null(u.GetSlot(1));
        Assert.Null(u.GetSlot(42));
    }

    [Fact]
    public void Swapping_moves_squads_and_never_loses_a_unit()
    {
        var w = UnitBuilderTests.Fresh();
        var u = w.Units;
        var changes = 0;
        u.FormationChanged += () => changes++;
        u.SetSlot(0, "skeleton");
        u.SetSlot(2, "wisp");
        u.DebugFillSquads();
        u.SyncFigures(0, 3);
        u.SwapSlots(0, 2);
        Assert.Equal(("wisp", 5L), (u.GetSlot(0), u.Figures(0)));
        Assert.Equal(("skeleton", 3L), (u.GetSlot(2), u.Figures(2)));
        u.SwapSlots(2, 8);                                              // onto an empty slot: a move
        Assert.Null(u.GetSlot(2));
        Assert.Equal(("skeleton", 3L), (u.GetSlot(8), u.Figures(8)));
        u.SwapSlots(1, 4);                                              // two empty slots: nothing happens
        u.SwapSlots(3, 3);
        Assert.Equal(new[] { "skeleton", "wisp" }, u.Army.Slots.OfType<string>().OrderBy(x => x));
        Assert.Equal(4, changes);

        u.ClearSlot(8);
        Assert.Null(u.GetSlot(8));
        Assert.Equal(0, u.Figures(8));
    }

    [Fact]
    public void A_newly_placed_unit_starts_with_no_figures_and_the_same_unit_keeps_its_squad()
    {
        var w = UnitBuilderTests.Fresh();
        var u = w.Units;
        u.SetSlot(4, "skeleton");
        u.DebugFillSquads();
        Assert.True(u.SetSlot(4, "skeleton"));
        Assert.Equal(5, u.Figures(4));
        Assert.True(u.SetSlot(4, "wisp"));
        Assert.Equal(0, u.Figures(4));
    }

    [Fact]
    public void Base_and_custom_lists_show_only_unlocked_units_and_say_when_they_change()
    {
        var w = UnitBuilderTests.Fresh(skeletonsResearched: false);
        var u = w.Units;
        var changed = 0;
        u.UnlockedUnitsChanged += () => changed++;
        Assert.Empty(u.GetUnlockedUnitIds(custom: false));
        w.Research.DebugComplete("summoning");
        Assert.Equal(new[] { "wisp" }, u.GetUnlockedUnitIds(custom: false));
        w.Research.DebugComplete("animate_bones");
        Assert.Equal(new[] { "wisp", "skeleton" }, u.GetUnlockedUnitIds(custom: false));
        Assert.True(changed >= 2);
        Assert.Empty(u.GetUnlockedUnitIds(custom: true));
        var before = changed;
        u.Build("skeleton");                                            // a gear-less build is the base unit, not custom
        u.Build("skeleton", "copper_sword");
        Assert.Equal(new[] { "skeleton_copper_sword" }, u.GetUnlockedUnitIds(custom: true));
        Assert.True(changed > before);
    }

    [Fact]
    public void Details_show_stats_actions_and_supply()
    {
        var w = UnitBuilderTests.Fresh();
        var u = w.Units;
        w.Storage.Add("mana", 1_000);
        w.Storage.Add("souls", 100);
        w.Inventory.Add("bones", 100);
        u.SetSlot(2, "skeleton");
        u.SetSlot(5, "skeleton");

        var d = u.GetUnitDetails("skeleton");
        Assert.Equal(("Skeleton", "Melee", false, (string?)null), (d.Name, d.Type, d.IsCustom, d.BuiltFrom));
        Assert.Equal((100.0, 10.0, 2.0), (d.Health, d.Damage, d.AttackSpeed));
        Assert.Equal("Skeleton attack", d.Actions.Single().Name);
        Assert.Equal(2, d.Actions.Single().CooldownSeconds);
        Assert.Equal("10 damage to the front line", d.Actions.Single().Description);
        Assert.Equal("150 mana, 10 souls, 10 bones", d.SummonCostText);
        Assert.Equal(6, d.Stock);                                       // 1,000 mana pays for six
        Assert.Equal(2, u.CountInFormation("skeleton"));

        var t = u.Build("skeleton", "copper_sword", "iron_armour").Type!;
        var c = u.GetUnitDetails(t.Id);
        Assert.True(c.IsCustom);
        Assert.Equal("Skeleton + copper sword + iron armour", c.BuiltFrom);
        Assert.Equal(14, c.Damage);
        Assert.Equal("150 mana, 10 souls, 10 bones, copper sword, iron armour", c.SummonCostText);
        Assert.Equal(0, c.Stock);                                       // no swords

        Assert.Equal("Next hit taken does 10% less", UnitBuilder.Describe(new BattleUnitAction("b", "Brace", 10, "reduce_next_incoming", 0.1, "self", "melee")));
        Assert.Equal("Heals the most wounded ally for 8", UnitBuilder.Describe(new BattleUnitAction("h", "Mend", 2, "heal", 8, "ally_lowest", "ranged")));
    }
}

public class LichSummonerTests
{
    private static World Stocked()
    {
        var w = UnitBuilderTests.Fresh();
        w.OpenAll();
        w.Buildings.OfType<MagicalStorage>().Single().DebugSetLevel(20);
        foreach (var b in w.Buildings) w.Ticks.Unregister(b);          // only the lich touches the stock
        w.Ticks.Unregister(w.Income);
        w.Storage.Add("mana", 10_000);
        w.Storage.Add("souls", 1_000);
        w.Inventory.Add("bones", 1_000);
        return w;
    }

    [Fact]
    public void One_figure_every_four_seconds_at_base_speed()
    {
        var w = Stocked();
        w.Units.SetSlot(2, "skeleton");
        w.Ticks.RunTicks(1);                                            // 2 s
        Assert.Equal(0, w.Units.Figures(2));
        Assert.Equal(0.5, w.Summoner.Fraction, 9);
        w.Ticks.RunTicks(1);                                            // 4 s
        Assert.Equal(1, w.Units.Figures(2));
        w.Ticks.RunTicks(20);
        Assert.Equal(ArmyFormation.SquadSize, w.Units.Figures(2));      // never above five
        Assert.Equal(1, w.Summoner.Fraction, 9);                        // waits, full, for a gap
        w.Units.SyncFigures(2, 4);
        w.Ticks.RunTicks(1);
        Assert.Equal(5, w.Units.Figures(2));                            // the waiting summon lands on the next tick
    }

    [Fact]
    public void Research_and_skills_speed_it_up_through_the_lich_stat()
    {
        var w = Stocked();
        w.Units.SetSlot(2, "skeleton");
        w.Lich.Stats.Add(new Modifier(LichSummoner.SpeedStat, ModifierOp.Mul, (Num)2, "test"));
        Assert.Equal(2, w.Summoner.SecondsPerSummon, 9);
        w.Ticks.RunTicks(2);
        Assert.Equal(2, w.Units.Figures(2));
    }

    [Fact]
    public void The_slot_missing_most_goes_first_and_ties_go_front_then_mid_then_back()
    {
        var army = new ArmyFormation();
        army.Slots[0] = "a";   // back, top
        army.Slots[1] = "a";   // mid, top
        army.Slots[5] = "a";   // front, middle row
        army.Slots[8] = "a";   // front, bottom row
        army.Figures[0] = 1;
        army.Figures[1] = 2;
        army.Figures[5] = 1;
        army.Figures[8] = 1;
        Assert.Equal(5, LichSummoner.PickSlot(army, _ => true));        // tie at 4 missing: front, then top row
        army.Figures[5] = 2;
        Assert.Equal(8, LichSummoner.PickSlot(army, _ => true));
        army.Figures[8] = 2;
        Assert.Equal(0, LichSummoner.PickSlot(army, _ => true));        // back is the only slot missing 4
        army.Figures[0] = 2;
        Assert.Equal(5, LichSummoner.PickSlot(army, _ => true));        // all missing 3: front first
        Assert.Equal(-1, LichSummoner.PickSlot(army, _ => false));       // nothing the stock can pay for
        army.Slots[3] = "b";                                            // missing 5 but nothing to pay with
        Assert.Equal(5, LichSummoner.PickSlot(army, id => id != "b"));
        Assert.Equal(3, LichSummoner.PickSlot(army, _ => true));
    }

    [Fact]
    public void A_unit_with_no_stock_is_skipped_and_the_stock_drops_by_exactly_what_was_used()
    {
        var w = Stocked();
        var sword = w.Units.Build("skeleton", "copper_sword").Type!;
        w.Inventory.Add("copper_sword", 2);
        w.Units.SetSlot(2, sword.Id);                                   // front, but only two swords
        w.Units.SetSlot(1, "skeleton");                                 // mid
        w.Ticks.RunTicks(20);                                           // 40 s: ten summons
        Assert.Equal(2, w.Units.Figures(2));
        Assert.Equal(5, w.Units.Figures(1));
        Assert.Equal(0, w.Inventory.Get("copper_sword"));
        Assert.Equal(10_000 - 7 * 150, w.Storage.Get("mana"));
        Assert.Equal(1_000 - 7 * 10, w.Storage.Get("souls"));
        Assert.Equal(1_000 - 7 * 10, w.Inventory.Get("bones"));

        w.Inventory.Add("copper_sword", 1);                             // the stock arrives: the waiting summon goes
        w.Ticks.RunTicks(1);
        Assert.Equal(3, w.Units.Figures(2));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(60)]
    public void Batched_equals_single_ticks(int n)
    {
        World Make()
        {
            var w = Stocked();
            var sword = w.Units.Build("skeleton", "copper_sword").Type!;
            w.Inventory.Add("copper_sword", 4);
            w.Units.SetSlot(2, sword.Id);
            w.Units.SetSlot(4, "skeleton");
            w.Units.SetSlot(6, "wisp");
            return w;
        }
        var live = Make();
        for (var i = 0; i < n; i++) live.Ticks.RunTicks(1);
        var batched = Make();
        batched.Ticks.RunTicks(n);
        Assert.Equal(live.Units.Army.Save().Node.ToJsonString(), batched.Units.Army.Save().Node.ToJsonString());
        Assert.Equal(live.Storage.Save().Node.ToJsonString(), batched.Storage.Save().Node.ToJsonString());
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
    public void Types_the_formation_its_squads_and_the_summon_progress_survive_reload()
    {
        var (s1, a) = Session();
        s1.NewGame(1);
        a.Research.DebugComplete("animate_bones");
        a.Research.DebugComplete("skeleton_warrior");
        var sk = a.Units.Build("skeleton", "copper_sword").Type!;
        var wa = a.Units.Build("skeleton_warrior", null, "iron_armour").Type!;
        a.Units.SetSlot(2, sk.Id);
        a.Units.SetSlot(5, sk.Id);
        a.Units.SetSlot(3, wa.Id);
        a.Units.SetSlot(7, "skeleton");
        a.Units.DebugFillSquads();
        a.Units.SyncFigures(5, 2);
        a.Units.Army.ProgressMicro = 1_500_000;
        s1.Save();

        var (s2, b) = Session();
        var loaded = 0;
        b.Units.FormationChanged += () => loaded++;
        s2.LoadSlot(1);
        Assert.Equal(a.Units.Save().Node.ToJsonString(), b.Units.Save().Node.ToJsonString());
        Assert.Equal(a.Units.Army.Save().Node.ToJsonString(), b.Units.Army.Save().Node.ToJsonString());
        Assert.Equal(new[] { null, null, sk.Id, wa.Id, null, sk.Id, null, "skeleton", null }, b.Units.Army.Slots);
        Assert.Equal(2, b.Units.Figures(5));
        Assert.Equal(1_500_000, b.Units.Army.ProgressMicro);
        Assert.True(loaded > 0);                                        // pages redraw after the load
        Assert.Equal(a.Units.BattleUnit(wa.Id).Hp, b.Units.BattleUnit(wa.Id).Hp);
    }

    [Fact]
    public void A_save_from_before_adjustment_03_keeps_its_types_and_drops_the_roster_and_old_formations()
    {
        var w = new World();
        w.Units.Load(new SaveData(JsonNode.Parse("""
            { "types": [ { "id": "skeleton_copper_sword", "name": "Skeleton (Copper Sword)", "base": "skeleton", "weapon": "copper_sword", "items": [] } ],
              "roster": { "skeleton_copper_sword": 40 },
              "formations": [ { "id": "formation_1", "name": "Main", "slots": [ { "unit": "skeleton_copper_sword", "count": 30 } ] } ] }
            """)!.AsObject()));
        w.Units.Army.Load(new SaveData());
        w.WorldMap.Load(new SaveData(JsonNode.Parse("""
            { "cleared": [], "garrison": { "skeleton": 21 }, "battle": { "area": "woods", "army": { "front": { "skeleton": 5 } } } }
            """)!.AsObject()));
        Assert.NotNull(w.Units.Type("skeleton_copper_sword"));
        Assert.All(w.Units.Army.Slots, Assert.Null);
        Assert.Null(w.WorldMap.Battle);                                 // nothing standing to restart with
    }

    [Fact]
    public void Saved_slots_with_units_that_no_longer_exist_are_dropped()
    {
        var w = new World();
        w.Units.Load(new SaveData());
        w.Units.Army.Load(new SaveData(JsonNode.Parse("""
            { "slots": [ { "unit": "dragon", "figures": 5 }, { "unit": "skeleton", "figures": 9 } ], "progress": 7 }
            """)!.AsObject()));
        Assert.Null(w.Units.GetSlot(0));
        Assert.Equal(("skeleton", 5L), (w.Units.GetSlot(1), w.Units.Figures(1)));   // clamped to a squad
    }
}
