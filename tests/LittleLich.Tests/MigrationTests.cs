using System;
using System.IO;
using LittleLich.Core;
using LittleLich.Core.Saves;
using Xunit;

namespace LittleLich.Tests;

public class ResourceMigrationTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeClock _clock = new();
    public void Dispose() => _dir.Dispose();

    [Fact]
    public void A_v1_save_moves_materials_to_the_inventory_and_coins_to_gold()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "slot_1.json"), """
            { "version": 1, "saved_at_utc": "2026-11-17T10:00:00Z",
              "sections": {
                "storage":   { "stock": { "mana": 40, "coins": 300, "copper_ore": 120, "iron_bar": 7, "bones": 60, "infused_mana": 9 } },
                "inventory": { "stacks": { "bones": 5, "rot_bomb": 2 } } } }
            """);
        var w = new World();
        var s = new GameSession(World.SampleContent, new SaveService(_dir.Path, _clock.Get), _clock.Get);
        foreach (var x in w.Saveables) s.Register(x);
        s.LoadSlot(1);

        Assert.Equal(40, w.Storage.Get("mana"));
        Assert.Equal(300, w.Storage.Get("gold"));
        Assert.Equal(120, w.Inventory.Get("copper_ore"));
        Assert.Equal(7, w.Inventory.Get("iron_bar"));
        Assert.Equal(65, w.Inventory.Get("bones"));            // added to the stack already there
        Assert.Equal(2, w.Inventory.Get("rot_bomb"));
        s.Save();
        Assert.Equal(SaveMigrator.CurrentVersion, s.Saves.ListSlots()[0].Version);
        Assert.DoesNotContain("infused_mana", File.ReadAllText(Path.Combine(_dir.Path, "slot_1.json")));
        Assert.DoesNotContain("coins", File.ReadAllText(Path.Combine(_dir.Path, "slot_1.json")));
    }
}
