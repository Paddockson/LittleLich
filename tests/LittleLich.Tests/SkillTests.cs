using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Core.Saves;
using LittleLich.Core.Ticks;
using LittleLich.Crypt;
using LittleLich.Skills;
using Xunit;

namespace LittleLich.Tests;

/// <summary>The phase 4 game world without Godot: lich, storage, inventory, ten skills, scheduler.</summary>
internal sealed class World
{
    public static readonly ContentDatabase SampleContent =
        ContentLoader.CreateDefault().Load(ContentSource.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Data")));

    public LichState Lich { get; } = new();
    public Storage Storage { get; }
    public Inventory Inventory { get; }
    public Stockpile Stock { get; }
    public TickScheduler Ticks { get; } = new();
    public IReadOnlyList<SkillBase> Skills { get; }
    public IReadOnlyList<LittleLich.Crypt.Buildings.BuildingBase> Buildings { get; }
    public LittleLich.Gear.Equipment Equipment { get; }
    public Unlocks Unlocks { get; } = new();
    public LittleLich.Crypt.Research.ResearchManager Research { get; }
    public LittleLich.Crypt.Units.UnitBuilder Units { get; }
    public LittleLich.World.WorldState WorldMap { get; }
    public LittleLich.Gear.ItemUser Items { get; }

    public World(ContentDatabase? content = null)
    {
        content ??= SampleContent;
        Storage = new Storage(content);
        Inventory = new Inventory(content);
        Stock = new Stockpile(Storage, Inventory);
        Research = new LittleLich.Crypt.Research.ResearchManager(Lich, Stock, content, Unlocks, () => Ticks.ActiveRecap);
        Units = new LittleLich.Crypt.Units.UnitBuilder(content, Stock, Unlocks);
        Buildings = LittleLich.Crypt.Buildings.BuildingFactory.CreateAll(
            new LittleLich.Crypt.Buildings.CryptContext(Lich, Stock, content, () => Ticks.ActiveRecap) { Unlocks = Unlocks, Units = Units });
        WorldMap = new LittleLich.World.WorldState(content, Stock, Units);
        Skills = SkillFactory.CreateAll(new SkillContext(Lich, Stock, content, () => Ticks.ActiveRecap) { Unlocks = Unlocks });
        Research.SkillLevel = id => Skills.First(s => s.Id == id).Level;     // as GameManager wires it
        Research.SkillXpEarned += (id, xp) => Skills.First(s => s.Id == id).GrantXp(xp);
        Equipment = new LittleLich.Gear.Equipment(Lich, Inventory, content);
        Items = new LittleLich.Gear.ItemUser(Lich, Inventory, content);
        Ticks.TickStarted += (_, _) => Storage.BeginBatch();   // as CryptManager does
        Ticks.TickEnded += (_, n) => Storage.EndBatch(n);
        Ticks.Register(Lich, TickGroup.Lich);
        var order = 0;
        foreach (var b in Buildings) { order += 100; if (b.Ticks) Ticks.Register(b, TickGroup.Buildings, order); }   // as CryptManager does
        foreach (var g in Skills.OfType<GatheringSkill>()) Ticks.Register(g, TickGroup.Skills, TickOrder.Gathering);
        foreach (var c in Skills.OfType<CraftingSkill>()) Ticks.Register(c, TickGroup.Skills, TickOrder.Crafting);
        Ticks.Register(Research, TickGroup.Research);
    }

    public IEnumerable<ISaveable> Saveables => new ISaveable[] { Lich, Storage, Inventory, Research, Units }.Concat(Buildings).Concat(Skills).Append(Equipment).Append(WorldMap);

    public T Skill<T>() where T : SkillBase => Skills.OfType<T>().Single();
    public MiningManager Mining => Skill<MiningManager>();
    public SmithingManager Smithing => Skill<SmithingManager>();
    public HealthSkill Health => Skill<HealthSkill>();
}

public class XpCurveTests
{
    [Fact]
    public void Each_level_needs_ten_percent_more_than_the_last()
    {
        Assert.Equal(new Num(0), XpCurve.XpForLevel(1));
        Assert.Equal(new Num(10), XpCurve.XpForLevel(2));
        Assert.Equal(21.0, XpCurve.XpForLevel(3).ToDouble(), 9);
        for (var l = 3; l < 999; l++)
        {
            var step = (XpCurve.XpForLevel(l + 1) - XpCurve.XpForLevel(l)).ToDouble();
            var prev = (XpCurve.XpForLevel(l) - XpCurve.XpForLevel(l - 1)).ToDouble();
            Assert.Equal(1.1, step / prev, 9);
        }
        var top = XpCurve.XpForLevel(999).ToDouble();
        Assert.InRange(top, 1e43, 1e44);
    }

    [Fact]
    public void LevelForXp_finds_the_level_at_the_boundaries()
    {
        Assert.Equal(1, XpCurve.LevelForXp(Num.Zero));
        Assert.Equal(1, XpCurve.LevelForXp(9.99));
        Assert.Equal(2, XpCurve.LevelForXp(10));
        Assert.Equal(2, XpCurve.LevelForXp(20.9));
        Assert.Equal(3, XpCurve.LevelForXp(21));
        for (var l = 1; l <= 999; l += 37)
            Assert.Equal(l, XpCurve.LevelForXp(XpCurve.XpForLevel(l)));
        Assert.Equal(999, XpCurve.LevelForXp(1e60));
        Assert.Equal(1, XpCurve.LevelForXp(-5));
    }
}

public class SkillTests
{
    [Fact]
    public void Sample_content_gives_every_skill_something_to_do()
    {
        var w = new World();
        Assert.Equal(SkillIds.All, w.Skills.Select(s => s.Id));
        Assert.Equal(new[] { "copper_ore", "coal", "iron_ore" }, w.Mining.Activities.Select(a => a.Id));
        foreach (var g in w.Skills.OfType<GatheringSkill>()) Assert.NotEmpty(g.Activities);
        foreach (var c in w.Skills.OfType<CraftingSkill>()) Assert.True(c.Recipes.Count >= 2);
    }

    [Fact]
    public void Pressing_plus_on_iron_ore_moves_necromancers_and_iron_ore_rises_each_tick()
    {
        var w = new World();
        w.Lich.DebugAddNecromancers(10);

        Assert.Equal(0, w.Mining.AddNecromancer("iron_ore", 10)); // locked at level 1 (needs 10)
        Assert.Equal(10, w.Lich.Pool.Free);

        w.Mining.DebugSetLevel(10);
        Assert.Equal(10, w.Mining.AddNecromancer("iron_ore", 10));
        Assert.Equal(0, w.Lich.Pool.Free);
        Assert.Equal(10, w.Lich.Pool.Assigned);
        Assert.Equal(10, w.Mining.GetAssigned("iron_ore"));

        var seen = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            w.Ticks.RunTicks(1);
            seen.Add(w.Stock.Get("iron_ore"));
        }
        Assert.Equal(new long[] { 3, 6, 9, 12, 15 }, seen);   // 10 x 0.3 per tick
        Assert.Equal(3.0, w.Mining.RatePerTick("iron_ore"), 9);

        Assert.Equal(4, w.Mining.RemoveNecromancer("iron_ore", 4));
        Assert.Equal(4, w.Lich.Pool.Free);
    }

    [Fact]
    public void Level_up_raises_the_unlocked_activity_list()
    {
        var w = new World();
        var levels = new List<(int, int)>();
        w.Mining.LeveledUp += (_, from, to) => levels.Add((from, to));
        Assert.Equal(new[] { "copper_ore" }, w.Mining.UnlockedActivities.Select(a => a.Id));

        w.Lich.DebugAddNecromancers(4);
        w.Mining.AddNecromancer("copper_ore", 4);   // 2 ore/tick, 10 XP/tick
        var ticks = 0;
        while (w.Mining.Level < 5) { w.Ticks.RunTicks(1); ticks++; }
        Assert.Equal(new[] { "copper_ore", "coal" }, w.Mining.UnlockedActivities.Select(a => a.Id));
        Assert.Equal((1, 2), levels[0]);

        w.Mining.DebugSetLevel(10);
        Assert.Equal(new[] { "copper_ore", "coal", "iron_ore" }, w.Mining.UnlockedActivities.Select(a => a.Id));
    }

    private static World Busy()
    {
        var w = new World();
        w.Lich.DebugAddNecromancers(1_000);
        w.Mining.DebugSetLevel(10);
        w.Smithing.DebugSetLevel(15);
        w.Skill<FishingManager>().AddNecromancer("eel_pool", 7);
        w.Mining.AddNecromancer("copper_ore", 3);
        w.Mining.AddNecromancer("coal", 13);
        w.Mining.AddNecromancer("iron_ore", 11);
        // Crafting from pre-stocked inputs (gathering feeding crafting in the same batch is order-dependent; see phase 3).
        w.Stock.Add("copper_bar", 100);
        w.Smithing.AddNecromancer("copper_sword", 4);
        w.Stock.Add("iron_bar", 37);
        w.Smithing.AddNecromancer("iron_armour", 9);
        return w;
    }

    private static string Snapshot(World w) =>
        string.Join("|", w.Storage.Save().Node.ToJsonString(), w.Inventory.Save().Node.ToJsonString(),
            // XP is a double (Num): one big addition and many small ones can differ in the last bits, so compare it to
            // 6 decimals; levels, stock and progress must match exactly.
            string.Join(";", w.Skills.Select(s =>
            {
                var node = s.Save().Node;
                node["xp"] = Math.Round(s.Xp.ToDouble(), 6);
                return node.ToJsonString();
            })),
            string.Join(";", w.Smithing.Recipes.Select(r => $"{r.Id}:{w.Smithing.Status(r.Id)}:{w.Smithing.MissingInput(r.Id)}")),
            string.Join(";", w.Mining.Activities.Select(a => $"{a.Id}:{w.Mining.IsCapped(a.Id)}")));

    [Theory]
    [InlineData(100)]
    [InlineData(7)]
    [InlineData(3_500)]   // long enough that ore hits its cap and the iron bars run out
    public void Live_ticks_equal_one_batched_tick(int n)
    {
        var live = Busy();
        for (var i = 0; i < n; i++) live.Ticks.RunTicks(1);

        var batched = Busy();
        batched.Ticks.RunTicks(n);

        Assert.Equal(Snapshot(live), Snapshot(batched));
    }

    [Fact]
    public void A_full_storage_stops_production_but_keeps_necromancers()
    {
        var w = new World();
        w.Inventory.SlotCapacity = 1;                // copper ore stacks to 1,000: one slot holds 1,000
        w.Lich.DebugAddNecromancers(10);
        w.Mining.AddNecromancer("copper_ore", 10); // 5 per tick
        var capped = new List<bool>();
        w.Mining.CappedChanged += (_, c) => capped.Add(c);

        w.Stock.Add("copper_ore", 997);
        w.Ticks.RunTicks(1);
        Assert.Equal(1_000, w.Stock.Get("copper_ore"));
        Assert.True(w.Mining.IsCapped("copper_ore"));
        Assert.Equal(0, w.Mining.RatePerTick("copper_ore"));
        var xp = w.Mining.Xp;
        w.Ticks.RunTicks(10);
        Assert.Equal(1_000, w.Stock.Get("copper_ore"));
        Assert.Equal(xp, w.Mining.Xp);
        Assert.Equal(10, w.Mining.GetAssigned("copper_ore"));

        w.Stock.TryRemove("copper_ore", 100);
        w.Ticks.RunTicks(1);
        Assert.Equal(905, w.Stock.Get("copper_ore"));
        Assert.False(w.Mining.IsCapped("copper_ore"));
        Assert.Equal(new[] { true, false }, capped);
    }

    [Fact]
    public void Fractions_carry_between_ticks()
    {
        var w = new World();
        w.Lich.DebugAddNecromancers(1);
        w.Mining.AddNecromancer("copper_ore", 1); // 0.5 per tick
        var seen = new List<long>();
        for (var i = 0; i < 4; i++) { w.Ticks.RunTicks(1); seen.Add(w.Stock.Get("copper_ore")); }
        Assert.Equal(new long[] { 0, 1, 1, 2 }, seen);
        Assert.Equal(new Num(10), w.Mining.Xp); // XP only for units actually stored
    }

    [Fact]
    public void Speed_modifier_multiplies_output()
    {
        var w = new World();
        w.Lich.DebugAddNecromancers(2);
        w.Mining.AddNecromancer("copper_ore", 2);
        w.Lich.AddModifier(new Modifier(SkillIds.SpeedStat(SkillIds.Mining), ModifierOp.Mul, 3, "potion"));
        w.Ticks.RunTicks(10);
        Assert.Equal(30, w.Stock.Get("copper_ore"));
    }

    [Fact]
    public void Crafting_uses_inputs_and_pauses_with_a_reason_when_they_run_out()
    {
        var w = new World();
        w.Lich.DebugAddNecromancers(10);
        var statuses = new List<RecipeStatus>();
        w.Smithing.StatusChanged += (_, s) => statuses.Add(s);
        w.Stock.Add("copper_ore", 9);
        Assert.Equal(2, w.Smithing.AddNecromancer("copper_bar", 2)); // 1 bar per tick (2 necros / 2 ticks)

        w.Ticks.RunTicks(1);
        Assert.Equal(1, w.Stock.Get("copper_bar"));
        Assert.Equal(7, w.Stock.Get("copper_ore"));
        Assert.Equal(RecipeStatus.Running, w.Smithing.Status("copper_bar"));
        Assert.Equal(new Num(6), w.Smithing.Xp);

        w.Ticks.RunTicks(10);
        Assert.Equal(4, w.Stock.Get("copper_bar"));
        Assert.Equal(1, w.Stock.Get("copper_ore"));
        Assert.Equal(RecipeStatus.MissingInput, w.Smithing.Status("copper_bar"));
        Assert.Equal("copper_ore", w.Smithing.MissingInput("copper_bar"));
        Assert.Equal(2, w.Smithing.GetAssigned("copper_bar"));

        w.Smithing.RemoveNecromancer("copper_bar", 2);
        Assert.Equal(RecipeStatus.Idle, w.Smithing.Status("copper_bar"));
        Assert.Equal(new[] { RecipeStatus.Running, RecipeStatus.MissingInput, RecipeStatus.Idle }, statuses);
    }

    [Fact]
    public void Crafting_into_a_full_output_pauses_as_output_full()
    {
        var w = new World();
        w.Lich.DebugAddNecromancers(2);
        w.Inventory.SlotCapacity = 2;                // one slot of ore, one full slot of bars
        w.Stock.Add("copper_ore", 100);
        w.Stock.Add("copper_bar", 1_000); // full
        w.Smithing.AddNecromancer("copper_bar", 2);
        w.Ticks.RunTicks(5);
        Assert.Equal(RecipeStatus.OutputFull, w.Smithing.Status("copper_bar"));
        Assert.Equal(100, w.Stock.Get("copper_ore"));
    }

    [Fact]
    public void Crafted_items_go_to_the_inventory()
    {
        var w = new World();
        w.Lich.DebugAddNecromancers(5);
        w.Smithing.DebugSetLevel(5);
        w.Stock.Add("copper_bar", 9);
        w.Smithing.AddNecromancer("copper_sword", 5); // 1 sword per tick
        w.Ticks.RunTicks(10);
        Assert.Equal(3, w.Inventory.Get("copper_sword"));
        Assert.Equal(0, w.Stock.Get("copper_bar"));
    }

    [Fact]
    public void Locked_recipes_cannot_take_necromancers_and_unknown_ids_throw()
    {
        var w = new World();
        w.Lich.DebugAddNecromancers(5);
        Assert.Equal(0, w.Smithing.AddNecromancer("iron_armour", 1));
        Assert.Throws<ArgumentException>(() => w.Smithing.AddNecromancer("nope", 1));
        Assert.Throws<ArgumentException>(() => w.Mining.AddNecromancer("eel_pool", 1)); // another skill's activity
    }

    [Fact]
    public void Offline_runs_fill_the_recap()
    {
        var w = new World();
        w.Inventory.SlotCapacity = 1;
        w.Lich.DebugAddNecromancers(10);
        w.Mining.AddNecromancer("copper_ore", 10);
        var recap = w.Ticks.RunOffline(3_600); // 1,800 ticks x 5 = 9,000, capped at 1,000
        Assert.Equal(new Num(1_000), recap.Gained[("item", "copper_ore")]);
        Assert.Equal(new Num(5_000), recap.Gained[("xp", "mining")]);
        Assert.Contains("copper_ore", recap.Capped);
    }

    [Fact]
    public void Health_skill_raises_max_health_without_dropping_current_health()
    {
        var w = new World();
        w.Health.DebugSetLevel(5);
        Assert.Equal(new Num(140), w.Lich.MaxHealth);
        Assert.Equal(new Num(100), w.Lich.Health); // no regen: a higher max does not heal
        w.Lich.Heal(1_000);
        Assert.Equal(new Num(140), w.Lich.Health);
        w.Health.DebugSetLevel(6);
        Assert.Equal(new Num(150), w.Lich.MaxHealth);
        Assert.Equal(new Num(140), w.Lich.Health);
        Assert.Single(w.Lich.Stats.Modifiers, m => m.Source == HealthSkill.Source);
    }
}

public class SkillSaveTests : IDisposable
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
    public void Level_xp_counts_carry_stock_and_items_survive_reload()
    {
        var (s1, a) = Session();
        s1.NewGame(1);
        a.Lich.DebugAddNecromancers(50);
        a.Mining.DebugSetLevel(12);
        a.Mining.GrantXp(3.5);
        a.Mining.AddNecromancer("iron_ore", 7);   // 2.1 per tick: leaves a carry
        a.Smithing.DebugSetLevel(5);
        a.Stock.Add("copper_bar", 50);
        a.Smithing.AddNecromancer("copper_sword", 3);
        a.Health.DebugSetLevel(4);
        a.Lich.Heal(1_000);
        a.Ticks.RunTicks(3);
        s1.Save();

        var (s2, b) = Session();
        s2.LoadSlot(1);

        foreach (var (x, y) in a.Skills.Zip(b.Skills))
        {
            Assert.Equal(x.Level, y.Level);
            Assert.Equal(x.Xp, y.Xp);
            Assert.Equal(x.Save().Node.ToJsonString(), y.Save().Node.ToJsonString());
        }
        Assert.Equal(7, b.Mining.GetAssigned("iron_ore"));
        Assert.Equal(3, b.Smithing.GetAssigned("copper_sword"));
        Assert.Equal(a.Stock.Get("iron_ore"), b.Stock.Get("iron_ore"));
        Assert.Equal(a.Inventory.Get("copper_sword"), b.Inventory.Get("copper_sword"));
        Assert.Equal(new Num(130), b.Lich.MaxHealth);
        Assert.Equal(new Num(130), b.Lich.Health); // not clamped to the base 100 during load

        // Same future: both continue identically.
        a.Ticks.RunTicks(17);
        b.Ticks.RunTicks(17);
        Assert.Equal(a.Stock.Get("iron_ore"), b.Stock.Get("iron_ore"));
        Assert.Equal(a.Mining.Xp, b.Mining.Xp);
    }

    [Fact]
    public void New_game_resets_skills_storage_and_inventory()
    {
        var (s, w) = Session();
        s.NewGame(1);
        w.Mining.DebugSetLevel(20);
        w.Stock.Add("coal", 10);
        w.Inventory.Add("rot_bomb", 2);
        w.Health.DebugSetLevel(3);
        s.NewGame(2);
        Assert.Equal(1, w.Mining.Level);
        Assert.Equal(0, w.Stock.Get("coal"));
        Assert.Equal(0, w.Inventory.Get("rot_bomb"));
        Assert.Equal(new Num(100), w.Lich.MaxHealth);
    }
}

public class StockTests
{
    [Fact]
    public void Storage_caps_and_removes_all_or_nothing()
    {
        var st = new Storage(World.SampleContent);
        Assert.Equal(500, st.Add("fire", 5_000));
        Assert.Equal(0, st.Add("fire", 1));
        Assert.False(st.TryRemove("fire", 501));
        Assert.True(st.TryRemove("fire", 500));
        Assert.Throws<ArgumentException>(() => st.Add("copper_sword", 1)); // an item, not a resource
        Assert.Throws<ArgumentException>(() => st.Add("coal", 1));         // a material: items live in the inventory
    }

    [Fact]
    public void Storage_rates_are_net_change_per_tick_over_a_batch()
    {
        var st = new Storage(World.SampleContent);
        st.BeginBatch();
        st.Add("water", 30);
        st.TryRemove("water", 10);
        st.EndBatch(10);
        Assert.Equal(2.0, st.RatePerTick("water"));
        st.BeginBatch();
        st.EndBatch(1);
        Assert.Equal(0.0, st.RatePerTick("water"));
    }

    [Fact]
    public void Load_drops_unknown_ids()
    {
        var st = new Storage(World.SampleContent);
        st.Load(new SaveData(System.Text.Json.Nodes.JsonNode.Parse("""{ "stock": { "earth": 5, "unobtainium": 9, "souls": -3, "coal": 4 } }""")!.AsObject()));
        Assert.Equal(5, st.Get("earth"));
        Assert.Equal(0, st.Get("coal"));
        Assert.Equal(0, st.Get("souls"));
        var inv = new Inventory(World.SampleContent);
        inv.Load(new SaveData(System.Text.Json.Nodes.JsonNode.Parse("""{ "stacks": { "rot_bomb": 2, "mana": 9 } }""")!.AsObject()));
        Assert.Equal(2, inv.Get("rot_bomb"));
        Assert.Empty(inv.Stacks.Where(x => x.Id == "mana"));      // a resource, not an item
    }
}

public class SkillContentTests
{
    private static ContentLoadException Fails(string json, string file = "x.json")
    {
        var sources = ContentSource.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Data"))
            .Append(new ContentSource(file, json));
        return Assert.Throws<ContentLoadException>(() => ContentLoader.CreateDefault().Load(sources));
    }

    [Fact]
    public void Item_ids_cannot_clash_with_resource_ids() =>
        Assert.Contains(Fails("""[{ "type": "material", "id": "mana", "name": "Mana" }]""").Errors,
            e => e.Id == "mana" && e.Field == "id");

    [Fact]
    public void Recipes_must_reference_real_stock_and_a_crafting_skill()
    {
        var e = Fails("""[{ "type": "recipe", "id": "bad", "name": "Bad", "skill": "mining", "inputs": [ { "id": "coal", "count": 1 } ], "output": { "id": "coal" }, "ticks": 1 }]""");
        Assert.Contains(e.Errors, x => x.Field == "skill");
        e = Fails("""[{ "type": "recipe", "id": "bad", "name": "Bad", "skill": "smithing", "inputs": [ { "id": "mithril", "count": 1 } ], "output": { "id": "coal" }, "ticks": 1 }]""");
        Assert.Contains(e.Errors, x => x.Field == "inputs[0].id" && x.Message.Contains("mithril"));
    }

    [Fact]
    public void Activities_need_a_gathering_skill_a_positive_rate_and_real_output()
    {
        var e = Fails("""[{ "type": "gathering_activity", "id": "bad", "name": "Bad", "skill": "smithing", "rate": 0, "xp": 1, "output": "coal" }]""");
        Assert.Contains(e.Errors, x => x.Field == "skill");
        Assert.Contains(e.Errors, x => x.Field == "rate");
        e = Fails("""[{ "type": "gathering_activity", "id": "bad", "name": "Bad", "skill": "mining", "rate": 1, "xp": 1, "output": "silver" }]""");
        Assert.Contains(e.Errors, x => x.Field == "output");
    }
}
