# Phase 7: ResearchManager (Necromancy 1 only)

## Goal
Research works end to end for one tome, Necromancy 1: a node tree, benches staffed by necromancers, and unlocks that change the game.

## Depends on
Phases 2 to 5.

## Model
- `ResearchTome` is one class per tome, owned by the CryptManager. v0.1 has only **Necromancy 1**.
- Tomes are listed (not tabbed) in the UI, so the manager exposes a list.
- Nodes are drawn large, Path of Exile 2 style, so each node has a position, links and a big icon.
- Because the player can run several benches, a tome can research several nodes at once, depending on how necromancers are distributed.

## Node definition (JSON)
```json
{
  "id": "nec1_stronger_skeletons",
  "tome": "necromancy_1",
  "name": "Stronger Skeletons",
  "requires": ["nec1_root"],
  "position": [2, 1],
  "cost": { "mana": 100, "souls": 10 },
  "research_ticks": 60,
  "effects": [ { "type": "unlock_unit", "id": "skeleton_warrior" },
               { "type": "stat_mod", "stat": "skeleton_hp", "op": "mul", "value": 1.1 } ]
}
```
Effects are data too: unlock unit, unlock recipe, unlock building level, stat modifier.

## Rules
- A node is available when all `requires` are done and the lich meets any level requirement.
- Researching a node spends its cost up front, then needs `research_ticks` worth of staffed bench progress. Necromancers on a bench speed it up.
- Progress is processed in tick step 5 (research benches).
- Bench count may be limited (open question); start with a fixed small number.

## Tasks
- [x] `ResearchManager` or per-tome `ResearchTome` classes with a registry.
- [x] Node loading from JSON and validation (no cycles, missing ids).
- [x] Bench assignment through LichManager.
- [x] Progress in the tick, batched for offline.
- [x] Effect application (unlock and modifier).
- [x] Events: node available, started, completed.
- [x] Save/load of progress and completed nodes.
- [x] A first Necromancy 1 tree of about 10 nodes that unlocks the first units and recipes.

## Done when
- Researching a node unlocks its effect and survives save and reload.
- Offline time completes research correctly.

## Out of scope
Other tomes, Necromancy 2 and beyond, cost balancing.

## Status: complete (2026-11-17)

### Decisions
- **Classes** (plain C#, `Scripts/Crypt/Research/Research.cs`, owned by the CryptManager):
  - `ResearchTome` (one class per tome; `Necromancy1Tome`), holding its nodes and done set.
  - `ResearchManager` (ITickable in `TickGroup.Research` = tick step 5; save section `research`), holding the tome list and the benches.
  - `Bench` (node, progress).
- **Benches are global, not per tome** (the design doc: extra benches let several nodes or several trees run in parallel). Settings are one `research_bench` content entry:
  - 1 bench at the start, 5 necromancer slots per bench.
  - Work per tick = `base_work` (0) + staffed x `work_per_necromancer` (1).
  - An unstaffed bench makes no progress ("staffed bench progress").
  - Staff go through LichManager assignments: owner `research`, activity `bench_N`.
  - Extra benches come from an `unlock_bench` effect. This settles the open bench-count question for now: research-unlocked, starting at one.
- **Node schema** (`research_node`): `tome`, `requires`, `position` [x, y], `size` (small, notable, keystone), `cost` {id: count}, `research_ticks` (work), `skill_level` (tome skill needed, 0 = none), `xp` (tome skill XP on completion), `effects`, `description`.
  - Tomes (`tome`) name their gating skill (`necromancy`).
  - Validation: requires exist and are in the same tome; each tome has a root and no cycles (named in the error); cost ids exist; effect types and their ids are checked.
  - `unlock_unit` ids are recorded but can't be checked until units exist (phase 9/10).
- **Rules:**
  - A node is available when all `requires` are done and the tome skill is at `skill_level`; `WhyLocked` says what's missing.
  - `Start(node, bench?)` checks availability, a free bench and the cost, then pays up front.
  - At `research_ticks` of work the node completes, its effects apply, the tome skill gets its XP, and the bench frees up. There is no queue, and leftover work is not carried over.
  - `Cancel(bench)` refunds the cost (resources capped by storage) and loses progress.
- **Effects** (data):
  - `stat_mod` → a permanent lich modifier, source `research:<node>`.
  - `unlock_unit` / `unlock_recipe` (recipe, transmutation or summon, resolved by id) / `unlock_activity` → the shared `Core/Unlocks` registry.
  - `building_level` → extra max levels for a building (`BuildingBase.MaxLevel`).
  - `unlock_bench`.
  - **Locking:** content named by any unlock effect is research-locked until that node is done. Skills refuse necromancers for locked activities/recipes, the Transmuter refuses locked recipes, and the Summoning Circle uses the first unlocked summon.
- **Tome skill and XP** (my addition): completing a Necromancy 1 node gives Necromancy XP (via `SkillXpEarned`, wired at boot). This gives the level-only Necromancy skill an XP source, and the deeper nodes need Necromancy 5 and 10, which matches "skill levels gate how deep each tree goes".
- **Save:** done node ids and bench node + progress. Load rebuilds every derived effect from the definitions (unlocks, building levels, benches, research modifiers), removes stale `research:` modifiers, and drops unknown nodes. Research is registered before the buildings, so extra building levels are in place when building levels are restored.
- **Batching:** fixed-point progress per bench, so `Tick(n)` completes exactly what n x `Tick(1)` does (tested at 7, 25 and 400 ticks with two benches). Offline runs record completed nodes in the away recap.
- **Necromancy 1 tree** (`Data/research.json`, 10 nodes, placeholders):
  - **Grave Lore** (root): +5 max necromancers.
  - **Raise Skeletons** and **Raise Zombies:** unlock units `skeleton` and `zombie` for phase 9.
  - **Second Bench.**
  - **Ossuary:** unlocks the Hunting activity Boneyard (bones) and the Transmuter's Hide to Bones.
  - **Bone Weapons:** unlocks the Smithing recipe Bone Spear (unit weapon).
  - **Deep Channels:** +10 max levels for the Mana Well and Summoning Circle, which are now capped at 10 without it.
  - **Tireless Dead:** Mining and Smithing x1.1.
  - **Hunger of the Grave:** Necromancy 5; +10 max necromancers.
  - **Lord of the Grave** (keystone): Necromancy 10; max necromancers x1.5.
- **Signals on CryptManager:** `ResearchNodeStarted`, `ResearchNodeCompleted`, `ResearchNodeAvailable`, `ResearchNodeCancelled`, `ResearchBenchesChanged`.

### Verified
- `dotnet test tests/LittleLich.Tests`: 172 tests (16 new). Covered:
  - **Done when:** researching Grave Lore spends its cost, completes after exactly 10 ticks of one necromancer, and raises Max necromancers 10 → 15. Done nodes, bench progress, staff, unlocks, building levels above the base cap (15 with Deep Channels) and research modifiers survive save and reload, and both copies continue identically. Offline time (`RunOffline`) completes research, and the recap shows it.
  - **Also:**
    - Every effect type; content locked until researched (activity, recipe, transmutation).
    - Skill-level gating with reasons; slot-capped bench speed; unstaffed benches; two benches in parallel; no free bench; cancel refunds.
    - Batched equals single ticks; stale research modifiers removed on load; cycles, missing ids and bad effects rejected.
- **Godot 4.4.1 at 1280x720, new game via `--dev-cmd`:**
  - Staffed bench 1 and researched Grave Lore, then Second Bench.
  - Staffed bench 2 and ran Raise Skeletons and Raise Zombies in parallel.
  - Starting Hunger of the Grave was refused with the reason.
  - A second launch reloaded the save; offline time finished Raise Zombies (shown in the recap), giving 4/10 done and Necromancy 10 from research XP.
  - Fixed on the way: the three-column layout didn't fit at 1280 px. It is now tomes row / full-width tree / details beside compact benches.

### Follow-ups (not in this phase)
- Bench levels (research speed for coin and mana, from the design doc), pan/zoom and node art.
- Unit unlocks take effect in phase 9 (`Unlocks.UnlockedOf(Unlocks.Unit)`); validate unit ids once units exist (phase 10).
- Other tomes (found on the world map), and Magic gating its trees.
- Cost and time balancing (out of scope).
