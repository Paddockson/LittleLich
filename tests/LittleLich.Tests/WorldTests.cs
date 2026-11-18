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

public class WorldTests
{
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

    private static ArmyPlan Skeletons(long n) => new ArmyPlan().Set(Sections.Front, "skeleton", n);

    [Fact]
    public void A_placeholder_army_clears_the_graveyard_and_gets_its_rewards()
    {
        var w = Fresh(out var stock);
        w.AddUnits("skeleton", 30);
        Assert.Equal(SendResult.Ok, w.Send("graveyard", Skeletons(30)));
        Assert.Equal(AreaState.InProgress, w.State("graveyard"));
        Assert.Equal(0, w.InRoster("skeleton"));
        Assert.Equal(SendResult.BattleRunning, w.Send("woods", Skeletons(1)));   // one battle at a time

        for (var i = 0; i < 300 && w.Battle is not null; i++) w.Advance(1);
        var r = w.LastResult!;
        Assert.Equal(BattleOutcome.Victory, r.Outcome);
        Assert.Equal(AreaState.Cleared, w.State("graveyard"));
        Assert.InRange(r.Survivors["skeleton"], 1, 30);            // data skeletons (100 HP, 10 damage) may lose none
        Assert.Equal(r.Survivors["skeleton"], w.InRoster("skeleton"));
        Assert.Equal(30, r.Lost + r.Survivors["skeleton"]);
        Assert.Equal(60, stock.Get("bones"));
        Assert.Equal(300, stock.Get("gold"));
        Assert.Equal(SendResult.AlreadyCleared, w.Send("graveyard", Skeletons(1)));
    }

    [Fact]
    public void A_weak_army_loses_and_the_area_stays_open()
    {
        var w = Fresh(out var stock);
        w.AddUnits("skeleton", 3);
        w.Send("woods", Skeletons(3));
        for (var i = 0; i < 300 && w.Battle is not null; i++) w.Advance(1);
        Assert.Equal(BattleOutcome.Defeat, w.LastResult!.Outcome);
        Assert.Equal(AreaState.NotStarted, w.State("woods"));
        Assert.Equal(0, w.InRoster("skeleton"));
        Assert.Equal(0, stock.Get("gold"));
    }

    [Fact]
    public void Retreat_brings_survivors_home()
    {
        var w = Fresh(out _);
        w.AddUnits("skeleton_warrior", 10);
        w.Send("woods", new ArmyPlan().Set(Sections.Front, "skeleton_warrior", 10));
        w.Advance(4);
        Assert.True(w.Retreat());
        Assert.Equal(BattleOutcome.Retreat, w.LastResult!.Outcome);
        Assert.Equal(w.LastResult.Survivors["skeleton_warrior"], w.InRoster("skeleton_warrior"));
        Assert.False(w.Retreat());
    }

    [Fact]
    public void Send_checks_the_garrison_units_and_paths()
    {
        var w = Fresh(out _);
        Assert.Equal(SendResult.EmptyArmy, w.Send("graveyard", new ArmyPlan()));
        Assert.Equal(SendResult.UnknownUnit, w.Send("graveyard", Skeletons(1)));   // no "skeleton" unit type yet
        w.AddUnits("skeleton", 5);
        Assert.Equal(SendResult.NotEnoughUnits, w.Send("graveyard", Skeletons(6)));
        Assert.Equal(SendResult.UnknownUnit, w.Send("graveyard", new ArmyPlan().Set(Sections.Front, "rival_skeleton", 1)));
        Assert.Equal(SendResult.UnknownArea, w.Send("castle", Skeletons(1)));
        Assert.Throws<ArgumentException>(() => w.AddUnits("grave_archer", 5));   // enemies cannot join the roster
        Assert.Equal(5, w.InRoster("skeleton"));
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
        w.AddUnits("skeleton", 60);
        Assert.Equal(SendResult.NotReachable, w.Send("deep", Skeletons(1)));
        w.Send("graveyard", Skeletons(40));
        for (var i = 0; i < 300 && w.Battle is not null; i++) w.Advance(1);
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
        s.Register(units);   // before the world, as at boot
        s.Register(w);
        return (s, w, stock);
    }

    [Fact]
    public void A_cleared_area_and_the_garrison_survive_reload()
    {
        var (s1, a, _) = Session();
        s1.NewGame(1);
        a.AddUnits("skeleton", 35);
        a.AddUnits("skeleton_warrior", 4);
        a.Send("graveyard", new ArmyPlan().Set(Sections.Front, "skeleton", 35));
        for (var i = 0; i < 300 && a.Battle is not null; i++) a.Advance(1);
        s1.Save();

        var (s2, b, stock) = Session();
        s2.LoadSlot(1);
        Assert.Equal(AreaState.Cleared, b.State("graveyard"));
        Assert.Equal(a.InRoster("skeleton"), b.InRoster("skeleton"));
        Assert.Equal(4, b.InRoster("skeleton_warrior"));
        Assert.Equal(300, stock.Get("gold"));
    }

    [Fact]
    public void A_battle_is_not_saved_mid_fight_it_restarts_on_load()
    {
        var (s1, a, _) = Session();
        s1.NewGame(1);
        a.AddUnits("skeleton", 40);
        a.Send("graveyard", new ArmyPlan().Set(Sections.Front, "skeleton", 30));
        a.Advance(3);                                            // into the fight (data skeletons win fast)
        Assert.True(a.Battle!.SideHp(BattleSide.Defender) < a.Battle.SideMaxHp(BattleSide.Defender));
        s1.Save();

        var (s2, b, _) = Session();
        s2.LoadSlot(1);
        Assert.Equal(AreaState.InProgress, b.State("graveyard"));
        Assert.Equal(0, b.Battle!.NowMs);                          // restarted from the beginning
        Assert.Equal(b.Battle.SideMaxHp(BattleSide.Defender), b.Battle.SideHp(BattleSide.Defender));
        Assert.Equal(30, b.Battle.Survivors(BattleSide.Attacker)["skeleton"]);
        Assert.Equal(10, b.InRoster("skeleton"));                // the 30 sent are out, not doubled
        for (var i = 0; i < 300 && b.Battle is not null; i++) b.Advance(1);
        Assert.Equal(AreaState.Cleared, b.State("graveyard"));
        Assert.Equal(10 + b.LastResult!.Survivors["skeleton"], b.InRoster("skeleton"));
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
