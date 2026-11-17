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
- [ ] `LichManager` autoload with `ISaveable`.
- [ ] Necromancer pool with the methods above and unit tests for edge cases (assign more than free, unassign more than assigned, pool at max).
- [ ] Assignment table keyed by owner + activity.
- [ ] Modifier system and stat calculation.
- [ ] Events for UI: pool changed, stat changed.
- [ ] Save/load of pool, assignments, modifiers.
- [ ] Debug command: add necromancers to the pool.

## Done when
- Assigning and unassigning from a test script keeps `Free + Assigned = Total` at all times.
- Save, quit, reload restores every assignment exactly.

## Out of scope
Skill XP logic (phase 4), gear (phase 6), spell book.
