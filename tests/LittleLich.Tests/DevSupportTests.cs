using System;
using System.Collections.Generic;
using System.IO;
using LittleLich.Content;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Core.Saves;
using Xunit;

namespace LittleLich.Tests;

/// <summary>Core additions made for the dev UI (phase 0): slot delete and assignment events.</summary>
public class DevSupportTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeClock _clock = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Delete_removes_save_backup_and_temp()
    {
        var saves = new SaveService(_dir.Path, _clock.Get);
        var session = new GameSession(ContentDatabase.Empty, saves, _clock.Get);
        session.NewGame(2);
        session.Save(); // creates the .bak
        File.WriteAllText(saves.TempPath(2), "junk");
        session.NewGame(1);

        Assert.True(session.DeleteSlot(2));
        Assert.False(File.Exists(saves.SlotPath(2)));
        Assert.False(File.Exists(saves.BackupPath(2)));
        Assert.False(File.Exists(saves.TempPath(2)));
        Assert.DoesNotContain(saves.ListSlots(), s => s.Slot == 2);
        Assert.False(session.DeleteSlot(2));
    }

    [Fact]
    public void The_current_slot_cannot_be_deleted()
    {
        var session = new GameSession(ContentDatabase.Empty, new SaveService(_dir.Path, _clock.Get), _clock.Get);
        session.NewGame(1);
        Assert.Throws<InvalidOperationException>(() => session.DeleteSlot(1));
        Assert.True(session.Saves.Exists(1));
    }

    [Fact]
    public void AssignmentChanged_reports_signed_deltas_after_state_is_updated()
    {
        var lich = new LichState();
        lich.DebugAddNecromancers(20);
        var seen = new List<string>();
        lich.AssignmentChanged += (o, a, d) => seen.Add($"{o}/{a} {d:+0;-0} assigned={lich.Pool.Assigned}");

        lich.TryAssign("mining", "iron_ore", 5);
        lich.TryAssign("mining", "copper_ore", 3);
        lich.Unassign("mining", "iron_ore", 2);
        lich.TryAssign("mining", "iron_ore", 0);
        lich.Pool.UnassignOwner("mining");

        Assert.Equal(new[]
        {
            "mining/iron_ore +5 assigned=5",
            "mining/copper_ore +3 assigned=8",
            "mining/iron_ore -2 assigned=6",
            "mining/copper_ore -3 assigned=0",
            "mining/iron_ore -3 assigned=0",
        }, seen);
    }

    [Fact]
    public void Load_does_not_raise_assignment_events()
    {
        var lich = new LichState();
        lich.DebugAddNecromancers(5);
        lich.TryAssign("a", "b", 2);
        var data = lich.Save();
        var other = new LichState();
        var count = 0;
        other.AssignmentChanged += (_, _, _) => count++;
        other.Load(data);
        Assert.Equal(0, count);
        Assert.Equal(2, other.GetAssigned("a", "b"));
    }
}
