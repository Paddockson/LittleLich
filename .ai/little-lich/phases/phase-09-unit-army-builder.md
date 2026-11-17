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
- [x] `UnitType` (base id, weapon, armour, items) and validation against the content database.
- [x] Summon: checks stock, spends gear, adds to the roster.
- [x] `Roster` with counts per unit type.
- [x] `Formation` model, slot rules, and save/load.
- [x] Hand a formation to `BattleController` (phase 8) to create army sections.
- [x] Events for the UI.

## Done when
- The player builds a skeleton with a sword, summons 100, places them in the front row, and starts the Graveyard battle with them.
- Gear stock drops by exactly the amount used.

## Out of scope
Per-stack loadouts, unit upgrades, mana cost (unless decided in), unit leveling.

## Status: complete (2026-11-17)

### Decisions
- **User decisions:**
  - **Formation:** 8 slots: 2 front, 3 middle, 3 back.
  - **Summons cost mana:** each player base has `mana_cost` (placeholder: Skeleton 5, Zombie 12) on top of its gear and items.
  - **Summoning both ways:** an instant `TrySummon(type, n)` (all or nothing), **and** the Summoning Circle keeping one unit type's stack at a target over ticks (design doc: "the Summoning Circle keeps each stack topped up to the target count").
- **Classes** (plain C#, `Scripts/Crypt/Units/Units.cs`; owned by the CryptManager as the design doc says; save section `units`):
  - `UnitType(Id, Name, Base, Weapon?, Armour?, Items)`: one loadout per type (v0.1). A different loadout is a different type, with automatic id `base[_weapon][_armour][_items]`; building the same loadout again returns the existing type.
  - `UnitBuilder`: types, the **roster** (the spec's `Roster`: counts per type) and formations.
  - `Formation { Id, Name, Slots[8] }`, `FormationSlot { Index, Section, UnitType, Count }`.
- **Build rules:**
  - The base must be a player unit and **not locked behind research**. Skeleton needs Raise Skeletons and Zombie needs Raise Zombies (phase 7's `unlock_unit`).
  - The weapon/armour must be **unit** gear (no lich slots), and items must be non-equippable items.
  - Results: `BuildResult` (UnknownBase, NotAPlayerUnit, BaseLocked, BadWeapon, BadArmour, BadItem).
- **Summon cost per unit:** mana, plus one weapon, one armour and one of each item, taken from storage and the inventory. `Affordable(type)` gives how many the stock pays for and what runs out first.
- **Battle stats per type** (placeholder rules until phase 10's unit schema): HP = base hp + armour defence x 5; each attack action does + weapon damage. Items are used up but have no battle effect yet (runes and traits later).
- **The roster replaces phase 8's garrison stub.**
  - `WorldState` takes units from the roster when sending and returns survivors to it. Battles use `UnitBuilder.BattleUnit(type)`.
  - `WorldState.PlanFor(formation)` turns a formation into an army: slots of one type in one section merge. `Send(area, formation)` was added.
  - **Old saves:** a phase 8 save's `garrison` and in-progress battle (base ids) move into gear-less types (id = base id). `addunits` (debug) adds to the roster without cost.
- **Summoning Circle unit mode:** `SetUnitTarget(type, target)`.
  - Work per summon = the base's `summon_work`, with the same costs as an instant summon.
  - It stops at the target (status PoolFull "target") and pauses when gear or mana runs out (MissingInput + id).
  - Batched = single ticks. `null` switches it back to necromancers. Saved with the circle; the Unit Builder loads before the buildings.
- **Safety:** a type can't be deleted while it has units in the roster, has units out fighting, or is in a formation slot.
- **Load:** types whose base or gear no longer exists are dropped (with their roster and slots).
- **Signals on CryptManager:** `UnitTypesChanged`, `UnitRosterChanged(type, count)`, `FormationsChanged`. The world's garrison signal was removed.

### Verified
- `dotnet test tests/LittleLich.Tests`: 203 tests (14 new; the phase 8 world tests were moved to the roster API). Covered:
  - **Done when:**
    1. Build a skeleton with a Copper Sword.
    2. Summon 100: exactly 100 swords (130 → 30) and 500 mana used.
    3. Place them in slot 1 (front) of a formation.
    4. Send it to the Graveyard: Victory, survivors back in the roster, area Cleared.
  - **Also:**
    - Build rules (research lock, enemy base, lich gear, wrong gear kind, resources as items); one type per loadout; costs per unit (an item listed twice costs 2).
    - All-or-nothing summoning; gear stats; the 8-slot layout and slot rules; plans merging slots; not enough units.
    - Delete rules; circle unit mode (tops up, stops at the target, pauses on missing gear, batched = single ticks).
    - Save/reload of types, roster, formations and the circle target; phase 8 garrison migration.
- **Godot 4.4.1 at 1280x720, three launches:**
  - **1. Build and summon:** new game; `unlock` the two research nodes; give 130 swords and 900 mana; `build skeleton copper_sword`; `summon ... 100`; build an armoured variant; `formation`; `slot`. The Units tab showed roster 100, swords left for 30 more, mana 400, the armoured variant at 50 hp, and Zombie dimmed as locked.
  - **2. Reload:** the Army tab showed the formation with slot 1 front = 100 and the front section's actions.
  - **3. Battle:** `battle graveyard formation_1` at 100x: Victory after 6 s, 3 lost, rewards granted, roster back to 97 (the Map tab warns the formation now needs 100).

### Follow-ups (not in this phase)
- Phase 10: the real unit schema (roles, damage types, armour mechanics, `weapon_slots`/`armour_slots`, reusable actions); move player bases and enemies into `Data/units/`.
- Re-equip units in the field, per-stack loadouts, unit upgrades and levels (out of scope).
- Several Summoning Circles (one per recipe); a Siege Host cap from research/Necromancy.
- Boss/dungeon encounters (lich + 3 fixed summon slots).
- The dev tab bar now scrolls (10 tabs); fine for the dev UI.
