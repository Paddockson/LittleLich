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
- [x] `DevShell.tscn` with top bar, left list, tabbed main panel, right console, bottom resource bar.
- [x] `IDevPanel` and panel registry.
- [ ] Resource bar bound to Storage (stock, cap, rate). *Built from content now; stock and rate wait for Storage (phase 5).*
- [x] Top bar bound to TickManager and the necromancer pool (phase 3).
- [x] Debug console with the commands above and autocomplete (commands for unbuilt systems say which phase brings them).
- [x] Event log with category filters.
- [x] Generic assignment row control with `-`, `+`, `+10`, `+100`, `+Max` (plus `-All` and `+1K`).
- [x] Save/load panel.
- [ ] Add each new panel in the phase that creates its system.

## Done when
- Every system built so far can be viewed and driven from the dev UI without touching code or the editor.
- Adding a new activity or recipe to JSON shows up in its panel with no scene change.
- Everything is usable at 1280x720 with the default theme.

## Out of scope
Art, icons, custom fonts, themes, animation, the mockup layouts, mobile layout.

## Status: shell built (2026-11-17), after phase 2

The shell is in place with the Lich (phase 2) and Saves (phase 1) panels. Parts that need systems not built yet are marked in the task list and finish in the phases that build those systems.

### Decisions
- **Files:** everything is in `littlelich/UI/Dev/`: `DevShell.tscn`/`.cs`, `IDevPanel` + `DevPanelRegistry` (in `IDevPanel.cs`), `DevConsole`, `DevCommands`, `DevLog`, `DevFormat`, `AssignmentRow`, `LichPanel`, `SavePanel`. UI is built in code (no hand-edited scenes beyond the root).
- **Debug-only:** `UI/Main.tscn` (MainShell) switches to `DevShell` when `OS.IsDebugBuild()`. Release builds still show the phase 1 placeholder shell. The temporary lich panel added to MainShell in phase 2 was removed (it lives in the dev UI now).
- **`IDevPanel`** has `Title`, `Root` (the Control placed in the tab), `Bind()`, `Refresh()`. To add a panel: write the class, then add one line to `DevPanelRegistry.Panels`. The registry already has commented placeholders for phases 5–11.
- **Refresh:** panels refresh on manager signals. The shell also refreshes every panel after each console command. Nothing redraws per frame.
- **`AssignmentRow`** takes delegates (`current`, `add(n)`, `remove(n)`, optional `rate`), so phase 4 can pass `MiningManager.AddNecromancer` etc. The owner/activity constructor calls `LichManager` directly. `+` buttons are disabled when no necromancer is free; `-` buttons are disabled when none are assigned.
- **Numbers:** exact values (`1,234,567`) in panels and the log; the short form (`2.4B`) with the exact value in a tooltip in the top bar and assignment rows. Console counts accept `250`, `1,000`, `1.5K`, `2M`, `100B`.
- **Console commands now:** `help`, `clear`, `addnecro`, `assign`, `unassign`, `mod <stat> <add|mul> <value> [ticks]` (source `console`), `unmod [source]`, `damage`, `heal <n|full>`, `ticks <n>`, `save`, `load <slot>`, `newgame [slot]`, `wipe` (new game over the current slot; the old save becomes its backup), `delete <slot>`, `slots`. Listed but not built yet (each says which phase brings it): `give`, `xp`, `level`, `offline`, `speed`, `pause`, `resume`, `unlock`, `battle`. Tab completes command names, then arguments (content ids, stat ids, modifier sources, slots, current assignments); Up/Down walks history.
- **`ticks <n>`** was a stopgap (`GameManager.DebugRunTicks`) until phase 3; it now calls `TickManager.RunTicks` (full tick order). Phase 3 also made `offline`, `speed`, `pause` and `resume` real.
- **Event log categories:** Tick, Pool, Lich, Save, Content, Command, Error, each with its own colour and filter checkbox. Errors (including content/boot errors) are red, and boot errors also show as a banner. Pool logs assignment changes (from a new `AssignmentChanged` event) and total/max changes. The log keeps the last 2,000 entries.
- **Core additions for the dev UI** (unit tested): `SaveService.Delete(slot)`; `GameSession.DeleteSlot(slot)`, which refuses the current slot; `ContentDatabase.AllDefinitions()`; `NecromancerPool`/`LichState`/`LichManager` `AssignmentChanged(owner, activity, delta)`, which is not raised during load and is in sorted order for `UnassignOwner`; `GameManager.GameStarted(slot, kind)`, `DeleteSlot`, `DebugRunTicks`; `LichManager` forwards modifier and health commands.
- **Automation args** (after `--`): `--dev-cmd="cmd1;cmd2"`, `--dev-tab=Saves`, `--dev-screenshot=path.png` (saves a screenshot after 10 frames, then quits). Used to check the layout without clicking.

### Verified
- `dotnet test tests/LittleLich.Tests`: 78 tests (4 new: slot delete, refusing to delete the current slot, assignment event order and deltas, no events on load).
- Godot 4.4.1 at `--resolution 1280x720` with screenshots of the Lich tab after `addnecro 1000; assign ...; mod ...; damage 30; ticks 5; level mining 5; bogus` and of the Saves tab after `newgame`, `delete 3` (refused: current) and `delete 2`. Everything fits with no clipped text. Button rows wrap (`HFlowContainer`) because an unwrapped Saves button row pushed the console off-screen.
- Not checked by hand: Tab autocomplete and history keys (the code path is simple, but nobody has tried it interactively).

### Follow-ups
- ~~Phase 3: tick bar, pause/speed, `ticks`/`offline`/`speed`/`pause`/`resume`, Tick log entries.~~ Done in phase 3: per-frame bar and `0.7s/2.0s` text, Pause/Resume toggle, 1x/5x, tick counter; batches, offline recaps, pause and speed are logged (single live ticks are not, to keep the log readable).
- Phase 4: skills list buttons (`Name level/999`), skill pages replacing the main panel, `xp`/`level`, Level log category.
- Phase 5: resource bar stock/rate from Storage, `give`, Workshop panel.
