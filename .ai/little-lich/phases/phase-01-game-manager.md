# Phase 1: GameManager

## Goal
A Godot C# project that starts, loads all content, finds or creates a save, and can write the save safely. Nothing is playable yet.

## Depends on
Nothing.

## Responsibilities
- Start the game and boot the other managers in order.
- Load every JSON content file once into a read-only content database.
- Find saves, list slots, load one, create a new one.
- Write saves (autosave and manual).
- On load, work out time away and hand it to the TickManager as offline ticks (phase 3).

## Project skeleton
- Godot 4 with C# (.NET) enabled.
- Autoload singletons: `GameManager`, then later `TickManager`, `LichManager`, `CryptManager`.
- Folders: `Scripts/Core`, `Scripts/Managers`, `Scripts/Skills`, `Scripts/Crypt`, `Scripts/Content`, `Data/` (JSON), `UI/`.

## Core types
```csharp
public interface ITickable { void Tick(long ticks); }
public interface ISaveable { SaveData Save(); void Load(SaveData data); }

public class ContentDatabase {            // read-only after load
    public T Get<T>(string id) where T : Definition;
    public IEnumerable<T> All<T>() where T : Definition;
}
public abstract class Definition { public string Id; public string Name; }
```

## Number type (decide first)
XP near level 999 is about 10^43 and necromancers reach 100 billion, so 64-bit integers overflow for XP.
- Option A: `double` everywhere. Simple, fast, loses exactness above 2^53 (fine for XP, risky for counts near 100B only if added one at a time; 100B is well under 2^53).
- Option B: a custom `BigNum` struct.
- Recommendation for v0.1: wrap the choice in one `Num` alias/struct backed by `double`, so it can be swapped later without touching callers. Necromancer counts and resource stock use `long` (100B fits); XP uses `Num`.

## Save format
- JSON, with `version` and `savedAtUtc` at the top.
- Write to a temp file, then rename over the real save. Keep one backup of the previous save.
- Multiple slots in the Godot user folder (`user://saves/slot_1.json`).
- Each `ISaveable` writes its own section keyed by its id. Saves hold ids and counts only, never definitions.
- Versioned migration hook: `Migrate(SaveData data, int fromVersion)`.

## Startup order
1. Load content JSON into `ContentDatabase`.
2. Find saves; if none, create a new game state.
3. Create/load LichManager, CryptManager, TickManager state from the save.
4. Compute offline ticks from `savedAtUtc` and hand to TickManager.
5. Show the main shell UI.

## Tasks
- [x] Create Godot C# project and folder layout.
- [x] `ITickable`, `ISaveable`, `Definition`, `ContentDatabase`.
- [x] JSON loader with clear errors (file, id, field).
- [x] Number type decision and `Num` wrapper.
- [x] Save writer (temp then rename, backup) and reader.
- [x] Slot list and new game.
- [x] Version and migration hook.
- [x] Autosave trigger (called by the TickManager every N ticks).

## Done when
- The game boots to an empty shell, loads sample JSON, creates a save, quits, and reloads the same save.
- A corrupted save falls back to the backup instead of crashing.

## Out of scope
Cloud saves, settings menu, UI beyond a placeholder.

## Status: complete (2026-11-17)

### Decisions
- **Number type:** `Num` (`Scripts/Core/Num.cs`), a struct backed by `double`, with operators, `ToShortString()` (1.23K ... 1Dc, then `1.23e43`) and a JSON converter that writes a plain number. NaN is rejected. Necromancer counts and stock stay `long`. Swap the backing field later without touching callers.
- **Pure C# core, thin Godot layer.** `Scripts/Core` and `Scripts/Content` have no Godot types, so they are unit tested with plain `dotnet test`. `GameSession` holds all GameManager logic; the `GameManager` autoload only adds Godot paths, signals and save-on-quit.
- **Content files:** either an array of entries (each with `"type"`) or `{ "type": "resource", "entries": [...] }` where `type` is the default. JSON keys are snake_case. Unknown fields are errors (catches typos). All errors are collected and reported together as `file [id] .field: message`.
- **Ids are unique per family** (the abstract class directly under `Definition`, e.g. `ResourceDefinitionBase`), so `iron_ore` can be both a resource and a mining activity (phase 11).
- **Content is read through Godot's `FileAccess`** (not `System.IO`) so it works from an exported .pck. Exports need `*.json` in the export include filter.
- **Save format:** `{ "version", "saved_at_utc", "sections": { <SaveKey>: {...} } }`. Snake_case to match content JSON. `ISaveable` gained a `SaveKey` property. A missing section calls `Load` with empty data (new features load defaults in old saves).
- **Safe writes:** `slot_N.json.tmp` -> flushed -> `File.Replace` over `slot_N.json`, previous save kept as `.bak`. If the current main file is unreadable it is not rotated into the backup, so a good backup is never lost.
- **Load fallback:** corrupt main -> backup; the slot is rewritten immediately so it is healthy again. Both unreadable -> startup creates a new game in the next free slot and never overwrites the broken slot. Saves from a newer version are refused.
- **Startup picks the most recently saved readable slot** (no slot picker UI yet).
- **Saves happen** on new game, on quit (window close or any tree exit), on request, and via `GameManager.OnTicksProcessed(ticks)` every `AutosaveIntervalTicks` (default 30 = 60 s). Batched offline ticks count fully.
- **Time away:** `GameSession.PendingOfflineSeconds` (never negative if the clock moved back). Phase 3 takes it once with `TakePendingOfflineSeconds()` and converts with `OfflineTime.ToTicks(seconds, BaseTickSeconds, cap)`.
- `GameMeta` (section `meta`: created time, save count, total ticks) exists so save round trips are meaningful before any manager does.

### Verified
- `dotnet test tests/LittleLich.Tests`: 50 tests (Num, content loader errors, save round trip, temp/backup, corrupt fallback, slots, migration, offline maths, autosave).
- Godot 4.4.1 headless: boots, loads 6 sample resources, creates slot 1, reloads it, recovers from a truncated save via the backup, and shows content errors without crashing.

### Follow-ups (not in this phase)
- Slot picker / load screen UI.
- `--validate-content` command (phase 11).
- Export preset include filter for `Data/**/*.json` (when exports are set up).
