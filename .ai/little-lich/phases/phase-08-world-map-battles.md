# Phase 8: World map and battles

## Goal
A map with paths and two areas, and cooldown-based battles that feel like a battle.

## Depends on
Phases 2, 3, 5, 6. Units and armies (phases 9 and 10) are stubbed until they land.

## World map (v0.1)
- No rings. The map is a set of **paths** between nodes.
- Two areas only:
  - **Graveyard:** a small undead army belonging to another lich. The graveyard gates are the walls.
  - **Woods:** where the wild people live.
- Each area has defenders, a layout of the path, and a reward when cleared.
- The old ring, stronghold, layer and Dread/alarm ideas are parked until after v0.1.

## Battle model
- Units are grouped into **army sections** (front, middle, back, matching the formation).
- Each section has **up to 3 actions**, each with its own cooldown timer.
  - Example: front-middle swordsman attacks every 2 seconds and braces every 10 seconds (the next attack against it does 10% less damage).
- **HP is shown as bars**, one per section or unit group, not individual counters.
- Actions are data (JSON on the unit definition, phase 10): id, cooldown, effect type, value, targeting.
- Battle cooldowns are the one place a timer is shorter than a tick. Run the battle on its own clock while a battle is active, driven by the TickManager's sub-tick updates (the TickManager passes elapsed seconds). Outside battle, everything stays tick-based.

## Boss and dungeon encounters
The lich plus three summon slots plus the boss. Summons use up their weapon, armour and items from inventory when summoned.

## Who owns it
Open question: one `WorldManager` for sieges and encounters, or two classes. Recommendation: a `WorldManager` owning the map, plus a separate `BattleController` that is created per fight, runs the cooldowns, and returns a result.

## Open questions to settle first
- Can several Hosts fight on different paths at once (and when does it unlock)?
- Do the three summon slots grow later?
- Can the player restock inside a dungeon?

## Tasks
- [ ] `WorldManager` with map nodes, paths and area state (not started, in progress, cleared).
- [ ] Graveyard and Woods definitions in JSON.
- [ ] `BattleController` with sections, actions, cooldowns, HP bars and a result.
- [ ] Targeting rules and damage maths (brace reduces the next incoming attack by 10%).
- [ ] Send an army from the map; return survivors and rewards.
- [ ] Events for UI: bar changes, action fired, battle ended.
- [ ] Save/load of area state; battles are not saved mid-fight (restart on load).
- [ ] Dev battle scene to test with placeholder units.

## Done when
- A placeholder army can fight and clear the Graveyard, and the result is saved.
- Cooldowns tick at the right times and brace visibly reduces damage.

## Out of scope
Rings, layers, Dread, relief armies, boss mechanics beyond a basic boss.
