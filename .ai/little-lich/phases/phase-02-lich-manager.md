# Phase 2: LichManager

## Goal
The lich as game state: stats, buffs/debuffs, and the necromancer pool with every assignment tracked in one place.

## Depends on
Phase 1.

## Responsibilities
- Own the per-skill classes (registered in phase 4) and the SpellBook (later).
- Hold Health, Insight (doc also says Max Focus; open question), and derived stats.
- Apply modifiers from buffs, debuffs, gear (phase 6) and research (phase 7).
- Own the **necromancer pool**: total, max, free, assigned.
- Track **where every necromancer is assigned** (skill activity, Workshop building, later others).
- Save and load the lich's data.

## Necromancer pool
- `Total`, `Max` (up to 100 billion), `Free = Total - Assigned`.
- `AddToPool(long count)`: called by the Summoning Circle when it produces necromancers. Respects `Max`.
- `TryAssign(string ownerId, string activityId, long count) -> long`: assigns up to `count`, limited by free. Returns how many were actually assigned.
- `Unassign(string ownerId, string activityId, long count)`.
- `GetAssigned(ownerId, activityId)` and `AllAssignments()` (the TickManager reads this every tick).
- Raises `NecromancerPoolChanged` for the top bar (Free, Assigned, Max).

## Assignment record
```csharp
public record Assignment(string OwnerId, string ActivityId, long Count);
// OwnerId example: "mining", ActivityId example: "iron_ore"
// Workshop example: OwnerId "mana_well", ActivityId "main"
```

## Buffs and debuffs
- `Modifier { StatId, Op (Add|Mul), Value, Source, ExpiresAtTick? }`.
- Stat value = (base + sum of Add) x product of Mul.
- Tick step expires finished modifiers.
- Health: max HP; regeneration is an open question (decide here).

## Tasks
- [x] `LichManager` autoload with `ISaveable`.
- [x] Necromancer pool with the methods above and unit tests for edge cases (assign more than free, unassign more than assigned, pool at max).
- [x] Assignment table keyed by owner + activity.
- [x] Modifier system and stat calculation.
- [x] Events for UI: pool changed, stat changed.
- [x] Save/load of pool, assignments, modifiers.
- [x] Debug command: add necromancers to the pool.

## Done when
- Assigning and unassigning from a test script keeps `Free + Assigned = Total` at all times.
- Save, quit, reload restores every assignment exactly.

## Out of scope
Skill XP logic (phase 4), gear (phase 6), spell book.

## Status: complete (2026-11-17)

### Decisions
- **Health regeneration: none in v0.1** (user decision). Health changes only through `Damage`, `Heal`, `RestoreFullHealth`. A higher max does not heal; a lower max clamps current health. No regen step in the lich tick.
- **Spell limit is called Insight** (user decision), stat id `max_insight`. Budget vs cap is still open for phase 6.
- **Plain C# core, thin Godot layer** (as in phase 1). `Scripts/Core/Lich/`: `NecromancerPool`, `Assignment`, `Modifier`/`ModifierOp`/`LichStats`, `StatSheet`, `LichState` (`ISaveable` + `ITickable`, save key `lich`). The `LichManager` autoload (`Scripts/Managers/LichManager.cs`) holds a `LichState`, forwards commands and re-emits events as signals: `NecromancerPoolChanged(free, assigned, max)`, `StatChanged(statId, value)`, `HealthChanged(current, max)`.
- **Boot:** `LichManager` is the second autoload and sets `Instance` in its constructor; `GameManager.Boot` registers `LichManager.Instance.State` before the save is read (fails with a clear boot error if the autoload is missing).
- **Max necromancers is a stat** (`max_necromancers`, placeholder base 10) so the Summoning Circle and research can raise it with modifiers. The pool max is floored and clamped to 0..100B (`NecromancerPool.HardCap`). If Max drops below Total, no necromancers are lost; the pool just stops growing.
- **Pool API:** `AddToPool`, `TryAssign` and `Unassign` all return how many actually moved (spec had `Unassign` as void). Zero/negative counts are no-ops; blank owner/activity ids throw. Extra helpers: `UnassignOwner(owner)`, `GetAssignedToOwner(owner)`. `AllAssignments()` is sorted by owner then activity (deterministic tick order) and cached until assignments change.
- **Modifiers:** `Modifier(StatId, Op, Value: Num, Source, ExpiresAtTick?)`. Stat = (base + sum Add) x product Mul; Mul 1.1 = +10%. Stat values use `Num`. `StatChanged` fires only when a final value actually changes. Remove by source (`RemoveModifiersFrom("gear:hood")`).
- **Buff clock:** `LichState.CurrentTick` (saved) advances in `Tick(n)`; a timed modifier is active while `CurrentTick < ExpiresAtTick`. `AddTimedModifier(..., durationTicks)` sets the expiry from now. `Tick(n)` equals n x `Tick(1)`.
- **Save section `lich`:** `current_tick`, `health`, `necromancers { total, assignments[{owner_id, activity_id, count}] }`, `modifiers[{stat_id, op: "add"|"mul", value, source, expires_at_tick}]`. Base stats are not saved (they come from code/skills). Load repairs bad data: drops blank ids, non-positive counts, unknown ops and already-expired modifiers; merges duplicate assignments; raises Total to cover the assigned sum so every saved assignment is restored exactly; clamps health. A missing section is a new game (full health, 0 necromancers). Load raises each UI event once at the end.
- **Debug command:** `LichManager.DebugAddNecromancers(n)` (raises Max with one permanent `debug` modifier when needed). Shell buttons, and command-line args after `--`: `--add-necromancers=N`, `--assign=owner:activity:N`, `--unassign=owner:activity:N`.

### Verified
- `dotnet test tests/LittleLich.Tests`: 74 tests (24 new: pool edge cases incl. assign > free, unassign > assigned, pool at max, a 5,000-step random invariant check of `Free + Assigned = Total`; stat formula and events; buff expiry and tick batching; health clamp/no regen; save/reload round trip; load repair; old saves without a `lich` section).
- Godot 4.4.1 headless: run 1 with `--add-necromancers=1000 --assign=mining:iron_ore:300 --assign=mana_well:main:11 --unassign=mining:iron_ore:50 --assign=fishing:trout:5000` (only 739 free, assigned 739), saved on quit; run 2 reloaded `fishing/trout=739, mana_well/main=11, mining/iron_ore=250`, total 1000, free 0.

### Follow-ups (not in this phase)
- Per-skill class registration on the LichManager (phase 4) and the SpellBook.
- Health skill sets the `max_health` base from its level (phase 4); gear modifiers (phase 6); research raises `max_necromancers` (phase 7).
- Real top bar with the Free/Assigned/Max bar and the Assign-by selector (1, 1k, 1M, all) once the UI is built.
- TickManager (phase 3) calls `LichState.Tick` as tick step 2.
