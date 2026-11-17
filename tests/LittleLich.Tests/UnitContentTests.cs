using System;
using System.IO;
using System.Linq;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Crypt.Units;
using LittleLich.World;
using Xunit;

namespace LittleLich.Tests;

public class UnitContentTests
{
    private static ContentDatabase ContentWith(string json, string file = "units/extra.json") => ContentLoader.CreateDefault().Load(
        ContentSource.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Data")).Append(new ContentSource(file, json)));

    private static ContentLoadException Fails(string json) => Assert.Throws<ContentLoadException>(() => ContentWith(json));

    [Fact]
    public void Units_load_from_the_units_folder_as_their_own_classes()
    {
        var db = World.SampleContent;
        Assert.IsType<UndeadUnitDefinition>(db.Get<UnitDefinitionBase>("skeleton"));
        Assert.IsType<WildUnitDefinition>(db.Get<UnitDefinitionBase>("wild_brute"));
        Assert.IsType<StructureUnitDefinition>(db.Get<UnitDefinitionBase>("graveyard_gate"));
        Assert.Equal("units/bases.json", db.Get<UnitDefinitionBase>("skeleton").SourceFile);
        Assert.Equal(new[] { "strike", "brace" }, db.Get<UnitDefinitionBase>("skeleton").ResolvedActions.Select(a => a.Id));
        var players = db.All<UnitDefinitionBase>().Where(u => u.IsPlayer).Select(u => u.Id).ToList();
        Assert.InRange(players.Count, 3, 4);
        Assert.Contains("bone_archer", players);
        Assert.All(db.All<UnitDefinitionBase>().Where(u => u.IsPlayer), u => Assert.NotNull(u.Unlock?.Research));
    }

    [Fact]
    public void A_new_unit_in_json_appears_in_the_unit_builder_and_fights_with_no_code_change()
    {
        var db = ContentWith("""
            [ { "type": "battle_action", "id": "claw", "name": "Claw", "cooldown_seconds": 1.5, "effect": "damage", "value": 4, "target": "front", "range": "melee" },
              { "type": "undead", "id": "ghoul", "name": "Ghoul", "side": "player", "role": "flanker", "hp": 30,
                "actions": [ "claw", "brace" ], "weapon_slots": [ "weapon" ], "armour_slots": [], "mana_cost": 9 },
              { "type": "boss", "id": "bone_king", "name": "Bone King", "side": "enemy", "hp": 400, "actions": [ "smash", "hex" ] } ]
            """);
        var stock = new LittleLich.Crypt.Stockpile(new LittleLich.Crypt.Storage(db), new LittleLich.Crypt.Inventory(db));
        var units = new UnitBuilder(db, stock, Unlocks.None);
        Assert.Contains(units.Bases, b => b.Id == "ghoul");                         // no unlock: open from the start
        var (r, t) = units.Build("ghoul", "copper_sword");
        Assert.Equal(BuildResult.Ok, r);
        Assert.Equal(BuildResult.NoArmourSlot, units.Build("ghoul", null, "iron_armour").Result);
        Assert.Equal(new[] { 8.0, 0.1 }, units.BattleUnit(t!.Id).Actions.Select(a => a.Value));   // claw 4 + sword 4; brace unchanged

        Assert.IsType<BossUnitDefinition>(db.Get<UnitDefinitionBase>("bone_king"));
        var b = new BattleController(new[] { ("front", units.BattleUnit(t.Id), 40L) },
                                     new[] { ("front", BattleUnit.From(db.Get<UnitDefinitionBase>("bone_king")), 1L) });
        var fired = 0;
        b.ActionFired += _ => fired++;
        b.Advance(120);
        Assert.Equal(BattleOutcome.Victory, b.Outcome);
        Assert.True(fired > 0);
    }

    [Fact]
    public void A_broken_entry_fails_with_a_readable_message_naming_file_id_and_field()
    {
        var e = Fails("""[ { "type": "undead", "id": "ghoul", "name": "Ghoul", "side": "player", "hp": 30, "actions": [ "claw" ] } ]""");
        var err = Assert.Single(e.Errors);
        Assert.Equal("units/extra.json [ghoul] .actions: unknown action id 'claw'", err.ToString());
        Assert.Contains("units/extra.json", e.Message);
    }

    [Fact]
    public void Missing_unlocks_negative_values_and_side_rules_are_rejected()
    {
        var e = Fails("""[ { "type": "undead", "id": "ghoul", "name": "Ghoul", "side": "player", "hp": 30, "unlock": { "research": "nec2_ghouls" } } ]""");
        Assert.Contains(e.Errors, x => x.Field == "unlock.research" && x.Message.Contains("nec2_ghouls"));
        e = Fails("""[ { "type": "undead", "id": "ghoul", "name": "Ghoul", "side": "player", "hp": -3, "mana_cost": -1, "summon_work": 0 } ]""");
        Assert.Contains(e.Errors, x => x.Field == "hp");
        Assert.Contains(e.Errors, x => x.Field == "mana_cost");
        Assert.Contains(e.Errors, x => x.Field == "summon_work");
        e = Fails("""[ { "type": "wild", "id": "bandit", "name": "Bandit", "side": "enemy", "hp": 10, "mana_cost": 5, "weapon_slots": [ "weapon" ] } ]""");
        Assert.Contains(e.Errors, x => x.Field == "side");
        e = Fails("""[ { "type": "structure", "id": "wall", "name": "Wall", "side": "enemy", "hp": 10, "actions": [ "strike" ] } ]""");
        Assert.Contains(e.Errors, x => x.Field == "actions" && x.Message.Contains("no actions"));
        e = Fails("""[ { "type": "research_node", "tome": "necromancy_1", "id": "x", "name": "X", "requires": [ "nec1_grave_lore" ], "effects": [ { "type": "unlock_unit", "id": "wild_brute" } ] } ]""");
        Assert.Contains(e.Errors, x => x.Field == "effects[0].id" && x.Message.Contains("no player unit"));
    }

    [Fact]
    public void Bone_archers_unlock_from_their_own_research_node()
    {
        var w = new World();
        foreach (var s in w.Saveables) s.Load(new LittleLich.Core.SaveData());
        Assert.True(w.Units.IsBaseLocked("bone_archer"));
        w.Research.DebugComplete("nec1_grave_lore");
        w.Research.DebugComplete("nec1_raise_skeletons");
        Assert.False(w.Units.IsBaseLocked("skeleton"));
        Assert.True(w.Units.IsBaseLocked("bone_archer"));
        w.Research.DebugComplete("nec1_bone_archers");
        Assert.False(w.Units.IsBaseLocked("bone_archer"));
        Assert.Equal(BuildResult.Ok, w.Units.Build("bone_archer").Result);
    }
}
