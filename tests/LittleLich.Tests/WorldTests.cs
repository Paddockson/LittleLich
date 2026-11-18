using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Saves;
using LittleLich.Crypt;
using LittleLich.World;
using Xunit;

namespace LittleLich.Tests;

public class BattleTests
{
    private static BattleUnit Unit(string id, double hp, params BattleUnitAction[] actions) => new(id, id, hp, actions.ToList());

    private static BattleUnitAction Attack(double cd, double value, string target = "front", string range = "melee", string id = "hit") =>
        new(id, id, cd, "damage", value, target, range);

    private static BattleUnitAction Brace(double cd, double value = 0.1) =>
        new("brace", "Brace", cd, "reduce_next_incoming", value, "self", "melee");

    private static (string, BattleUnit, long) G(string section, BattleUnit u, long n) => (section, u, n);

    private static BattleUnit FromContent(string id) => BattleUnit.From(World.SampleContent.Get<UnitDefinitionBase>(id));

    [Fact]
    public void Cooldowns_fire_at_the_right_times()
    {
        var a = Unit("a", 1000, Attack(2, 1), Attack(3, 1, id: "jab"));
        var dummy = Unit("dummy", 1_000_000);
        var b = new BattleController(new[] { G("front", a, 1) }, new[] { G("front", dummy, 1) });
        var log = new List<string>();
        b.ActionFired += x => log.Add($"{x.AtMs}:{x.Action.Id}");
        b.Advance(6.5);
        Assert.Equal(new[] { "2000:hit", "3000:jab", "4000:hit", "6000:hit", "6000:jab" }, log);
        Assert.Equal(6_500, b.NowMs);
    }

    [Fact]
    public void Brace_reduces_the_next_attack_by_ten_percent_once()
    {
        var hitter = Unit("hitter", 1000, Attack(1, 10));
        var wall = Unit("wall", 1000, Brace(0.5));          // braces at 0.5 s, 1.0 s ... before each hit lands
        var b = new BattleController(new[] { G("front", hitter, 1) }, new[] { G("front", wall, 1) });
        var hits = new List<(double Amount, bool Braced)>();
        b.ActionFired += x => { if (x.Action.Effect == "damage") hits.Add((x.Amount, x.Braced)); };
        b.Advance(1);
        Assert.Equal((9.0, true), hits[0]);
        Assert.Equal(991, b.Groups[1].Hp, 9);

        var lazyWall = Unit("lazy", 1000, Brace(10));       // braces once at 10 s
        var c = new BattleController(new[] { G("front", hitter, 1) }, new[] { G("front", lazyWall, 1) });
        var amounts = new List<double>();
        c.ActionFired += x => { if (x.Action.Effect == "damage") amounts.Add(x.Amount); };
        c.Advance(12);
        // Hits at 1..12 s; the brace at 10 s (fires before the 10 s hit: defender acts after attacker on ties, so it
        // protects the 11 s hit), and only that one.
        Assert.Equal(new[] { 10.0, 10, 10, 10, 10, 10, 10, 10, 10, 10, 9, 10 }, amounts);
    }

    [Fact]
    public void Damage_scales_with_units_standing_and_hp_is_pooled()
    {
        var a = Unit("a", 10, Attack(1, 2));
        var t = Unit("t", 10);
        var b = new BattleController(new[] { G("front", a, 5) }, new[] { G("front", t, 4) });
        b.Advance(1);                                         // 5 x 2 = 10 damage: one of four down
        Assert.Equal(30, b.Groups[1].Hp, 9);
        Assert.Equal(3, b.Groups[1].Alive);
        b.Advance(3);
        Assert.Equal(BattleOutcome.Victory, b.Outcome);
        Assert.Equal(new Dictionary<string, long> { ["a"] = 5 }, b.Survivors(BattleSide.Attacker));
    }

    [Fact]
    public void Melee_waits_behind_the_front_while_ranged_fires()
    {
        var gate = Unit("gate", 30);
        var guard = Unit("guard", 100, Attack(1, 5));         // melee, in the middle
        var archer = Unit("archer", 100, Attack(1, 1, range: "ranged"));
        var hero = Unit("hero", 1000, Attack(1, 10));
        var b = new BattleController(new[] { G("front", hero, 1) },
            new[] { G("front", gate, 1), G("middle", guard, 1), G("back", archer, 1) });
        var fired = new List<string>();
        b.ActionFired += x => fired.Add($"{x.AtMs}:{x.Source.Unit.Id}");
        b.Advance(3);                                         // gate falls at 3 s; the guard's overdue swing fires at once
        Assert.Equal(new[] { "1000:hero", "1000:archer", "2000:hero", "2000:archer", "3000:hero", "3000:guard", "3000:archer" }, fired);
        Assert.Equal("middle", b.FrontSection(BattleSide.Defender));
    }

    [Fact]
    public void Healing_restores_the_lowest_ally()
    {
        var a = Unit("a", 1000, Attack(1, 8));
        var tank = Unit("tank", 50);
        var healer = Unit("healer", 50, new BattleUnitAction("mend", "Mend", 2, "heal", 5, "ally_lowest", "ranged"));
        var b = new BattleController(new[] { G("front", a, 1) }, new[] { G("front", tank, 1), G("back", healer, 1) });
        b.Advance(2);                                         // -8 at 1 s, -8 at 2 s, +5 at 2 s
        Assert.Equal(39, b.Groups[1].Hp, 9);
    }

    [Fact]
    public void The_time_limit_is_a_defeat_and_retreat_ends_at_once()
    {
        var a = Unit("a", 10, Attack(1, 0.001));
        var t = Unit("t", 1_000_000);
        var b = new BattleController(new[] { G("front", a, 1) }, new[] { G("front", t, 1) }, timeLimitMs: 5_000);
        b.Advance(100);
        Assert.Equal(BattleOutcome.Defeat, b.Outcome);
        Assert.Equal(5_000, b.NowMs);

        var c = new BattleController(new[] { G("front", a, 3) }, new[] { G("front", t, 1) });
        c.Advance(1);
        c.Retreat();
        Assert.Equal(BattleOutcome.Retreat, c.Outcome);
        Assert.Equal(3, c.Survivors(BattleSide.Attacker)["a"]);
    }

    [Fact]
    public void Advancing_in_chunks_gives_the_same_battle()
    {
        string Run(Action<BattleController> drive)
        {
            var db = World.SampleContent;
            var area = db.Get<AreaDefinitionBase>("woods");
            var b = new BattleController(
                new[] { G("front", FromContent("skeleton"), 25), G("front", FromContent("skeleton_warrior"), 12) },
                area.Defenders.SelectMany(s => s.Value.Select(u => G(s.Key, FromContent(u.Unit), u.Count))));
            var log = new List<string>();
            b.ActionFired += x => log.Add($"{x.AtMs}:{x.Source.Unit.Id}:{x.Action.Id}:{x.Target?.Unit.Id}:{x.Amount:0.###}");
            drive(b);
            return $"{b.Outcome}|{b.NowMs}|{string.Join(",", log)}";
        }
        var oneGo = Run(b => b.AdvanceMs(120_000));
        var seconds = Run(b => { for (var i = 0; i < 120; i++) b.AdvanceMs(1_000); });
        var frames = Run(b => { for (var i = 0; i < 7_200; i++) b.AdvanceMs(16); b.AdvanceMs(120_000 - 7_200 * 16); });
        Assert.Equal(oneGo, seconds);
        Assert.Equal(oneGo, frames);
    }
}


public class SquadBattleTests
{
    private static BattleUnit Unit(string id, double hp, params BattleUnitAction[] actions) => new(id, id, hp, actions.ToList());

    [Fact]
    public void A_squad_holds_up_to_its_capacity_and_takes_replacements_mid_fight()
    {
        var a = Unit("a", 10, new BattleUnitAction("hit", "Hit", 1, "damage", 1, "front", "melee"));
        var t = Unit("t", 1_000_000);
        var b = new BattleController(new[] { new BattleEntry("front", a, 2, 5, Slot: 4) }, new[] { new BattleEntry("front", t, 1) });
        var g = b.SlotGroup(4)!;
        Assert.Equal(2, g.Alive);
        Assert.Equal(50, g.MaxHp, 9);                       // the bar is out of five figures
        Assert.Equal(3, b.Reinforce(4, 10));                // only room for three more
        Assert.Equal(5, g.Alive);
        Assert.Equal(0, b.Reinforce(4, 1));
        Assert.Equal(0, b.Reinforce(7, 1));                 // no squad for that slot
        Assert.Equal(new Dictionary<string, long> { ["a"] = 5 }, b.Fielded(BattleSide.Attacker));
    }

    [Fact]
    public void An_empty_squad_waits_in_the_battle_for_its_first_figure()
    {
        var a = Unit("a", 10, new BattleUnitAction("hit", "Hit", 1, "damage", 1, "front", "melee"));
        var t = Unit("t", 1_000_000);
        var b = new BattleController(new[] { new BattleEntry("front", a, 1, 5, 2), new BattleEntry("back", a, 0, 5, 0) }, new[] { new BattleEntry("front", t, 1) });
        Assert.False(b.SlotGroup(0)!.IsAlive);
        Assert.Equal(1, b.Reinforce(0, 1));
        Assert.True(b.SlotGroup(0)!.IsAlive);
    }

    [Fact]
    public void Healing_never_raises_a_squad_past_the_figures_it_fielded()
    {
        var hitter = Unit("hitter", 1000, new BattleUnitAction("hit", "Hit", 1, "damage", 5, "front", "melee"));
        var squad = Unit("squad", 10, new BattleUnitAction("mend", "Mend", 1, "heal", 100, "self", "ranged"));
        var b = new BattleController(new[] { new BattleEntry("front", hitter, 1) }, new[] { new BattleEntry("front", squad, 2, 5, 0) });
        b.Advance(3);
        Assert.Equal(20, b.Groups[1].Hp, 9);                // healed back to two figures, not five
        Assert.Equal(2, b.Groups[1].Alive);
    }
}

public class WorldTests
{
    /// <summary>Front column first, then mid, then back (top to bottom).</summary>
    internal static readonly int[] FrontFirst = { 2, 5, 8, 1, 4, 7, 0, 3, 6 };

    private static WorldState Fresh(out Stockpile stock, ContentDatabase? db = null)
    {
        db ??= World.SampleContent;
        stock = new Stockpile(new Storage(db), new Inventory(db));
        var w = new WorldState(db, stock, new LittleLich.Crypt.Units.UnitBuilder(db, stock, Unlocks.None));
        w.Load(new SaveData());
        return w;
    }

    private static ContentDatabase ContentWith(string json) => ContentLoader.CreateDefault().Load(
        ContentSource.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Data")).Append(new ContentSource("extra.json", json)));

    /// <summary>Fills <paramref name="squads"/> slots (front first) with full squads of a unit.</summary>
    internal static void Squads(LittleLich.Crypt.Units.UnitBuilder u, string unit, int squads)
    {
        foreach (var i in FrontFirst.Take(squads)) Assert.True(u.SetSlot(i, unit));
        u.DebugFillSquads();
    }

    private static void Fight(WorldState w)
    {
        for (var i = 0; i < 300 && w.Battle is not null; i++) w.Advance(1);
    }

    [Fact]
    public void Six_full_squads_clear_the_graveyard_and_get_its_rewards()
    {
        var w = Fresh(out var stock);
        Squads(w.Units, "skeleton", 6);
        Assert.Equal(30, w.ArmyFigures);
        Assert.Equal(SendResult.Ok, w.Send("graveyard"));
        Assert.Equal(AreaState.InProgress, w.State("graveyard"));
        Assert.Equal(SendResult.BattleRunning, w.Send("woods"));   // one battle at a time
        Assert.Equal(6, w.Battle!.GroupsOf(BattleSide.Attacker).Count());
        Assert.Equal(new[] { "front", "front", "front", "middle", "middle", "middle" }, w.Battle.GroupsOf(BattleSide.Attacker).Select(g => g.Section));

        Fight(w);
        var r = w.LastResult!;
        Assert.Equal(BattleOutcome.Victory, r.Outcome);
        Assert.Equal(AreaState.Cleared, w.State("graveyard"));
        Assert.InRange(r.Survivors["skeleton"], 1, 30);
        Assert.Equal(r.Survivors["skeleton"], w.ArmyFigures);           // survivors stay in their squads
        Assert.Equal(30, r.Lost + r.Survivors["skeleton"]);
        Assert.Equal(60, stock.Get("bones"));
        Assert.Equal(300, stock.Get("gold"));
        Assert.Equal(SendResult.AlreadyCleared, w.Send("graveyard"));
    }

    [Fact]
    public void The_formation_is_locked_while_the_army_fights()
    {
        var w = Fresh(out _);
        Squads(w.Units, "skeleton", 2);
        w.Send("woods");
        Assert.False(w.Units.SetSlot(0, "skeleton"));
        w.Units.ClearSlot(2);
        w.Units.SwapSlots(2, 0);
        Assert.Equal("skeleton", w.Units.GetSlot(2));
        Assert.Null(w.Units.GetSlot(0));
        w.Retreat();
        Assert.True(w.Units.SetSlot(0, "skeleton"));
    }

    [Fact]
    public void A_weak_army_loses_and_the_squads_are_left_empty()
    {
        var w = Fresh(out var stock);
        Squads(w.Units, "skeleton", 1);
        w.Units.SyncFigures(2, 3);
        w.Send("woods");
        Fight(w);
        Assert.Equal(BattleOutcome.Defeat, w.LastResult!.Outcome);
        Assert.Equal(AreaState.NotStarted, w.State("woods"));
        Assert.Equal(0, w.ArmyFigures);
        Assert.Equal("skeleton", w.Units.GetSlot(2));                    // the slot keeps its unit for the lich to refill
        Assert.Equal(0, stock.Get("gold"));
    }

    [Fact]
    public void Retreat_keeps_the_survivors_in_their_squads()
    {
        var w = Fresh(out _);
        Squads(w.Units, "skeleton_warrior", 2);
        w.Send("woods");
        w.Advance(4);
        Assert.True(w.Retreat());
        Assert.Equal(BattleOutcome.Retreat, w.LastResult!.Outcome);
        Assert.Equal(w.LastResult.Survivors["skeleton_warrior"], w.ArmyFigures);
        Assert.False(w.Retreat());
    }

    [Fact]
    public void The_lichs_replacements_join_the_running_battle()
    {
        var w = Fresh(out var stock);
        Squads(w.Units, "skeleton", 1);
        w.Units.SyncFigures(2, 2);
        stock.Add("mana", 1000);
        stock.Add("souls", 100);
        stock.Add("bones", 100);
        w.Send("woods");
        Assert.Equal(1, w.Units.SummonIntoSlot(2, 1));
        Assert.Equal(3, w.Battle!.SlotGroup(2)!.Alive);
        Assert.Equal(1, w.Battle.SlotGroup(2)!.Reinforced);
        w.Retreat();
        Assert.Equal(3, w.LastResult!.Sent["skeleton"]);
    }

    [Fact]
    public void Send_needs_a_figure_standing_a_known_area_and_a_path()
    {
        var w = Fresh(out _);
        Assert.Equal(SendResult.EmptyArmy, w.Send("graveyard"));
        Assert.True(w.Units.SetSlot(2, "skeleton"));
        Assert.Equal(SendResult.EmptyArmy, w.Send("graveyard"));      // a new slot starts with no figures
        Assert.False(w.Units.SetSlot(2, "rival_skeleton"));           // enemies cannot join the army
        w.Units.DebugFillSquads();
        Assert.Equal(SendResult.UnknownArea, w.Send("castle"));
        Assert.Equal(SendResult.Ok, w.Send("graveyard"));
    }

    [Fact]
    public void Areas_behind_an_uncleared_area_are_not_reachable()
    {
        var db = ContentWith("""
            [ { "type": "map_node", "id": "deep_crypt", "name": "Deep Crypt", "position": [6, 1], "links": [ "graveyard_gates" ], "area": "deep" },
              { "type": "area", "id": "deep", "name": "Deep", "defenders": { "front": [ { "unit": "rival_skeleton", "count": 1 } ] } } ]
            """);
        var w = Fresh(out _, db);
        Assert.True(w.IsReachable("graveyard"));
        Assert.True(w.IsReachable("woods"));
        Assert.False(w.IsReachable("deep"));
        Squads(w.Units, "skeleton", 8);
        Assert.Equal(SendResult.NotReachable, w.Send("deep"));
        w.Send("graveyard");
        Fight(w);
        Assert.True(w.IsReachable("deep"));
    }
}

public class WorldSaveTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeClock _clock = new();
    public void Dispose() => _dir.Dispose();

    private (GameSession, WorldState, Stockpile) Session()
    {
        var db = World.SampleContent;
        var stock = new Stockpile(new Storage(db), new Inventory(db));
        var units = new LittleLich.Crypt.Units.UnitBuilder(db, stock, Unlocks.None);
        var w = new WorldState(db, stock, units);
        var s = new GameSession(db, new SaveService(_dir.Path, _clock.Get), _clock.Get);
        s.Register(stock.Storage);
        s.Register(units);        // before the army and the world, as at boot
        s.Register(units.Army);
        s.Register(w);
        return (s, w, stock);
    }

    [Fact]
    public void A_cleared_area_and_the_squads_survive_reload()
    {
        var (s1, a, _) = Session();
        s1.NewGame(1);
        WorldTests.Squads(a.Units, "skeleton", 7);
        Assert.True(a.Units.SetSlot(6, "skeleton_warrior"));
        a.Send("graveyard");
        for (var i = 0; i < 300 && a.Battle is not null; i++) a.Advance(1);
        s1.Save();

        var (s2, b, stock) = Session();
        s2.LoadSlot(1);
        Assert.Equal(AreaState.Cleared, b.State("graveyard"));
        Assert.Equal(a.ArmyFigures, b.ArmyFigures);
        Assert.Equal("skeleton_warrior", b.Units.GetSlot(6));
        Assert.Equal(0, b.Units.Figures(6));
        Assert.Equal(300, stock.Get("gold"));
    }

    [Fact]
    public void A_battle_is_not_saved_mid_fight_it_restarts_on_load_with_the_saved_squads()
    {
        var (s1, a, _) = Session();
        s1.NewGame(1);
        WorldTests.Squads(a.Units, "skeleton", 6);
        a.Send("graveyard");
        a.Advance(3);                                            // into the fight
        Assert.True(a.Battle!.SideHp(BattleSide.Defender) < a.Battle.SideMaxHp(BattleSide.Defender));
        var standing = a.ArmyFigures;
        s1.Save();

        var (s2, b, _) = Session();
        s2.LoadSlot(1);
        Assert.Equal(AreaState.InProgress, b.State("graveyard"));
        Assert.Equal(0, b.Battle!.NowMs);                          // restarted from the beginning
        Assert.Equal(b.Battle.SideMaxHp(BattleSide.Defender), b.Battle.SideHp(BattleSide.Defender));
        Assert.Equal(standing, b.Battle.Survivors(BattleSide.Attacker)["skeleton"]);
        for (var i = 0; i < 300 && b.Battle is not null; i++) b.Advance(1);
        Assert.Equal(AreaState.Cleared, b.State("graveyard"));
        Assert.Equal(b.LastResult!.Survivors["skeleton"], b.ArmyFigures);
    }
}

public class WorldContentTests
{
    private static ContentLoadException Fails(string json) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.CreateDefault().Load(
            ContentSource.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Data")).Append(new ContentSource("x.json", json))));

    [Fact]
    public void Units_have_at_most_three_valid_actions()
    {
        var e = Fails("""[ { "type": "wild", "id": "x", "name": "X", "hp": 5, "actions": [ "strike", "brace", "bite", "smash" ] } ]""");
        Assert.Contains(e.Errors, x => x.Id == "x" && x.Field == "actions" && x.Message.Contains("at most 3"));
        e = Fails("""[ { "type": "battle_action", "id": "zap", "name": "Zap", "cooldown_seconds": 0.01, "effect": "explode", "target": "moon", "value": -1 } ]""");
        Assert.Contains(e.Errors, x => x.Field == "cooldown_seconds");
        Assert.Contains(e.Errors, x => x.Field == "effect");
        Assert.Contains(e.Errors, x => x.Field == "target");
        Assert.Contains(e.Errors, x => x.Field == "value");
    }

    [Fact]
    public void Areas_and_paths_must_reference_real_things()
    {
        var e = Fails("""
            [ { "type": "area", "id": "x", "name": "X", "defenders": { "front": [ { "unit": "dragon", "count": 1 } ] }, "rewards": { "silver": 5 } },
              { "type": "map_node", "id": "n", "name": "N", "links": [ "nowhere" ] } ]
            """);
        Assert.Contains(e.Errors, x => x.Id == "x" && x.Message.Contains("dragon"));
        Assert.Contains(e.Errors, x => x.Field == "rewards.silver");
        Assert.Contains(e.Errors, x => x.Id == "x" && x.Message.Contains("no map node holds"));
        Assert.Contains(e.Errors, x => x.Field == "links");
    }

    [Fact]
    public void The_sample_world_has_the_graveyard_and_the_woods()
    {
        var db = World.SampleContent;
        Assert.Equal(new[] { "graveyard", "woods" }, db.All<AreaDefinitionBase>().Select(a => a.Id).OrderBy(x => x));
        Assert.Single(db.All<MapNodeDefinitionBase>(), n => n.Home);
    }
}
