---
name: little-lich-dev
description: Use when building, extending or reviewing code and data for Little Lich, the Godot C# incremental RPG. Covers architecture rules, phase order, JSON content conventions and the v0.1 scope.
---

# Little Lich development skill

Little Lich is an idle/incremental RPG in Godot with C#, modelled on Melvor Idle. A little lich, raised by Death, builds an undead army and claims the soul of the king who cheated death. Players automate by assigning necromancers.

## Before you write anything

1. Read `design/Little-Lich-Game-Design-Document.md` for the game design.
2. Find the current phase in `phases/` (01 to 11). Work on one phase at a time, in order.
3. Read that phase's file fully: goal, dependencies, tasks, "done when", out of scope.
4. When building any UI, read `phases/phase-00-ui-spec.md` and look at the matching screenshot in `ui-reference/images/` first.
5. If the work touches an item listed as open in `phases/00-README.md`, ask the user before deciding.

## Architecture rules (never break)

- **Simulation and UI are separate.** State lives in plain C# classes. UI reads state, sends commands, never mutates state. Notify the UI with signals/events.
- **Everything advances on the tick** (2 s at startup). No feature owns its own timer. The one exception is battle action cooldowns (phase 8), which run on the battle's own seconds clock.
- **Four managers (autoload singletons):**
  - `GameManager`: startup, content loading, saves, offline time.
  - `TickManager`: runs ticks in a fixed order, reads assignments, calls feature classes.
  - `LichManager`: skills, stats, buffs/debuffs, **the necromancer pool and every assignment**, lich save data.
  - `CryptManager`: Workshop buildings, Storage, research tomes, Unit Builder.
- **One class per feature.** Every skill, building, research tome and the spell book has its own class. Base classes: `SkillBase`, `GatheringSkill`, `CraftingSkill`, `BuildingBase`. Interfaces: `ITickable`, `ISaveable`.
- **Content is data.** Items, units, resources, research, recipes and activities are JSON with stable lowercase text ids (`iron_ore`). Definitions are abstract classes with subclasses chosen by a `type` field. Saves store ids and counts, never definitions.
- **Tick maths is batched.** `Tick(long ticks)` must give the same result for one call of 100 ticks as 100 calls of 1 tick, so offline progress works.
- **Numbers.** Use the `Num` wrapper (phase 1) for XP and big values. Necromancer counts and stock are `long`. Never use raw `int` for game quantities.

## Tick order

1. Counter and `TickStarted`.
2. LichManager: buffs expire, regeneration, cooldowns.
3. CryptManager buildings: Mana Well, Infuser, Soul Well, Transmuter, Summoning Circle, Undead Market.
4. Read assignments from LichManager, call skill and crafting classes.
5. Research benches.
6. World/battle step.
7. `TickEnded`, UI update, autosave check.

## Automation flow

UI press + on Iron Ore → `MiningManager.AddNecromancer("iron_ore", n)` → `LichManager.TryAssign("mining","iron_ore",n)` (returns how many were actually assigned) → MiningManager stores the count → each tick the TickManager reads assignments and calls `MiningManager.Tick` → count x rate x modifiers adds ore to Storage (capped) and adds XP.

## v0.1 scope

- Skills (to 999, flat 10% more XP per level): Health, Necromancy, Magic, Mining, Fishing, Hunting, Herbalism, Smithing, Alchemy, Enchanting.
- Summoning Circle summons necromancers into the free pool. Several skills can train at once.
- No mastery, no prestige, no rings. Coins buy Workshop and research upgrades.
- Research: Necromancy 1 only.
- World: paths with two areas, Graveyard (another lich's small undead army, gates as walls) and Woods (wild people).
- Battles: up to 3 actions per army section, each with its own cooldown; HP shown as bars. Every summon uses up its weapon, armour and items.

## Working rules

- Prefer small, testable steps. Each phase ends with something that runs.
- Write tests for pool maths, tick batching, save/load round trips and content validation.
- Placeholder numbers are fine but mark them `"placeholder": true` in JSON.
- When a phase finishes, tick its tasks and note decisions in its file.
- Do not add features outside the current phase; list them as follow-ups.
- Ask the user when design is unclear; do not invent mechanics that conflict with the design doc.
