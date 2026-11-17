using System;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Managers;
using LittleLich.Skills;
using LittleLich.Content.Definitions;

namespace LittleLich.UI.Dev;

/// <summary>Thrown by a command for bad input; the message is shown in red.</summary>
public sealed class DevCommandException : Exception
{
    public DevCommandException(string message) : base(message) { }
}

/// <param name="Complete">Candidates for argument <c>index</c> (0 = first argument after the name), given the args typed so far.</param>
public sealed record DevCommand(
    string Name,
    string Usage,
    string Help,
    Func<string[], string?> Run,
    Func<int, string[], IEnumerable<string>>? Complete = null);

/// <summary>
/// Debug console commands. Each calls the same manager methods as the UI; none edits state directly.
/// Commands for systems that don't exist yet are listed and say which phase brings them.
/// </summary>
public static class DevCommands
{
    private static readonly Dictionary<string, DevCommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<DevCommand> All => _commands.Values.OrderBy(c => c.Name, StringComparer.Ordinal);

    static DevCommands()
    {
        // ---- General ----
        Add(new("help", "help [command]", "List commands, or show one command's usage.", a =>
        {
            if (a.Length > 0)
                return _commands.TryGetValue(a[0], out var c) ? $"{c.Usage}\n  {c.Help}" : throw new DevCommandException($"Unknown command '{a[0]}'.");
            return string.Join("\n", All.Select(c => $"{c.Usage,-38} {c.Help}"));
        }, (i, _) => i == 0 ? _commands.Keys : Empty));
        Add(new("clear", "clear", "Clear the log.", _ => { DevLog.Clear(); return null; }));

        // ---- Lich / necromancers (phase 2) ----
        Add(new("addnecro", "addnecro <n>", "Add n necromancers to the free pool (raises Max if needed).", a =>
        {
            var n = Count(a, 0);
            var added = Lich.DebugAddNecromancers(n);
            return $"Added {DevFormat.Exact(added)} necromancers. Free {DevFormat.Exact(Lich.State.Pool.Free)}.";
        }));
        Add(new("assign", "assign <owner> <activity> <n|max>", "Assign necromancers (through the skill when the owner is one, so locks apply).", a =>
        {
            Need(a, 3);
            var n = a[2].Equals("max", StringComparison.OrdinalIgnoreCase) ? long.MaxValue : Count(a, 2);
            var done = Assign(a[0], a[1], n, true);
            return $"Assigned {DevFormat.Exact(done)} to {a[0]}/{a[1]} (now {DevFormat.Exact(Lich.GetAssigned(a[0], a[1]))}).";
        }, CompleteAssignment));
        Add(new("unassign", "unassign <owner> <activity> <n|all>", "Free necromancers from an assignment.", a =>
        {
            Need(a, 3);
            var n = a[2].Equals("all", StringComparison.OrdinalIgnoreCase) ? long.MaxValue : Count(a, 2);
            var done = Assign(a[0], a[1], n, false);
            return $"Freed {DevFormat.Exact(done)} from {a[0]}/{a[1]} (now {DevFormat.Exact(Lich.GetAssigned(a[0], a[1]))}).";
        }, CompleteAssignment));
        Add(new("mod", "mod <stat> <add|mul> <value> [ticks]", "Add a modifier (source 'console'); with ticks it is a timed buff.", a =>
        {
            Need(a, 3);
            var op = a[1].ToLowerInvariant() switch
            {
                "add" => ModifierOp.Add,
                "mul" => ModifierOp.Mul,
                _ => throw new DevCommandException("Op must be add or mul."),
            };
            if (!DevFormat.TryParseNum(a[2], out var value)) throw new DevCommandException($"'{a[2]}' is not a number.");
            if (a.Length > 3)
            {
                var ticks = Count(a, 3);
                if (ticks <= 0) throw new DevCommandException("Ticks must be positive.");
                var m = Lich.AddTimedModifier(a[0], op, value, "console", ticks);
                return $"Added {a[0]} {a[1]} {DevFormat.Exact(value)} until tick {m.ExpiresAtTick}. {a[0]} = {DevFormat.Exact(Lich.State.Stats.Get(a[0]))}.";
            }
            Lich.AddModifier(new Modifier(a[0], op, value, "console"));
            return $"Added {a[0]} {a[1]} {DevFormat.Exact(value)}. {a[0]} = {DevFormat.Exact(Lich.State.Stats.Get(a[0]))}.";
        }, (i, _) => i switch { 0 => StatIds(), 1 => new[] { "add", "mul" }, _ => Empty }));
        Add(new("unmod", "unmod [source]", "Remove all modifiers from a source (default 'console').", a =>
        {
            var source = a.Length > 0 ? a[0] : "console";
            return $"Removed {Lich.RemoveModifiersFrom(source)} modifier(s) from '{source}'.";
        }, (i, _) => i == 0 ? Lich.State.Stats.Modifiers.Select(m => m.Source).Distinct() : Empty));
        Add(new("damage", "damage <amount>", "Damage the lich.", a =>
        {
            Lich.Damage(NumArg(a, 0));
            return $"Health {DevFormat.Exact(Lich.State.Health)} / {DevFormat.Exact(Lich.State.MaxHealth)}.";
        }));
        Add(new("heal", "heal <amount|full>", "Heal the lich (no regeneration in v0.1).", a =>
        {
            Need(a, 1);
            if (a[0].Equals("full", StringComparison.OrdinalIgnoreCase)) Lich.RestoreFullHealth();
            else Lich.Heal(NumArg(a, 0));
            return $"Health {DevFormat.Exact(Lich.State.Health)} / {DevFormat.Exact(Lich.State.MaxHealth)}.";
        }, (i, _) => i == 0 ? new[] { "full" } : Empty));

        // ---- Ticks ----
        Add(new("ticks", "ticks <n>", "Run n ticks now as one batch, in full tick order (tests batching).", a =>
        {
            var n = Count(a, 0);
            if (n <= 0) throw new DevCommandException("n must be positive.");
            Ticks.RunTicks(n);
            return $"Ran {DevFormat.Exact(n)} ticks. Tick {DevFormat.Exact(Ticks.TickCount)}.";
        }));
        Add(new("offline", "offline <seconds|30m|8h|2d>", "Simulate time away (base 2 s ticks, 24 h cap) and show the recap.", a =>
        {
            Need(a, 1);
            var seconds = Duration(a[0]);
            _ = Ticks.RunOffline(seconds) ?? throw new DevCommandException("No game loaded, or the run failed (see log).");
            return null; // the recap is logged by the shell (Tick category)
        }, (i, _) => i == 0 ? new[] { "60", "30m", "8h", "24h", "2d" } : Empty));
        Add(new("speed", "speed <x>", "Dev time scale, 0.1 to 100 (tick length and offline maths stay at 2 s).", a =>
        {
            Need(a, 1);
            if (!double.TryParse(a[0].TrimEnd('x', 'X'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
                throw new DevCommandException($"'{a[0]}' is not a speed.");
            Ticks.SetSpeed(x);
            return $"Speed {Ticks.Speed:0.##}x.";
        }, (i, _) => i == 0 ? new[] { "1", "5", "20", "100" } : Empty));
        Add(new("pause", "pause", "Pause ticking (stops the bar and processing).", _ => { Ticks.Pause(); return "Paused."; }));
        Add(new("resume", "resume", "Resume ticking.", _ => { Ticks.Resume(); return "Resumed."; }));

        // ---- Saves (phase 1) ----
        Add(new("save", "save", "Save the current slot now.", _ =>
        {
            Game.SaveNow();
            return $"Saved slot {Game.Session?.CurrentSlot}.";
        }));
        Add(new("load", "load <slot>", "Load a slot (falls back to its backup).", a =>
        {
            var slot = Slot(a);
            Game.LoadSlot(slot);
            return $"Loaded slot {slot} ({Game.Session?.StartKind}).";
        }, (i, _) => i == 0 ? Slots() : Empty));
        Add(new("newgame", "newgame [slot]", "Start a new game in a slot (default: next free).", a =>
        {
            var s = Game.Session ?? throw new DevCommandException("No session (boot failed).");
            var slot = a.Length > 0 ? Slot(a) : s.Saves.NextFreeSlot();
            Game.NewGame(slot);
            return $"New game in slot {slot}.";
        }));
        Add(new("wipe", "wipe", "Start a new game over the current slot (old save kept as its backup).", _ =>
        {
            var slot = Game.Session?.CurrentSlot ?? throw new DevCommandException("No game loaded.");
            Game.NewGame(slot);
            return $"Wiped slot {slot}.";
        }));
        Add(new("delete", "delete <slot>", "Delete another slot and its backup.", a =>
        {
            var slot = Slot(a);
            try { return Game.DeleteSlot(slot) ? $"Deleted slot {slot}." : $"Slot {slot} has nothing to delete."; }
            catch (InvalidOperationException e) { throw new DevCommandException(e.Message); }
        }, (i, _) => i == 0 ? Slots() : Empty));
        Add(new("slots", "slots", "List save slots.", _ => string.Join("\n", Game.ListSlots().Select(i =>
            $"slot {i.Slot}{(i.Slot == Game.Session?.CurrentSlot ? " (current)" : "")}: " +
            (i.Readable ? $"saved {i.SavedAtUtc?.ToLocalTime():g}, v{i.Version}" : "UNREADABLE") +
            (i.MainCorrupt ? ", main corrupt" : "") + (i.HasBackup ? ", backup" : "")))));

        // ---- Not built yet: listed so the console matches the plan ----
        Add(new("give", "give <resource|item> <amount>", "Add a resource (capped) or item; a negative amount removes.", a =>
        {
            Need(a, 2);
            if (!long.TryParse(a[1], out var signed) && !DevFormat.TryParseCount(a[1], out signed))
                throw new DevCommandException($"'{a[1]}' is not an amount.");
            try
            {
                var done = CryptManager.Instance.DebugGive(a[0], signed);
                return $"{(signed >= 0 ? "Gave" : "Removed")} {DevFormat.Exact(Math.Abs(done))} {a[0]} " +
                       $"(now {DevFormat.Exact(CryptManager.Instance.Stockpile.Get(a[0]))}).";
            }
            catch (ArgumentException e) { throw new DevCommandException(e.Message); }
        }, (i, _) => i == 0 ? StockIdsAll() : i == 1 ? new[] { "10", "100", "1K" } : Empty));
        Add(new("xp", "xp <skill> <amount>", "Give skill XP.", a =>
        {
            var s = SkillArg(a);
            s.GrantXp(NumArg(a, 1));
            return $"{s.Name}: level {s.Level}, {DevFormat.Exact(s.Xp)} XP.";
        }, (i, _) => i == 0 ? SkillIds.All : Empty));
        Add(new("level", "level <skill> <n>", "Raise a skill to level n (sets XP to that level; never lowers).", a =>
        {
            var s = SkillArg(a);
            var n = (int)Math.Clamp(Count(a, 1), 1, XpCurve.MaxLevel);
            if (n < s.Level) throw new DevCommandException($"{s.Name} is already level {s.Level}; levels never go down.");
            s.DebugSetLevel(n);
            return $"{s.Name}: level {s.Level}.";
        }, (i, _) => i == 0 ? SkillIds.All : i == 1 ? new[] { "5", "10", "15", "99" } : Empty));
        // ---- Workshop (phase 5) ----
        Add(new("upgrade", "upgrade <building>", "Buy the next level of a building with coins.", a =>
        {
            var b = BuildingArg(a, 1);
            var cost = b.UpgradeCost;
            if (!CryptManager.Instance.TryUpgrade(b.Id))
                throw new DevCommandException(b.IsMaxLevel ? $"{b.Name} is at max level." : $"{b.Name} needs {DevFormat.Exact(cost)} coins.");
            return $"{b.Name} is level {b.Level} (spent {DevFormat.Exact(cost)} coins).";
        }, (i, _) => i == 0 ? BuildingIds.All : Empty));
        Add(new("blevel", "blevel <building> <n>", "Debug: set a building's level (no cost).", a =>
        {
            var b = BuildingArg(a, 2);
            b.DebugSetLevel((int)Math.Clamp(Count(a, 1), 1, b.Definition.MaxLevel));
            return $"{b.Name} is level {b.Level}.";
        }, (i, _) => i == 0 ? BuildingIds.All : Empty));
        Add(new("sell", "sell <id> <on|off> [reserve]", "Undead Market: add or change a sell list line.", a =>
        {
            Need(a, 2);
            var m = CryptManager.Instance.Building<Crypt.Buildings.UndeadMarket>() ?? throw new DevCommandException("No market.");
            var on = a[1].ToLowerInvariant() switch { "on" => true, "off" => false, _ => throw new DevCommandException("Use on or off.") };
            var reserve = a.Length > 2 ? Count(a, 2) : m.Entry(a[0])?.Reserve ?? 0;
            try { m.SetSell(a[0], on, reserve); }
            catch (ArgumentException e) { throw new DevCommandException(e.Message); }
            return $"Market: {(on ? "selling" : "keeping")} {a[0]} at {m.PriceOf(a[0])} coins, reserve {DevFormat.Exact(reserve)}.";
        }, (i, _) => i == 0 ? CryptManager.Instance.Building<Crypt.Buildings.UndeadMarket>()?.Sellable ?? Empty : i == 1 ? new[] { "on", "off" } : Empty));
        Add(new("transmute", "transmute <recipe|none>", "Transmuter: choose the conversion to run.", a =>
        {
            Need(a, 1);
            var t = CryptManager.Instance.Building<Crypt.Buildings.Transmuter>() ?? throw new DevCommandException("No transmuter.");
            try { t.Select(a[0] == "none" ? null : a[0]); }
            catch (ArgumentException e) { throw new DevCommandException(e.Message); }
            return $"Transmuter: {t.Recipe?.Name ?? "none"}.";
        }, (i, _) => i == 0 ? (CryptManager.Instance.Building<Crypt.Buildings.Transmuter>()?.Recipes.Select(r => r.Id) ?? Empty).Append("none") : Empty));
        Add(new("target", "target <n>", "Summoning Circle: stop when the pool total reaches n (0 = up to max).", a =>
        {
            var c = CryptManager.Instance.Building<Crypt.Buildings.SummoningCircle>() ?? throw new DevCommandException("No circle.");
            c.Target = Count(a, 0);
            return c.Target == 0 ? "Summoning up to the max." : $"Summoning until the pool has {DevFormat.Exact(c.Target)}.";
        }));

        Later("unlock", "unlock <id>", "Unlock research, a unit or a recipe.", "research (phase 7)", (i, _) => i == 0 ? ContentIds() : Empty);
        Later("battle", "battle <area>", "Start a test battle.", "the world map (phase 8)");
    }

    public static void Add(DevCommand c) => _commands[c.Name] = c;

    /// <summary>Runs one console line. Returns output text (may be null). Throws DevCommandException for bad input.</summary>
    public static string? Execute(string line)
    {
        var parts = Split(line);
        if (parts.Length == 0) return null;
        if (!_commands.TryGetValue(parts[0], out var cmd))
            throw new DevCommandException($"Unknown command '{parts[0]}'. Type help.");
        return cmd.Run(parts[1..]);
    }

    /// <summary>Candidates for the word being typed at the end of <paramref name="line"/>.</summary>
    public static IReadOnlyList<string> Completions(string line)
    {
        var parts = Split(line).ToList();
        if (line.Length == 0 || char.IsWhiteSpace(line[^1])) parts.Add("");
        var word = parts[^1];
        IEnumerable<string> pool;
        if (parts.Count == 1) pool = _commands.Keys;
        else if (_commands.TryGetValue(parts[0], out var c) && c.Complete is not null)
            pool = c.Complete(parts.Count - 2, parts.Skip(1).Take(parts.Count - 2).ToArray());
        else pool = Empty;
        return pool.Where(p => p.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    public static string[] Split(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ---- helpers ----

    private static LichManager Lich => LichManager.Instance;
    private static GameManager Game => GameManager.Instance;
    private static TickManager Ticks => TickManager.Instance;
    private static readonly string[] Empty = Array.Empty<string>();

    private static void Later(string name, string usage, string help, string needs, Func<int, string[], IEnumerable<string>>? complete = null) =>
        Add(new(name, usage, $"{help} [needs {needs}]", _ => throw new DevCommandException($"'{name}' needs {needs}, which is not built yet."), complete));

    private static void Need(string[] a, int n)
    {
        if (a.Length < n) throw new DevCommandException("Not enough arguments.");
    }

    private static long Count(string[] a, int i)
    {
        Need(a, i + 1);
        return DevFormat.TryParseCount(a[i], out var n) ? n : throw new DevCommandException($"'{a[i]}' is not a count (e.g. 250, 1.5K, 2M, 100B).");
    }

    private static Num NumArg(string[] a, int i)
    {
        Need(a, i + 1);
        return DevFormat.TryParseNum(a[i], out var n) ? n : throw new DevCommandException($"'{a[i]}' is not a number.");
    }

    /// <summary>"90" (seconds), "30s", "15m", "8h", "2d".</summary>
    private static double Duration(string s)
    {
        s = s.Trim().ToLowerInvariant();
        double unit = s.Length > 0 ? s[^1] switch { 's' => 1, 'm' => 60, 'h' => 3600, 'd' => 86400, _ => 0 } : 0;
        var number = unit == 0 ? s : s[..^1];
        if (unit == 0) unit = 1;
        if (!double.TryParse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            || !double.IsFinite(v) || v < 0)
            throw new DevCommandException($"'{s}' is not a duration (e.g. 90, 30m, 8h, 2d).");
        return v * unit;
    }

    private static int Slot(string[] a)
    {
        Need(a, 1);
        return int.TryParse(a[0], out var s) && s > 0 ? s : throw new DevCommandException($"'{a[0]}' is not a slot number.");
    }

    private static IEnumerable<string> Slots() => GameManager.Instance.ListSlots().Select(s => s.Slot.ToString());

    private static IEnumerable<string> ContentIds() =>
        GameManager.Instance.Content.AllDefinitions().Select(d => d.Id);

    private static IEnumerable<string> StatIds() =>
        new[] { LichStats.MaxHealth, LichStats.MaxInsight, LichStats.MaxNecromancers }.Concat(Lich.State.Stats.KnownStats);

    private static long Assign(string owner, string activity, long n, bool add)
    {
        try { return add ? DevAssign.Add(owner, activity, n) : DevAssign.Remove(owner, activity, n); }
        catch (ArgumentException e) { throw new DevCommandException(e.Message); }
    }

    private static Crypt.Buildings.BuildingBase BuildingArg(string[] a, int need)
    {
        Need(a, need);
        return CryptManager.Instance.Building(a[0].ToLowerInvariant())
            ?? throw new DevCommandException($"No building '{a[0]}' ({string.Join(", ", BuildingIds.All)}).");
    }

    private static SkillBase SkillArg(string[] a)
    {
        Need(a, 2);
        return Lich.Skill(a[0].ToLowerInvariant())
            ?? throw new DevCommandException($"No skill '{a[0]}' ({string.Join(", ", SkillIds.All)}).");
    }

    private static IEnumerable<string> StockIdsAll() =>
        GameManager.Instance.Content.All<ResourceDefinitionBase>().Select(d => d.Id)
            .Concat(GameManager.Instance.Content.All<ItemDefinitionBase>().Select(d => d.Id));

    private static IEnumerable<string> CompleteAssignment(int i, string[] typed)
    {
        var assignments = Lich.AllAssignments();
        return i switch
        {
            0 => assignments.Select(a => a.OwnerId).Concat(SkillIds.Gathering).Concat(SkillIds.Crafting).Concat(BuildingIds.All),
            1 => Lich.Skill(typed[0]) switch
            {
                GatheringSkill g => g.Activities.Select(x => x.Id),
                CraftingSkill c => c.Recipes.Select(x => x.Id),
                _ => CryptManager.Instance.Building(typed[0]) is not null ? new[] { "main" } : assignments.Where(a => a.OwnerId == typed[0]).Select(a => a.ActivityId),
            },
            2 => new[] { "1", "10", "100", "1K", "max", "all" },
            _ => Empty,
        };
    }
}
