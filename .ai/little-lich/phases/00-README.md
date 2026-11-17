# Little Lich: Development Phases (v0.1)

Godot + C#. Each phase is one markdown file. Build in order; each phase ends with something that runs and can be tested before the next starts.

| Phase | File | Delivers |
|---|---|---|
| 1 | phase-01-game-manager.md | Startup, content loading, save/load, project skeleton |
| 2 | phase-02-lich-manager.md | Lich state, necromancer pool and assignments, buffs |
| 3 | phase-03-tick-manager.md | Tick loop, fixed processing order, tick bar, offline ticks |
| 4 | phase-04-skill-managers.md | Skill base classes, gathering and crafting skills |
| 5 | phase-05-workshop-managers.md | CryptManager, Workshop buildings, storage, resources |
| 6 | phase-06-lich-equipment-inventory.md | Lich gear, inventory, stat modifiers |
| 7 | phase-07-research-manager.md | ResearchManager, Necromancy 1 tree only |
| 8 | phase-08-world-map-battles.md | Map with paths, Graveyard and Woods, cooldown battles |
| 9 | phase-09-unit-army-builder.md | Unit Builder and army (formation) builder |
| 10 | phase-10-unit-json.md | Unit definitions in JSON |
| 11 | phase-11-item-json.md | Item, resource and recipe definitions in JSON |

## Rules that apply to every phase

- **Simulation is separate from UI.** State lives in plain C# classes. UI reads state, sends commands, never changes state directly. Signals/events tell the UI something changed.
- **Everything advances on the tick** (2 s at startup). No feature runs its own timer, except battle action cooldowns (see phase 8).
- **One class per feature.** Every skill, building, research tome and the spell book has its own class. Shared base classes: `SkillBase`, `GatheringSkill`, `CraftingSkill`, `BuildingBase`. Interfaces: `ITickable`, `ISaveable`.
- **Content is data.** Items, units, resources, research and recipes are JSON with stable text ids (`iron_ore`). Definitions are abstract classes with subclasses. Saves store ids and counts only.
- **Numbers.** Necromancers can reach 100 billion and XP is huge at level 999. Phase 1 decides the number type; every phase uses that type, never raw `long` for game quantities.

## v0.1 decisions (from the design doc)

- Summoning Circle summons necromancers into the free pool.
- XP: flat 10% more per level to 999, no easing.
- Several skills can train at once, based on how necromancers are assigned.
- No mastery. Coins buy upgrades for Workshop and research.
- World map: paths, two areas only: Graveyard (another lich's small undead army, gates are the walls) and Woods (wild people).
- Battles: up to 3 actions per army section, each on its own cooldown, HP shown as bars.
- Every summon uses up its weapon, armour and items.
- No prestige in v0.1.

## Still open (decide before the phase that needs it)

- Formation size, 8 or 9 slots (phase 9).
- Focus/Insight: budget or simple cap (phase 6).
- Several Hosts fighting at once (phase 8).
- Summons also cost mana? (phase 9)
- Summon slots growing beyond 3 (phase 8).
- Restocking inside a dungeon (phase 8).
- Health gives regeneration? (phase 2)
- ~~Big-number type (phase 1).~~ Decided: `Num` struct backed by `double`; counts and stock stay `long`.
