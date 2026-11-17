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
- [ ] JSON schema for units, actions and enemies.
- [ ] `UnitDefinition` classes and loader into the content database.
- [ ] Validation with clear errors.
- [ ] 3 to 4 player unit bases and the Graveyard and Woods enemy lists.
- [ ] Hook units to research unlocks (phase 7).

## Done when
- Adding a new unit to JSON makes it appear in the Unit Builder and in battle with no code change.
- A broken entry fails the loader with a readable message.

## Out of scope
Unit leveling, art references beyond an icon id, balance.
