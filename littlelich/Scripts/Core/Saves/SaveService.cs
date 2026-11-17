using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LittleLich.Core.Saves;

/// <summary>What the slot list shows for one save slot.</summary>
public sealed record SaveSlotInfo(int Slot, DateTime? SavedAtUtc, int? Version, bool Readable, bool MainCorrupt, bool HasBackup);

/// <summary>Result of loading a slot.</summary>
public sealed record SaveLoadResult(SaveFile File, bool FromBackup, string? MainError);

/// <summary>Neither the save nor its backup could be read.</summary>
public sealed class SaveLoadException : Exception
{
    public SaveLoadException(string message) : base(message) { }
}

/// <summary>
/// Reads and writes save slots in one folder (Godot: user://saves).
///
/// Writes are crash-safe: the new save goes to slot_N.json.tmp, is flushed to disk, then renamed over
/// slot_N.json. The previous good save is kept as slot_N.json.bak. Loads fall back to the backup when
/// the main file is corrupt. Plain .NET IO only, so it runs in tests without Godot.
/// </summary>
public sealed class SaveService
{
    private static readonly Regex SlotFile = new(@"^slot_(\d+)\.json$", RegexOptions.Compiled);

    private readonly Func<DateTime> _utcNow;

    public SaveService(string directory, Func<DateTime>? utcNow = null)
    {
        Directory = directory;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public string Directory { get; }

    public string SlotPath(int slot) => Path.Combine(Directory, $"slot_{slot}.json");
    public string BackupPath(int slot) => SlotPath(slot) + ".bak";
    public string TempPath(int slot) => SlotPath(slot) + ".tmp";

    public bool Exists(int slot) => File.Exists(SlotPath(slot)) || File.Exists(BackupPath(slot));

    /// <summary>Every slot with a save or a backup, by slot number.</summary>
    public IReadOnlyList<SaveSlotInfo> ListSlots()
    {
        if (!System.IO.Directory.Exists(Directory)) return Array.Empty<SaveSlotInfo>();

        var slots = new SortedSet<int>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory))
        {
            var name = Path.GetFileName(path);
            if (name.EndsWith(".bak", StringComparison.Ordinal)) name = name[..^4];
            var m = SlotFile.Match(name);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > 0) slots.Add(n);
        }
        return slots.Select(Describe).ToList();
    }

    private SaveSlotInfo Describe(int slot)
    {
        bool hasBackup = File.Exists(BackupPath(slot));
        if (TryReadHeader(SlotPath(slot), out var h))
            return new SaveSlotInfo(slot, h.SavedAtUtc, h.Version, true, false, hasBackup);
        bool mainCorrupt = File.Exists(SlotPath(slot));
        if (TryReadHeader(BackupPath(slot), out var b))
            return new SaveSlotInfo(slot, b.SavedAtUtc, b.Version, true, mainCorrupt, hasBackup);
        return new SaveSlotInfo(slot, null, null, false, mainCorrupt, hasBackup);
    }

    /// <summary>Deletes a slot's save, backup and any leftover temp file. Returns true if anything was deleted.</summary>
    public bool Delete(int slot)
    {
        var any = false;
        foreach (var path in new[] { SlotPath(slot), BackupPath(slot), TempPath(slot) })
        {
            if (!File.Exists(path)) continue;
            File.Delete(path);
            any = true;
        }
        return any;
    }

    /// <summary>The lowest slot number with nothing in it.</summary>
    public int NextFreeSlot()
    {
        int n = 1;
        while (Exists(n)) n++;
        return n;
    }

    /// <summary>
    /// Writes <paramref name="file"/> to <paramref name="slot"/>, stamping version and time.
    /// The previous save becomes the backup, unless it is itself unreadable (then the old backup is kept).
    /// </summary>
    public void Write(int slot, SaveFile file)
    {
        if (slot < 1) throw new ArgumentOutOfRangeException(nameof(slot));
        System.IO.Directory.CreateDirectory(Directory);

        file.Version = SaveMigrator.CurrentVersion;
        file.SavedAtUtc = _utcNow();

        string main = SlotPath(slot), tmp = TempPath(slot), bak = BackupPath(slot);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(file.ToJsonString());

        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(flushToDisk: true);
        }

        if (File.Exists(main) && TryReadHeader(main, out _))
        {
            try
            {
                File.Replace(tmp, main, bak, ignoreMetadataErrors: true);
                return;
            }
            catch (Exception e) when (e is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
            {
                // Some file systems do not support File.Replace; fall back to copy + move.
                File.Copy(main, bak, overwrite: true);
            }
        }
        File.Move(tmp, main, overwrite: true);
    }

    /// <summary>
    /// Loads a slot, migrating it to the current version. Falls back to the backup if the main file is
    /// missing or corrupt. Throws <see cref="SaveLoadException"/> if neither can be read.
    /// </summary>
    public SaveLoadResult Load(int slot)
    {
        string? mainError;
        try
        {
            return new SaveLoadResult(ReadAndMigrate(SlotPath(slot)), false, null);
        }
        catch (Exception e) when (e is SaveFormatException or IOException)
        {
            mainError = e.Message;
        }

        try
        {
            return new SaveLoadResult(ReadAndMigrate(BackupPath(slot)), true, mainError);
        }
        catch (Exception e) when (e is SaveFormatException or IOException)
        {
            throw new SaveLoadException($"slot {slot} cannot be loaded. Save: {mainError}. Backup: {e.Message}.");
        }
    }

    private static SaveFile ReadAndMigrate(string path)
    {
        if (!File.Exists(path)) throw new SaveFormatException("file not found");
        var root = SaveFile.ParseRoot(File.ReadAllText(path));
        var (version, _) = SaveFile.ReadHeader(root);
        SaveMigrator.Migrate(new SaveData(root), version);
        return SaveFile.FromJson(root);
    }

    private static bool TryReadHeader(string path, out (int Version, DateTime SavedAtUtc) header)
    {
        header = default;
        try
        {
            if (!File.Exists(path)) return false;
            var root = SaveFile.ParseRoot(File.ReadAllText(path));
            header = SaveFile.ReadHeader(root);
            if (header.Version > SaveMigrator.CurrentVersion) return false;
            // Also verify the body parses so a half-written file is never reported as readable.
            SaveFile.FromJson(root);
            return true;
        }
        catch (Exception e) when (e is SaveFormatException or IOException)
        {
            return false;
        }
    }
}
