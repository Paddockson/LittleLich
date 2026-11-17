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
- [ ] Create Godot C# project and folder layout.
- [ ] `ITickable`, `ISaveable`, `Definition`, `ContentDatabase`.
- [ ] JSON loader with clear errors (file, id, field).
- [ ] Number type decision and `Num` wrapper.
- [ ] Save writer (temp then rename, backup) and reader.
- [ ] Slot list and new game.
- [ ] Version and migration hook.
- [ ] Autosave trigger (called by the TickManager every N ticks).

## Done when
- The game boots to an empty shell, loads sample JSON, creates a save, quits, and reloads the same save.
- A corrupted save falls back to the backup instead of crashing.

## Out of scope
Cloud saves, settings menu, UI beyond a placeholder.
