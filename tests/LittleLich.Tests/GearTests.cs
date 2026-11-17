using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Core.Saves;
using LittleLich.Crypt;
using LittleLich.Crypt.Buildings;
using LittleLich.Gear;
using LittleLich.Skills;
using Xunit;

namespace LittleLich.Tests;

public class EquipmentTests
{
    private static ContentDatabase ContentWith(string json) => ContentLoader.CreateDefault().Load(
        ContentSource.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Data")).Append(new ContentSource("extra.json", json)));

    [Fact]
    public void Equipping_a_weapon_changes_a_derived_stat_and_unequipping_restores_it()
    {
        var w = new World();
        w.Inventory.Add("bone_staff", 1);
        Assert.Equal(new Num(10), w.Lich.MaxInsight);

        Assert.Equal(EquipResult.Ok, w.Equipment.Equip(LichSlots.Staff, "bone_staff"));
        Assert.Equal(new Num(12), w.Lich.MaxInsight);
        Assert.Equal("bone_staff", w.Equipment.Worn(LichSlots.Staff));
        Assert.Equal(0, w.Inventory.Get("bone_staff"));

        Assert.True(w.Equipment.Unequip(LichSlots.Staff));
        Assert.Equal(new Num(10), w.Lich.MaxInsight);
        Assert.Null(w.Equipment.Worn(LichSlots.Staff));
        Assert.Equal(1, w.Inventory.Get("bone_staff"));
        Assert.DoesNotContain(w.Lich.Stats.Modifiers, m => m.Source.StartsWith("gear:"));
        Assert.False(w.Equipment.Unequip(LichSlots.Staff));
    }

    [Fact]
    public void Gear_with_add_and_mul_modifiers_stacks_with_other_sources()
    {
        var w = new World();
        w.Inventory.Add("rotting_robe", 1);
        w.Inventory.Add("grave_hood", 1);
        w.Equipment.Equip(LichSlots.Robe, "rotting_robe");
        w.Equipment.Equip(LichSlots.Hood, "grave_hood");
        w.Health.DebugSetLevel(3); // +20
        Assert.Equal(new Num((100 + 25 + 15 + 20) * 1.1), w.Lich.MaxHealth);
        Assert.Equal(new Num(100), w.Lich.Health); // no regen: gear does not heal
    }

    [Fact]
    public void Only_items_whose_definition_allows_the_slot_can_be_equipped()
    {
        var w = new World();
        w.Inventory.Add("bone_staff", 1);
        w.Inventory.Add("copper_sword", 1);
        w.Inventory.Add("rot_bomb", 1);
        Assert.Equal(EquipResult.WrongSlot, w.Equipment.Equip(LichSlots.Hood, "bone_staff"));
        Assert.Equal(EquipResult.NotEquippable, w.Equipment.Equip(LichSlots.Staff, "copper_sword")); // unit gear
        Assert.Equal(EquipResult.NotEquippable, w.Equipment.Equip(LichSlots.Staff, "rot_bomb"));
        Assert.Equal(EquipResult.UnknownSlot, w.Equipment.Equip("tail", "bone_staff"));
        Assert.Equal(EquipResult.NotInInventory, w.Equipment.Equip(LichSlots.Grimoire, "dusty_grimoire"));
        Assert.Equal(1, w.Inventory.Get("bone_staff"));
        Assert.Empty(w.Lich.Stats.Modifiers);
    }

    [Fact]
    public void Swapping_returns_the_old_item_and_replaces_only_that_slots_modifiers()
    {
        var w = new World(ContentWith("""
            [{ "type": "weapon", "id": "iron_staff", "name": "Iron Staff", "slots": ["staff"],
               "modifiers": [ { "stat": "max_insight", "op": "add", "value": 7 } ] }]
            """));
        w.Inventory.Add("bone_staff", 1);
        w.Inventory.Add("iron_staff", 1);
        w.Inventory.Add("dusty_grimoire", 1);
        w.Equipment.Equip(LichSlots.Grimoire, "dusty_grimoire");
        w.Equipment.Equip(LichSlots.Staff, "bone_staff");
        Assert.Equal(new Num(15), w.Lich.MaxInsight);
        Assert.Equal(EquipResult.Ok, w.Equipment.Equip(LichSlots.Staff, "iron_staff"));
        Assert.Equal(new Num(20), w.Lich.MaxInsight);
        Assert.Equal(1, w.Inventory.Get("bone_staff"));
        Assert.Equal(0, w.Inventory.Get("iron_staff"));
        Assert.Single(w.Lich.Stats.Modifiers, m => m.Source == LichSlots.Source(LichSlots.Staff));
    }

    [Fact]
    public void EquipAuto_picks_the_items_slot()
    {
        var w = new World();
        w.Inventory.Add("amulet_of_ash", 1);
        Assert.Equal(EquipResult.Ok, w.Equipment.EquipAuto("amulet_of_ash"));
        Assert.Equal("amulet_of_ash", w.Equipment.Worn(LichSlots.Amulet));
        Assert.Equal(15, w.Lich.Pool.Max);
    }

    [Fact]
    public void Unequip_fails_when_the_inventory_is_full_and_keeps_the_item_worn()
    {
        var w = new World();
        w.Inventory.Add("bone_staff", 1);
        w.Equipment.Equip(LichSlots.Staff, "bone_staff");
        w.Inventory.SlotCapacity = 1;
        w.Inventory.Add("rot_bomb", 100);                    // one full stack fills the only slot
        Assert.False(w.Equipment.Unequip(LichSlots.Staff));
        Assert.Equal("bone_staff", w.Equipment.Worn(LichSlots.Staff));
        Assert.Equal(new Num(12), w.Lich.MaxInsight);
    }

    [Fact]
    public void Swapping_into_a_full_inventory_is_refused_and_changes_nothing()
    {
        var w = new World(ContentWith("""[{ "type": "weapon", "id": "iron_staff", "name": "Iron Staff", "slots": ["staff"], "stack_size": 10 }]"""));
        w.Inventory.Add("bone_staff", 1);
        w.Equipment.Equip(LichSlots.Staff, "bone_staff");
        w.Inventory.Add("iron_staff", 2);                    // one slot; taking one out does not free it
        w.Inventory.Add("rot_bomb", 100);                    // a second, full slot
        w.Inventory.SlotCapacity = 2;                        // no room for the bone staff to come back
        Assert.Equal(EquipResult.InventoryFull, w.Equipment.Equip(LichSlots.Staff, "iron_staff"));
        Assert.Equal("bone_staff", w.Equipment.Worn(LichSlots.Staff));
        Assert.Equal(2, w.Inventory.Get("iron_staff"));
        Assert.Equal(0, w.Inventory.Get("bone_staff"));
        Assert.Equal(new Num(12), w.Lich.MaxInsight);
    }
}

public class InventoryTests
{
    [Fact]
    public void Space_is_counted_in_stacks()
    {
        var inv = new Inventory(World.SampleContent) { SlotCapacity = 3 };
        Assert.Equal(300, inv.Room("rot_bomb"));               // stack size 100
        Assert.Equal(150, inv.Add("rot_bomb", 150));            // 2 slots
        Assert.Equal(2, inv.SlotsUsed);
        Assert.Equal(150, inv.Room("rot_bomb"));               // rest of stack 2 + 1 free slot
        Assert.Equal(10, inv.Room("bone_staff"));              // 1 free slot x stack 10
        Assert.Equal(10, inv.Add("bone_staff", 25));
        Assert.Equal(0, inv.FreeSlots);
        Assert.Equal(50, inv.Add("rot_bomb", 999));            // tops up the partial stack only
        Assert.True(inv.TryRemove("rot_bomb", 200));
        Assert.Equal(1, inv.SlotsUsed);
    }

    [Fact]
    public void Crafting_into_a_full_inventory_pauses_as_output_full()
    {
        var w = new World();
        w.Lich.DebugAddNecromancers(5);
        w.Smithing.DebugSetLevel(5);
        w.Stock.Add("copper_bar", 300);
        w.Inventory.SlotCapacity = 2;                // one slot for the bars (a material item now), one for swords
        w.Inventory.Add("copper_sword", 49);                   // stack size 50: one more fits
        w.Smithing.AddNecromancer("copper_sword", 5);
        w.Ticks.RunTicks(10);
        Assert.Equal(50, w.Inventory.Get("copper_sword"));
        Assert.Equal(RecipeStatus.OutputFull, w.Smithing.Status("copper_sword"));
    }

    [Fact]
    public void Magical_storage_sets_and_raises_inventory_space()
    {
        var w = new World();
        var st = w.Buildings.OfType<MagicalStorage>().Single();
        st.Load(new SaveData());
        Assert.Equal(40, w.Inventory.SlotCapacity);
        st.DebugSetLevel(4);
        Assert.Equal(70, w.Inventory.SlotCapacity);
    }
}

public class ItemUseTests
{
    [Fact]
    public void Healing_potions_heal_up_to_max()
    {
        var w = new World();
        w.Inventory.Add("mend_flesh_potion", 2);
        w.Lich.Damage(70);
        Assert.Equal(UseResult.Ok, w.Items.Use("mend_flesh_potion"));
        Assert.Equal(new Num(80), w.Lich.Health);
        w.Items.Use("mend_flesh_potion");
        Assert.Equal(new Num(100), w.Lich.Health);
        Assert.Equal(UseResult.NoneLeft, w.Items.Use("mend_flesh_potion"));
    }

    [Fact]
    public void Buff_potions_add_timed_modifiers_that_refresh_instead_of_stacking()
    {
        var w = new World();
        w.Inventory.Add("clarity_draught", 2);
        w.Items.Use("clarity_draught");
        Assert.Equal(new Num(15), w.Lich.MaxInsight);
        Assert.Equal(1.25, w.Mining.SpeedMultiplier, 9);
        w.Ticks.RunTicks(200);
        w.Items.Use("clarity_draught");                         // refresh: 300 more ticks from now
        Assert.Equal(new Num(15), w.Lich.MaxInsight);
        Assert.Equal(2, w.Lich.Stats.Modifiers.Count(m => m.Source == "potion:clarity_draught"));
        w.Ticks.RunTicks(299);
        Assert.Equal(new Num(15), w.Lich.MaxInsight);
        w.Ticks.RunTicks(1);
        Assert.Equal(new Num(10), w.Lich.MaxInsight);
        Assert.Empty(w.Lich.Stats.Modifiers);
    }

    [Fact]
    public void Only_potions_can_be_used()
    {
        var w = new World();
        w.Inventory.Add("rot_bomb", 1);
        Assert.Equal(UseResult.NotUsable, w.Items.Use("rot_bomb"));
        Assert.Equal(UseResult.NotUsable, w.Items.Use("coal"));
        Assert.Equal(1, w.Inventory.Get("rot_bomb"));
    }
}

public class GearSaveTests : IDisposable
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
    public void Inventory_and_equipment_survive_save_and_reload()
    {
        var (s1, a) = Session();
        s1.NewGame(1);
        a.Inventory.Add("rotting_robe", 2);
        a.Inventory.Add("bone_staff", 1);
        a.Inventory.Add("rot_bomb", 37);
        a.Inventory.Add("clarity_draught", 3);
        a.Equipment.Equip(LichSlots.Robe, "rotting_robe");
        a.Equipment.Equip(LichSlots.Staff, "bone_staff");
        a.Items.Use("clarity_draught");
        a.Lich.Heal(1_000);                                     // 137.5 with the robe
        a.Ticks.RunTicks(10);
        s1.Save();

        var (s2, b) = Session();
        s2.LoadSlot(1);
        Assert.Equal(a.Inventory.Stacks, b.Inventory.Stacks);
        Assert.Equal(1, b.Inventory.Get("rotting_robe"));
        Assert.Equal(37, b.Inventory.Get("rot_bomb"));
        Assert.Equal("rotting_robe", b.Equipment.Worn(LichSlots.Robe));
        Assert.Equal("bone_staff", b.Equipment.Worn(LichSlots.Staff));
        Assert.Equal(a.Lich.Stats.Modifiers.OrderBy(m => m.Source).ThenBy(m => m.StatId),
                     b.Lich.Stats.Modifiers.OrderBy(m => m.Source).ThenBy(m => m.StatId));
        Assert.Equal(new Num(137.5), b.Lich.Health);           // not clamped while loading
        Assert.Equal(new Num(17), b.Lich.MaxInsight);          // 10 + 2 staff + 5 potion
        Assert.Equal(40, b.Inventory.SlotCapacity);
    }

    [Fact]
    public void Load_rebuilds_gear_modifiers_from_definitions()
    {
        var w = new World();
        // A save whose lich section still carries a stale gear modifier, and whose equipment puts a staff on the hood.
        w.Lich.Load(new SaveData(JsonNode.Parse("""
            { "modifiers": [ { "stat_id": "max_health", "op": "add", "value": 999, "source": "gear:boots" } ] }
            """)!.AsObject()));
        Assert.Equal(new Num(1_099), w.Lich.MaxHealth);
        w.Equipment.Load(new SaveData(JsonNode.Parse("""{ "worn": { "hood": "bone_staff", "staff": "bone_staff", "ring": "no_such_ring" } }""")!.AsObject()));
        Assert.Equal(new Num(100), w.Lich.MaxHealth);           // stale boots modifier gone
        Assert.Equal("bone_staff", w.Equipment.Worn(LichSlots.Staff));
        Assert.Null(w.Equipment.Worn(LichSlots.Hood));
        Assert.Equal(1, w.Inventory.Get("bone_staff"));         // the misfit went back to the inventory
        Assert.Null(w.Equipment.Worn(LichSlots.Ring));
    }
}

public class ItemContentTests
{
    private static ContentLoadException Fails(string json) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.CreateDefault().Load(
            ContentSource.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Data")).Append(new ContentSource("x.json", json))));

    [Fact]
    public void Every_item_type_loads_as_its_own_class()
    {
        var db = World.SampleContent;
        Assert.IsType<WeaponDefinition>(db.Get<ItemDefinitionBase>("bone_staff"));
        Assert.IsType<ArmourDefinition>(db.Get<ItemDefinitionBase>("grave_hood"));
        Assert.IsType<AccessoryDefinition>(db.Get<ItemDefinitionBase>("ring_of_whispers"));
        Assert.IsType<PotionDefinition>(db.Get<ItemDefinitionBase>("clarity_draught"));
        Assert.IsType<BombDefinition>(db.Get<ItemDefinitionBase>("rot_bomb"));
        Assert.IsType<RuneDefinition>(db.Get<ItemDefinitionBase>("rune_of_rot"));
        Assert.Equal("potion", db.Get<ItemDefinitionBase>("clarity_draught").Kind);
        foreach (var slot in LichSlots.All)
            Assert.Contains(db.All<EquippableDefinitionBase>(), d => d.Slots.Contains(slot));
    }

    [Fact]
    public void Bad_slots_modifiers_and_potions_are_rejected()
    {
        Assert.Contains(Fails("""[{ "type": "armour", "id": "x", "name": "X", "slots": ["tail"] }]""").Errors, e => e.Field == "slots");
        Assert.Contains(Fails("""[{ "type": "weapon", "id": "x", "name": "X", "modifiers": [ { "stat": "max_health", "op": "add", "value": 1 } ] }]""").Errors, e => e.Field == "modifiers");
        Assert.Contains(Fails("""[{ "type": "accessory", "id": "x", "name": "X", "slots": ["ring"], "modifiers": [ { "stat": "max_health", "op": "pow", "value": 1 } ] }]""").Errors, e => e.Field == "modifiers[0].op");
        Assert.Contains(Fails("""[{ "type": "potion", "id": "x", "name": "X" }]""").Errors, e => e.Field == "effects");
        Assert.Contains(Fails("""[{ "type": "potion", "id": "x", "name": "X", "effects": [ { "stat": "max_health", "value": 1 } ] }]""").Errors, e => e.Field == "duration_ticks");
    }
}
