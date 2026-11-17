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
- [x] `WorldManager` with map nodes, paths and area state (not started, in progress, cleared).
- [x] Graveyard and Woods definitions in JSON.
- [x] `BattleController` with sections, actions, cooldowns, HP bars and a result.
- [x] Targeting rules and damage maths (brace reduces the next incoming attack by 10%).
- [x] Send an army from the map; return survivors and rewards.
- [x] Events for UI: bar changes, action fired, battle ended.
- [x] Save/load of area state; battles are not saved mid-fight (restart on load).
- [x] Dev battle scene to test with placeholder units.

## Done when
- A placeholder army can fight and clear the Graveyard, and the result is saved.
- Cooldowns tick at the right times and brace visibly reduces damage.

## Out of scope
Rings, layers, Dread, relief armies, boss mechanics beyond a basic boss.

## Status: complete (2026-11-17)

### Decisions
- **Open questions (user decisions):**
  - **One battle at a time** in v0.1.
  - **Area battles only** in this phase. The boss/dungeon encounter format (lich + 3 summon slots, **fixed at 3**) comes after phase 9, when summons exist.
  - **No restocking:** what the army brings is all it has (win, lose or retreat).
- **Owner (open question settled as recommended):**
  - `WorldManager` (new 5th autoload, between CryptManager and TickManager) wraps a plain `WorldState`: map, area states, garrison, active battle.
  - A `BattleController` is created per fight and returns an outcome.
  - Tick step 6 has nothing tick-based yet, so nothing registers in `TickGroup.World`.
- **Battle clock:** the one timer shorter than a tick.
  - `TickManager.GameSecondsElapsed(seconds)` fires every frame with real seconds x speed, and not while paused or offline. `WorldManager` feeds it to the battle.
  - The battle keeps time in whole milliseconds and fires actions in exact time order, so one 120 s step equals 120 one-second steps or 7,200 frame-sized steps (tested).
  - **Tie order:** attacker first, then front to back, group order, action order.
- **Pulled forward from phases 9/10 (stubs):**
  - **Placeholder `unit` content** (`Data/units.json`): `side` (player or enemy), `hp` per unit, and up to 3 `actions`, each `{id, name, cooldown s, effect attack|brace|heal, value, target front|back|lowest|self|ally_lowest, range melee|ranged}`. Player units: Skeleton (slash 3 / 2 s, brace 10% / 10 s, the spec's example) and Zombie. Enemies: Graveyard Gate (a wall: HP, no actions), Rival Skeleton, Grave Archer, Wild Brute, Wild Hunter, Wild Shaman (heals).
  - **Garrison:** unit counts by id in `WorldState`, saved. `Send` takes units out and survivors come back. Units are added with the debug command; phase 9 replaces it with summoning, stacks and formations.
- **Battle model:**
  - **Sections:** front, middle, back. Each army entry is a group (unit type + count) in a section.
  - **HP:** pooled per group (one bar); units standing = ceil(hp / unit hp).
  - **Actions:** fire every cooldown, the first after one cooldown. Damage and heal scale with units standing. Melee is only usable from the side's front-most living section; an overdue melee action fires the moment it becomes usable (e.g. when the gate falls).
  - **Brace:** the next incoming attack does `value` (10%) less, then the brace is used up.
  - **Ending:** a side with no one standing loses. Retreat ends it at once; the 300 s time limit counts as a defeat.
- **World map** (`Data/world.json`, `map_node` + `area` types): crypt (home) – crossroads – Graveyard Gates (`graveyard`) / Edge of the Woods (`woods`).
  - An area can be attacked when a path leads to it from home through nodes that hold no uncleared area.
  - Area state: NotStarted, InProgress, Cleared. Rewards are granted once on clearing (Graveyard: bones 60, souls 10, coins 300; Woods: wolf hide 40, nightshade 40, coins 200), capped by storage.
  - Validation: links, area references, defender units, rewards, every area placed on a node.
- **Save** (section `world`): cleared areas, the garrison, and the army out fighting. **Battles are not saved mid-fight:** on load the battle restarts from the beginning with the same army (the units stay out of the garrison, never doubled).
- **Signals on WorldManager:** `BattleStarted`, `BattleEnded(area, outcome)`, `BattleActionFired(side, unit, action, amount)`, `AreaStateChanged`, `GarrisonChanged`. C# events `ActionFired(BattleAction)` and `Resolved(BattleResult)` carry the full details.

### Verified
- `dotnet test tests/LittleLich.Tests`: 189 tests (17 new; the test project now also compiles `Scripts/World`). Covered:
  - **Done when:** 30 placeholder Skeletons clear the Graveyard (Victory, survivors home, rewards in storage, area Cleared, refusing a second attack). The clear and the garrison survive reload. Cooldowns fire at exactly 2, 3, 4, 6 and 6 s for 2 s and 3 s actions. Brace makes the next hit 9 instead of 10, once only.
  - **Also:** pooled HP and scaling damage; melee waiting behind the gate while ranged fires; healing the lowest ally; the time limit and retreat; chunked = single-step clocks; a weak army losing with the area left open; send rules (one at a time, garrison counts, enemy units refused, unknown area); reachability behind an uncleared area; a mid-fight save restarting on load; content validation.
- **Godot 4.4.1 at 1280x720:**
  - **First launch:** new game; `addunits skeleton 30; addunits zombie 6; send graveyard skeleton:30@front zombie:6@middle; speed 60`. The Battle tab showed group HP bars, cooldown bars, the gate nearly broken, and the log line "hits Skeleton for 10.8 (braced, -10%)".
  - **Second launch:** the reload restarted the battle, which finished at 100x: Victory after 26 s, 9 lost, rewards granted, Graveyard green on the Map.
  - **Third launch:** the clear and the garrison (21 Skeletons, 6 Zombies) were still there.
  - **Fixes:** the Map page now scrolls, and units with no actions show "no actions".

### Follow-ups (not in this phase)
- Phase 9: replace the garrison with summoned stacks and formations; Raise Skeletons / Raise Zombies research should gate them.
- Boss/dungeon encounters (lich + 3 fixed summon slots, gear used up per summon, a basic boss).
- Battle rewards beyond the first clear, scouting, retreat thresholds, damage types and armour (phase 10 unit schema).
- A battle in progress is not shown in the away recap; battles do not run offline.

> **Note (phase 9):** the garrison stub was replaced by the Unit Builder roster; armies are unit types sent from formations.
