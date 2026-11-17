# Phase 10: Unit JSON

## Goal
All units are data. Adding a unit means adding a JSON entry, no code.

## Depends on
Phases 8 and 9 (so the schema matches what battles and the builder need).

## File layout
```
Data/
  units/
    bases.json         # body types (skeleton, ghoul, ...)
    enemies.json       # Graveyard and Woods defenders
    actions.json       # reusable battle actions
```

## Unit base
```json
{
  "id": "skeleton",
  "name": "Skeleton",
  "type": "undead",
  "hp": 40,
  "unlock": { "research": "nec1_skeletons" },
  "weapon_slots": ["weapon"],
  "armour_slots": ["body"],
  "actions": ["strike", "brace"]
}
```

## Action
```json
{ "id": "strike", "cooldown_seconds": 2.0, "effect": "damage", "value": 5, "target": "front" }
{ "id": "brace",  "cooldown_seconds": 10.0, "effect": "reduce_next_incoming", "value": 0.10, "target": "self" }
```
A section has at most 3 actions. Cooldowns are in seconds because battles run on their own clock.

## Enemy unit
Same schema as a base unit plus `section` (front/mid/back), `count`, and `reward` hints. The Graveyard and Woods each list their defenders here or in an area file.

## Rules
- Stable text ids, lowercase with underscores; never reuse or rename an id once released (saves refer to them).
- Abstract `UnitDefinition` class with subclasses by `type` (undead, wild, boss).
- The loader validates: unknown action ids, missing unlock ids, more than 3 actions, negative values. Errors name the file and id.
- Balance numbers are placeholders until playtested.

## Tasks
- [x] JSON schema for units, actions and enemies.
- [x] `UnitDefinition` classes and loader into the content database.
- [x] Validation with clear errors.
- [x] 3 to 4 player unit bases and the Graveyard and Woods enemy lists.
- [x] Hook units to research unlocks (phase 7).

## Done when
- Adding a new unit to JSON makes it appear in the Unit Builder and in battle with no code change.
- A broken entry fails the loader with a readable message.

## Out of scope
Unit leveling, art references beyond an icon id, balance.

> **Note (phase 9):** placeholder unit content already exists in `Data/units.json` (`unit` type: side, hp, actions, mana_cost, summon_work) and unit types take battle stats from gear (`UnitBuilder.BattleUnit`). Build the real schema from it and move the files into `Data/units/`.

## Status: complete (2026-11-17)

### Decisions
- **Bases (user decision):** v0.1 has three player bases: Skeleton, Zombie and **Bone Archer** (ranged, undead), unlocked by a new Necromancy 1 node, **Bone Archers** (requires Raise Skeletons). Wisp and Ghoul wait for their tomes (Infusion, Necromancy 2), as in the design doc.
- **File layout as specified:** `Data/units/actions.json`, `bases.json`, `enemies.json`. They replace the placeholder `Data/units.json` from phases 8–9, keep the same numbers so battles behave the same, and make all units pure data.
- **Actions** (`battle_action`, family `BattleActionDefinitionBase`):
  - Fields: `{id, name, cooldown_seconds, effect: damage | reduce_next_incoming | heal, value, target: front | back | lowest | self | ally_lowest, range: melee | ranged}`. The effect names follow the spec, and the battle code now uses them.
  - Validation: cooldown at least 0.1 s, known effect/target/range, value 0 or more, `reduce_next_incoming` as a fraction targeting self.
- **Units** (abstract `UnitDefinitionBase`; subclasses by JSON type `undead`, `wild`, `boss` and `structure` for walls and gates):
  - Fields: `side` (player | enemy), `hp`, `role`, `description`, `actions` (action ids, at most 3), `unlock: { research }`, `weapon_slots`, `armour_slots`, `mana_cost`, `summon_work`.
  - **Gear slots:** a body with no weapon/armour slot cannot carry one (`BuildResult.NoWeaponSlot/NoArmourSlot`). v0.1 allows at most one of each.
  - **Side rules:** unlock, mana cost and gear slots are for player units only. Structures are enemy-only with no actions.
  - **Enemies:** the spec's enemy `section` / `count` / reward hints stay in the area file (`world.json` defenders and rewards), which the spec allows.
- **Errors name file, id and field:** unknown action ids, a missing unlock node, more than 3 actions, negative values, and side/structure rules. For example `units/extra.json [ghoul] .actions: unknown action id 'claw'`. Cross-reference errors are reported once field errors are fixed (loader behaviour since phase 1).
- **Runtime vs content:** battles use a runtime `BattleUnit` / `BattleUnitAction` (`Scripts/World/BattleUnit.cs`) built from a definition (`BattleUnit.From`) or from a unit type with its gear (`UnitBuilder.BattleUnit`: weapon damage added to damage actions, armour defence x 5 HP). Content definitions stay read-only data.
- **Research hook:** a unit names its unlocking node (`unlock.research`). The research manager marks those units locked and unlocks them when the node completes, so `unlock_unit` research effects are no longer needed (still supported; their id must now be a real player unit). The Research tab lists "Unlocks unit X" from the units.
- **Adding a unit is JSON only:** the Unit Builder lists every player base from content, battles accept any unit, areas reference enemies by id, and the test project and the game both read `Data/**`.

### Verified
- `dotnet test tests/LittleLich.Tests`: 208 tests (5 new; battle tests moved to `BattleUnit`). Covered:
  - **Done when:** a Ghoul and a Bone King boss added in a JSON file with no code change appear in the Unit Builder (buildable, with the slot rules applied and gear stats) and fight and win a battle. A broken entry fails with `units/extra.json [ghoul] .actions: unknown action id 'claw'`.
  - **Also:** unit classes from the `units/` folder; 3–4 player bases each with an unlock node; actions resolved by id; missing unlock nodes; negative values; side and structure rules; `unlock_unit` must name a player unit; Bone Archers unlocking from their own node.
- **Godot 4.4.1 at 1280x720, with a temporary `Data/units/zz_temp_ghoul.json` (deleted afterwards):**
  - The Ghoul appeared in the Unit Builder with no code change. 40 were built, summoned (360 mana) and sent: Victory, with "Claw hits ... for 304" (38 standing x (4 claw + 4 sword)).
  - With a broken version of the file, the boot showed a "BOOT ERROR" banner and red log lines `units/zz_temp_ghoul.json [ghoul] .hp: must be above 0` and `... .actions: has 4 actions; at most 3`, with no crash.

### Follow-ups (not in this phase)
- Damage types, armour mechanics beyond +HP, roles affecting battle, several weapon/armour slots (balance and later phases).
- Wisp, Ghoul, Wraith and Golem with their tomes; a real boss encounter.
- Unit icons (only the `icon` id exists), unit levels (out of scope).
