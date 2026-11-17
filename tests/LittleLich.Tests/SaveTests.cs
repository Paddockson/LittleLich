using System;
using System.Collections.Generic;
using System.IO;
using LittleLich.Content;
using LittleLich.Core;
using LittleLich.Core.Saves;
using Xunit;

namespace LittleLich.Tests;

/// <summary>Stand-in for a manager's state (LichManager etc. arrive in later phases).</summary>
internal sealed class FakeManager : ISaveable
{
    public string SaveKey => "fake";
    public long Necromancers;
    public Num Xp;
    public Dictionary<string, long> Stock = new();

    public SaveData Save() => new SaveData().Set("necromancers", Necromancers).Set("xp", Xp).Set("stock", Stock);

    public void Load(SaveData d)
    {
        Necromancers = d.Get("necromancers", 0L);
        Xp = d.Get("xp", Num.Zero);
        Stock = d.Get("stock", new Dictionary<string, long>());
    }
}

public class SaveTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeClock _clock = new();

    public void Dispose() => _dir.Dispose();

    private SaveService Service() => new(_dir.Path, _clock.Get);

    private (GameSession session, FakeManager fake) NewSession()
    {
        var session = new GameSession(ContentDatabase.Empty, Service(), _clock.Get);
        var fake = new FakeManager();
        session.Register(fake);
        return (session, fake);
    }

    [Fact]
    public void No_saves_creates_new_game_in_slot_1_and_writes_it()
    {
        var (s, _) = NewSession();
        Assert.Equal(SessionStart.NewGame, s.StartFromSaves());
        Assert.Equal(1, s.CurrentSlot);
        Assert.True(File.Exists(Path.Combine(_dir.Path, "slot_1.json")));
    }

    [Fact]
    public void Boot_save_quit_reload_round_trip()
    {
        var (s1, f1) = NewSession();
        s1.StartFromSaves();
        f1.Necromancers = 100_000_000_000L;
        f1.Xp = Num.Pow(1.1, 998);
        f1.Stock["iron_ore"] = 42;
        s1.Save();

        _clock.Advance(TimeSpan.FromMinutes(10));

        var (s2, f2) = NewSession();
        Assert.Equal(SessionStart.Loaded, s2.StartFromSaves());
        Assert.Equal(100_000_000_000L, f2.Necromancers);
        Assert.Equal(f1.Xp, f2.Xp);
        Assert.Equal(42, f2.Stock["iron_ore"]);
        Assert.Equal(s1.Meta.CreatedAtUtc, s2.Meta.CreatedAtUtc);
        Assert.Equal(600, s2.PendingOfflineSeconds, precision: 3);
        Assert.Equal(600, s2.TakePendingOfflineSeconds(), precision: 3);
        Assert.Equal(0, s2.PendingOfflineSeconds);
    }

    [Fact]
    public void Save_file_has_version_and_time_at_top_and_holds_ids_and_counts()
    {
        var (s, f) = NewSession();
        s.StartFromSaves();
        f.Stock["iron_ore"] = 7;
        s.Save();

        var root = SaveFile.ParseRoot(File.ReadAllText(Service().SlotPath(1)));
        var (version, savedAt) = SaveFile.ReadHeader(root);
        Assert.Equal(SaveMigrator.CurrentVersion, version);
        Assert.Equal(_clock.Now, savedAt);
        Assert.Equal(7, (long)root["sections"]!["fake"]!["stock"]!["iron_ore"]!);
    }

    [Fact]
    public void Write_leaves_no_temp_file_and_keeps_backup_of_previous_save()
    {
        var svc = Service();
        var a = new SaveFile();
        a.Sections["fake"] = new SaveData().Set("n", 1);
        svc.Write(1, a);
        Assert.False(File.Exists(svc.BackupPath(1)));

        var b = new SaveFile();
        b.Sections["fake"] = new SaveData().Set("n", 2);
        svc.Write(1, b);

        Assert.False(File.Exists(svc.TempPath(1)));
        Assert.True(File.Exists(svc.BackupPath(1)));
        Assert.Equal(2, svc.Load(1).File.Section("fake").Get<int>("n"));
        Assert.Contains("\"n\": 1", File.ReadAllText(svc.BackupPath(1)));
    }

    [Theory]
    [InlineData("{ \"version\": 1, \"saved_at_utc\": \"2026-11-17T12:00:00Z\", \"sections\": { \"fake\": { \"necro")] // truncated
    [InlineData("")]
    [InlineData("\0\0\0\0")]
    [InlineData("[]")]
    [InlineData("{ \"sections\": {} }")] // no header
    public void Corrupt_save_falls_back_to_backup(string corrupt)
    {
        var (s1, f1) = NewSession();
        s1.StartFromSaves();
        f1.Necromancers = 5;
        s1.Save();          // main = 5, backup = the new-game save
        f1.Necromancers = 9;
        s1.Save();          // main = 9, backup = 5

        File.WriteAllText(Service().SlotPath(1), corrupt);

        var (s2, f2) = NewSession();
        Assert.Equal(SessionStart.LoadedFromBackup, s2.StartFromSaves());
        Assert.Equal(5, f2.Necromancers);
        Assert.NotNull(s2.LastLoadWarning);

        // The slot was repaired: main is readable again.
        Assert.False(Service().Load(1).FromBackup);
    }

    [Fact]
    public void Saving_over_a_corrupt_main_keeps_the_good_backup()
    {
        var svc = Service();
        var good = new SaveFile();
        good.Sections["fake"] = new SaveData().Set("n", 1);
        svc.Write(1, good);
        svc.Write(1, good); // backup = good
        File.WriteAllText(svc.SlotPath(1), "garbage");

        var next = new SaveFile();
        next.Sections["fake"] = new SaveData().Set("n", 2);
        svc.Write(1, next);

        Assert.Contains("\"n\": 1", File.ReadAllText(svc.BackupPath(1)));
        Assert.Equal(2, svc.Load(1).File.Section("fake").Get<int>("n"));
    }

    [Fact]
    public void Both_main_and_backup_corrupt_throws_and_startup_makes_a_new_slot()
    {
        var svc = Service();
        svc.Write(1, new SaveFile());
        svc.Write(1, new SaveFile());
        File.WriteAllText(svc.SlotPath(1), "x");
        File.WriteAllText(svc.BackupPath(1), "y");

        Assert.Throws<SaveLoadException>(() => svc.Load(1));
        var info = Assert.Single(svc.ListSlots());
        Assert.False(info.Readable);

        var (s, _) = NewSession();
        Assert.Equal(SessionStart.NewGame, s.StartFromSaves());
        Assert.Equal(2, s.CurrentSlot); // never overwrites the unreadable slot
    }

    [Fact]
    public void Slot_list_and_latest_slot_is_loaded()
    {
        var (s, f) = NewSession();
        s.NewGame(1);
        _clock.Advance(TimeSpan.FromHours(1));
        s.NewGame(3);
        f.Necromancers = 33;
        s.Save();
        _clock.Advance(TimeSpan.FromHours(1));

        var slots = Service().ListSlots();
        Assert.Equal(new[] { 1, 3 }, new[] { slots[0].Slot, slots[1].Slot });
        Assert.All(slots, x => Assert.True(x.Readable));
        Assert.Equal(2, Service().NextFreeSlot());

        var (s2, f2) = NewSession();
        s2.StartFromSaves();
        Assert.Equal(3, s2.CurrentSlot);
        Assert.Equal(33, f2.Necromancers);
    }

    [Fact]
    public void New_game_resets_registered_state()
    {
        var (s, f) = NewSession();
        s.NewGame(1);
        f.Necromancers = 10;
        s.NewGame(2);
        Assert.Equal(0, f.Necromancers);
    }

    [Fact]
    public void Missing_section_loads_defaults()
    {
        var svc = Service();
        svc.Write(1, new SaveFile()); // written before "fake" existed
        var (s, f) = NewSession();
        f.Necromancers = 99;
        s.LoadSlot(1);
        Assert.Equal(0, f.Necromancers);
    }

    [Fact]
    public void Duplicate_save_keys_are_rejected()
    {
        var (s, _) = NewSession();
        Assert.Throws<InvalidOperationException>(() => s.Register(new FakeManager()));
    }

    [Fact]
    public void Clock_moving_backwards_gives_zero_offline_time()
    {
        var (s1, _) = NewSession();
        s1.StartFromSaves();
        _clock.Advance(TimeSpan.FromHours(-5));
        var (s2, _) = NewSession();
        s2.StartFromSaves();
        Assert.Equal(0, s2.PendingOfflineSeconds);
    }

    [Fact]
    public void Autosave_after_interval_ticks_including_batches()
    {
        var (s, _) = NewSession();
        s.StartFromSaves();
        s.AutosaveIntervalTicks = 10;
        long before = s.Meta.SaveCount;

        for (int i = 0; i < 9; i++) Assert.False(s.NotifyTicksProcessed(1));
        Assert.True(s.NotifyTicksProcessed(1));
        Assert.True(s.NotifyTicksProcessed(500)); // one offline batch = one save
        Assert.Equal(before + 2, s.Meta.SaveCount);
        Assert.Equal(510, s.Meta.TotalTicks);
    }

    [Fact]
    public void Newer_save_version_is_refused()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "slot_1.json"),
            "{ \"version\": 999, \"saved_at_utc\": \"2026-11-17T12:00:00Z\", \"sections\": {} }");
        Assert.Throws<SaveLoadException>(() => Service().Load(1));
    }
}

public class MigrationTests
{
    [Fact]
    public void Steps_run_in_order_and_bump_version()
    {
        var data = new SaveData().Set("version", 1).Set("log", "");
        var steps = new Dictionary<int, Action<SaveData>>
        {
            [1] = d => d.Set("log", d.Get<string>("log") + "a"),
            [2] = d => d.Set("log", d.Get<string>("log") + "b"),
        };
        SaveMigrator.Migrate(data, 1, 3, steps);
        Assert.Equal("ab", data.Get<string>("log"));
        Assert.Equal(3, data.Get<int>("version"));
    }

    [Fact]
    public void Missing_step_fails_loudly()
    {
        var steps = new Dictionary<int, Action<SaveData>>();
        Assert.Throws<SaveFormatException>(() => SaveMigrator.Migrate(new SaveData(), 1, 2, steps));
    }

    [Fact]
    public void Current_version_needs_no_steps() =>
        SaveMigrator.Migrate(new SaveData(), SaveMigrator.CurrentVersion);
}

public class OfflineTimeTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1.99, 0)]
    [InlineData(2, 1)]
    [InlineData(3601, 1800)]
    public void Ticks_use_base_tick_length(double seconds, long ticks) =>
        Assert.Equal(ticks, OfflineTime.ToTicks(seconds));

    [Fact]
    public void Cap_is_applied() => Assert.Equal(100, OfflineTime.ToTicks(1e9, maxTicks: 100));
}
