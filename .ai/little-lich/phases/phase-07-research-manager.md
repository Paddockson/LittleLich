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
- [ ] `ResearchManager` or per-tome `ResearchTome` classes with a registry.
- [ ] Node loading from JSON and validation (no cycles, missing ids).
- [ ] Bench assignment through LichManager.
- [ ] Progress in the tick, batched for offline.
- [ ] Effect application (unlock and modifier).
- [ ] Events: node available, started, completed.
- [ ] Save/load of progress and completed nodes.
- [ ] A first Necromancy 1 tree of about 10 nodes that unlocks the first units and recipes.

## Done when
- Researching a node unlocks its effect and survives save and reload.
- Offline time completes research correctly.

## Out of scope
Other tomes, Necromancy 2 and beyond, cost balancing.
