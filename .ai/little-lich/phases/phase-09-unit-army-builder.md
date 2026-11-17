# Phase 9: Unit Builder and Army Builder

## Goal
Players design unit types from items and unlocks, summon them, and arrange them into an army formation for battle.

## Depends on
Phases 5, 6, 7, 8. Final unit JSON is phase 10, so start with a few placeholder units.

## Unit Builder
- A **unit type** is a base body (skeleton, ghoul, and so on) plus a weapon, armour and optional items.
- Building a unit type makes it summonable in the Summoning Circle flow.
- **Every summon uses up its weapon, armour and items**, taken from inventory/storage.
- One loadout per unit type for now (per-stack loadouts are an open question).
- Open question: do summons also cost mana? Decide here.

## Army Builder (formation)
- Formation slots: 8 (2 front, 3 mid, 3 back) as planned, or 9. Decide before building the UI.
- Each slot holds a unit type and a count (stacks), since armies can be large.
- A formation is a saved loadout the player can reuse: `Formation { Id, Name, Slots[] }`.
- Section actions from phase 8 come from the units placed in each section.

## Classes
```csharp
public class UnitBuilder : ISaveable {
    public UnitType Build(string baseId, string weaponId, string armourId, string[] itemIds);
    public bool TrySummon(string unitTypeId, long count);   // spends gear, adds to roster
}
public class Roster : ISaveable { Dictionary<string,long> counts; }
public class Formation { string Id; FormationSlot[] Slots; }
```

## Tasks
- [ ] `UnitType` (base id, weapon, armour, items) and validation against the content database.
- [ ] Summon: checks stock, spends gear, adds to the roster.
- [ ] `Roster` with counts per unit type.
- [ ] `Formation` model, slot rules, and save/load.
- [ ] Hand a formation to `BattleController` (phase 8) to create army sections.
- [ ] Events for the UI.

## Done when
- The player builds a skeleton with a sword, summons 100, places them in the front row, and starts the Graveyard battle with them.
- Gear stock drops by exactly the amount used.

## Out of scope
Per-stack loadouts, unit upgrades, mana cost (unless decided in), unit leveling.
