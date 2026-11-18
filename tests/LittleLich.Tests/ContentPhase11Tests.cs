using System;
using System.IO;
using System.Linq;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Saves;
using LittleLich.Crypt.Buildings;
using Xunit;

namespace LittleLich.Tests;

public class ContentLayoutTests
{
    private static ContentLoadException Fails(string json, string file = "recipes/zz_test.json") =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.CreateDefault().Load(
            ContentSource.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Data")).Append(new ContentSource(file, json))));

    [Fact]
    public void Content_comes_from_the_phase_11_layout()
    {
        var db = World.SampleContent;
        Assert.Equal("recipes/smithing.json", db.Get<RecipeDefinitionBase>("iron_sword").SourceFile);
        Assert.Equal("items/weapons.json", db.Get<ItemDefinitionBase>("iron_sword").SourceFile);
        Assert.Equal("activities/mining.json", db.Get<ActivityDefinitionBase>("iron_ore").SourceFile);
        Assert.Equal("upgrades.json", db.Get<UpgradeDefinitionBase>("mana_well_level").SourceFile);
        foreach (var skill in SkillIds.Crafting)
            Assert.InRange(db.All<RecipeDefinition>().Count(r => r.Skill == skill), 8, 14);
        Assert.Equal(3, db.All<GatheringActivityDefinition>().Count(a => a.Skill == SkillIds.Mining));
        foreach (var skill in SkillIds.Gathering)
            Assert.Contains(db.All<GatheringActivityDefinition>(), a => a.Skill == skill);
    }

    [Fact]
    public void Compact_amounts_and_spec_field_names_load()
    {
        var db = World.SampleContent;
        var bar = db.Get<RecipeDefinitionBase>("iron_bar");
        Assert.Equal(new[] { "iron_ore:2", "coal:1" }, bar.Inputs.Select(i => $"{i.Id}:{i.Count}"));
        Assert.Equal("iron_bar", bar.Output.Id);
        Assert.Equal(10, bar.RequiredLevel);
        var seam = db.Get<GatheringActivityDefinition>("iron_ore");
        Assert.Equal(0.3, seam.Rate);
        Assert.Equal(12, seam.Xp);
    }

    [Fact]
    public void Every_shipped_entry_with_placeholder_numbers_is_marked()
    {
        var db = World.SampleContent;
        Assert.All(db.All<RecipeDefinitionBase>(), r => Assert.True(r.Placeholder, r.Id));
        Assert.All(db.All<ItemDefinitionBase>(), i => Assert.True(i.Placeholder, i.Id));
        Assert.All(db.All<ActivityDefinitionBase>(), a => Assert.True(a.Placeholder, a.Id));
        Assert.All(db.All<UpgradeDefinitionBase>(), u => Assert.True(u.Placeholder, u.Id));
    }

    [Fact]
    public void The_validator_catches_a_missing_reference_in_a_test_file()
    {
        var e = Fails("""[ { "type": "recipe", "id": "bad_sword", "name": "Bad Sword", "skill": "smithing", "inputs": { "mithril_bar": 3 }, "output": { "iron_sword": 1 } } ]""");
        var err = Assert.Single(e.Errors);
        Assert.Equal("recipes/zz_test.json [bad_sword] .inputs.mithril_bar: no resource or item with id 'mithril_bar'", err.ToString());
    }

    [Fact]
    public void Bad_amounts_and_upgrades_are_rejected()
    {
        Assert.Contains(Fails("""[ { "type": "recipe", "id": "x", "name": "X", "skill": "smithing", "inputs": { "coal": 1.5 }, "output": { "coal": 1 } } ]""").Errors,
            e => e.Message.Contains("whole number"));
        Assert.Contains(Fails("""[ { "type": "recipe", "id": "x", "name": "X", "skill": "smithing", "inputs": { "coal": 1 }, "output": { "coal": 1, "iron_bar": 1 } } ]""").Errors,
            e => e.Message.Contains("exactly one"));
        Assert.Contains(Fails("""[ { "type": "building_upgrade", "id": "x", "name": "X", "building": "castle", "cost": { "gold": 5 } } ]""", "upgrades_extra.json").Errors,
            e => e.Field == "building");
        Assert.Contains(Fails("""[ { "type": "building_upgrade", "id": "x", "name": "X", "building": "mana_well", "cost": { "platinum": 5 } } ]""", "upgrades_extra.json").Errors,
            e => e.Field == "cost.platinum");
    }

    [Fact]
    public void Every_building_needs_its_upgrade()
    {
        var sources = ContentSource.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Data"))
            .Select(s => s.Path == "upgrades.json"
                ? s with { Json = s.Json.Replace("""{ "type": "building_upgrade", "id": "infuser_level",""", """{ "type": "building_upgrade", "id": "infuser_level_off", "building": "mana_well",""").Replace("\"building\": \"infuser\",", "") }
                : s);
        var e = Assert.Throws<ContentLoadException>(() => ContentLoader.CreateDefault().Load(sources));
        Assert.Contains(e.Errors, x => x.Id == "infuser" && x.Message.Contains("no building_upgrade"));
    }
}

public class UpgradeTests
{
    private static World Fresh()
    {
        var w = new World();
        foreach (var s in w.Saveables) s.Load(new SaveData());
        w.OpenAll();                            // research gates buildings; these tests are about upgrades
        return w;
    }

    [Fact]
    public void Building_levels_and_costs_come_from_upgrades_json()
    {
        var w = Fresh();
        var well = w.Buildings.OfType<SoulWell>().Single();   // no level table: upgrades.json formula
        Assert.Equal(100, well.MaxLevel);
        Assert.Equal(new[] { "gold:100" }, well.UpgradeCosts.Select(c => $"{c.Id}:{c.Count}"));
        w.Storage.Add("gold", 200);
        Assert.True(well.TryUpgrade());
        Assert.Equal(new[] { "gold:150" }, well.UpgradeCosts.Select(c => $"{c.Id}:{c.Count}"));
        Assert.Equal(100, w.Storage.Get("gold"));
    }

    [Fact]
    public void The_bench_upgrade_costs_gold_and_mana_and_speeds_research()
    {
        // Research revamp: bench level costs come from the old build.json tiers (coin -> gold), 2 s.f. halves up.
        var w = Fresh();
        var r = w.Research;
        Assert.Equal(1, r.BenchLevel);
        Assert.Equal(new[] { "gold:120", "mana:320" }, r.BenchUpgradeCosts.Select(c => $"{c.Id}:{c.Count}"));   // 50 x 2.3 = 115, 150 x 2.1 = 315
        w.Storage.Add("gold", 120);
        Assert.False(r.TryUpgradeBench());                        // short of mana: nothing spent
        Assert.False(r.CanUpgradeBench(out var why));
        Assert.Equal("Not enough mana yet", why);
        Assert.Equal(120, w.Storage.Get("gold"));
        w.Storage.Add("mana", 320);
        Assert.True(r.TryUpgradeBench());
        Assert.Equal(2, r.BenchLevel);
        Assert.Equal(0, w.Storage.Get("gold"));
        Assert.Equal(0, w.Storage.Get("mana"));
        Assert.Equal(new[] { "gold:260", "mana:660" }, r.BenchUpgradeCosts.Select(c => $"{c.Id}:{c.Count}"));   // the mockup's 260 / 660

        w.Lich.DebugAddNecromancers(1);
        r.AddNecromancer(0, 1);
        Assert.Equal(1.65, r.WorkPerTick(r.Benches[0]), 9);      // (1 + 0.5 x 1) x 1.1, the mockup's 1.65 per tick
        var saved = r.Save();
        var v = Fresh();
        v.Research.Load(saved);
        Assert.Equal(2, v.Research.BenchLevel);
    }
}
