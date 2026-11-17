# Phase 11: Item JSON

## Goal
Every item, resource and recipe is data, with abstract classes for reuse. This phase fills in the content that phases 4 to 9 used as placeholders.

## Depends on
All earlier phases (schemas must match what they read).

## File layout
```
Data/
  resources.json      # stored, capped resources (mana, souls, iron_ore, ...)
  items/
    weapons.json
    armour.json
    runes.json
    potions.json
    materials.json
  recipes/
    smithing.json
    alchemy.json
    enchanting.json
  activities/
    mining.json  fishing.json  hunting.json  herbalism.json
  upgrades.json       # coin purchases for Workshop and research
```

## Resource
```json
{ "id": "iron_ore", "name": "Iron Ore", "base_cap": 1000, "icon": "iron_ore" }
```

## Item (abstract base, subclass by type)
```json
{ "id": "iron_sword", "type": "weapon", "name": "Iron Sword",
  "stack_size": 999, "slot": "weapon", "stats": { "damage": 4 } }
```

## Recipe
```json
{ "id": "smith_iron_sword", "skill": "smithing", "level": 5,
  "inputs": { "iron_bar": 3 }, "output": { "iron_sword": 1 },
  "ticks": 4, "xp": 12 }
```

## Gathering activity
```json
{ "id": "iron_ore", "skill": "mining", "level": 10,
  "rate_per_tick": 1.0, "xp_per_unit": 5, "output": "iron_ore" }
```

## Rules
- Text ids are stable forever. Saves store ids and counts only.
- Abstract definition classes: `ItemDefinition` (weapon, armour, rune, potion, material), `RecipeDefinition`, `ActivityDefinition`, `ResourceDefinition`, `UpgradeDefinition`.
- Cross references are checked at load: every input, output, unlock and research reference must exist.
- Required-level and rate numbers are placeholders; keep them in one place so balance passes touch data only.
- Placeholders must be marked (`"placeholder": true`) so none ship unnoticed.

## Tasks
- [ ] Finalise schemas and abstract classes.
- [ ] Loader with cross-reference validation and readable errors.
- [ ] Ten or so items, resources and recipes per crafting skill, enough for the first slice.
- [ ] Mining activities and one activity for each other gathering skill.
- [ ] Workshop and research upgrades purchasable with coins.
- [ ] A content check command (`--validate-content`) that fails the build on errors.

## Done when
- Everything the earlier phases show in the UI comes from JSON.
- The validator catches a missing reference in a test file.

## Out of scope
Balance, localisation, art assets.
