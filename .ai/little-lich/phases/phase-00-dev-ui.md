# Phase 0: Basic development UI

## Goal
A plain, fast, usable UI for building and testing the game. No art, no theme, no animation. It exists so every later phase can be seen working and poked at. The polished UI from the mockups comes after the systems work.

## Depends on
Nothing for the shell. Each panel is added in the phase that creates its system.

## Principles
- **Function over looks.** Default Godot theme, standard controls (`Button`, `Label`, `ProgressBar`, `ItemList`, `Tree`, `TabContainer`). No custom styling.
- **Generated from data.** Lists of skills, activities, recipes, buildings, units and items are built from the content database. Adding JSON adds UI with no scene edits.
- **UI never mutates state.** Panels read state, call manager methods, and refresh on events. Same rule as the real UI.
- **Readable at 1280x720.** Containers and anchors only, no fixed positions. Scrolling lists, no text cut off.
- **Plain numbers.** Show exact values, with an abbreviated form only where space is tight (2.4B). Always show `/tick` rates.
- **Disposable.** Nothing here ships. Keep it in `UI/Dev/` so it can be deleted or kept behind a debug flag.

## Layout (one scene, one window)
```
+--------------------------------------------------------------+
| Tick bar  0.7s/2.0s  [Pause][1x][5x]   Free | Assigned | Max |
+----------+---------------------------------------+-----------+
| Skills   |  Main panel (TabContainer)            | Debug     |
| list     |  Skill page / Workshop / Research /   | console   |
| (button  |  Lich / Map / Units / Inventory       | + log     |
| per      |                                       |           |
| skill)   |                                       |           |
+----------+---------------------------------------+-----------+
| Resources: name  stock/cap  +rate/tick  (one line each)      |
+--------------------------------------------------------------+
```
- **Top bar:** tick progress bar, pause and speed buttons, necromancer counts (Free, Assigned, Max).
- **Left:** a button per skill showing `Name  level/999`. Selecting one shows its page in the main panel.
- **Main panel:** tabs for Crypt (Workshop buildings), Research, Lich, World Map, Units, Inventory. Skill pages replace the main panel when a skill is selected.
- **Right:** debug console and event log, collapsible.
- **Bottom:** all resources as a scrolling list or grid with stock, cap and rate per tick.

## Panels (added as each phase lands)
| Panel | Added in | Shows | Controls |
|---|---|---|---|
| Shell, top bar, resource bar | Phase 3 | Tick bar, pool, resources | Pause, speed |
| Save/load | Phase 1 | Slot list, last save time | New, Save, Load, Delete |
| Lich | Phase 2 | Stats, modifiers, assignments table | Add/remove necromancers (debug) |
| Gathering skill page | Phase 4 | Activities with level lock, assigned count, rate, XP | `-` `+` `+10` `+Max` per activity |
| Crafting skill page | Phase 4 | Recipes with inputs, output, state (running/missing input) | `-` `+` per recipe |
| Workshop | Phase 5 | A box per building: level, staffed, output, upgrade cost | Assign, upgrade |
| Inventory and gear | Phase 6 | Slots, stacks | Equip, unequip |
| Research | Phase 7 | Node list or tree with state | Start, assign bench |
| World map | Phase 8 | Areas and paths, state | Send army |
| Battle view | Phase 8 | Section HP bars, action cooldown bars, log | Start, stop |
| Units and formation | Phase 9 | Unit types, roster, formation slots | Build, summon, place |
| Content browser | Phase 10, 11 | Every unit, item, recipe, activity by id | Search, validation errors |

## Assignment control
Necromancers can reach 100 billion, so no per-necromancer icons. Each assignable row has:
- Current assigned count, abbreviated, with the exact value in a tooltip.
- `-`, `+`, `+10`, `+100`, `+1K`, `+Max` buttons (or a number box and Apply).
- Disabled `+` when free is zero.

## Debug console
A single `LineEdit` plus a log. Commands call the same manager methods as the UI, never edit state directly.
- `addnecro <n>`: add to the free pool.
- `give <resource|item> <amount>`
- `xp <skill> <amount>`, `level <skill> <n>`
- `ticks <n>`: run n ticks now (tests batching and offline).
- `offline <seconds>`: simulate time away and show the recap.
- `speed <x>`, `pause`, `resume`
- `save`, `load <slot>`, `wipe`
- `unlock <id>` (research, units, recipes)
- `battle <area>`: start a test battle.
- Autocomplete from content ids.

## Event log
Scrolling list of timestamped events: tick ended, assigned/unassigned, level up, capped, research done, battle action, save written, errors. Filter by category. Content validation errors appear here in red.

## Implementation notes
- One root scene `DevShell.tscn` with a script that builds panels from a registry.
- Each panel implements:
```csharp
public interface IDevPanel {
    string Title { get; }
    void Bind();        // subscribe to manager events
    void Refresh();     // redraw from current state
}
```
- Panels register themselves with `DevShell`. Phases add a panel by adding one class and one line in the registry.
- Refresh on events (`TickEnded`, `NecromancerPoolChanged`, and so on), not on every frame. The tick bar is the only per-frame update.
- Debug builds only: load `DevShell` as the main scene when `OS.IsDebugBuild()`; the real shell comes later.

## Tasks
- [ ] `DevShell.tscn` with top bar, left list, tabbed main panel, right console, bottom resource bar.
- [ ] `IDevPanel` and panel registry.
- [ ] Resource bar bound to Storage (stock, cap, rate).
- [ ] Top bar bound to TickManager and the necromancer pool.
- [ ] Debug console with the commands above and autocomplete.
- [ ] Event log with category filters.
- [ ] Generic assignment row control with `-`, `+`, `+10`, `+100`, `+Max`.
- [ ] Save/load panel.
- [ ] Add each new panel in the phase that creates its system.

## Done when
- Every system built so far can be viewed and driven from the dev UI without touching code or the editor.
- Adding a new activity or recipe to JSON shows up in its panel with no scene change.
- Everything is usable at 1280x720 with the default theme.

## Out of scope
Art, icons, custom fonts, themes, animation, the mockup layouts, mobile layout.
