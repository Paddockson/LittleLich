# Adjustment 03: Army Builder screen

Build the Army Builder as a planning screen. It replaces the formation part of `dev-phases/phase-09-unit-army-builder.md` (which says 8 slots and stacks with counts). Reference images:
- `ui-reference/images/14-army-builder.png` (the target, blocky)
- `ui-reference/html/14-army-builder.html` (exact CSS and layout; open it in a browser, the layout comes from this file)

Follow `skills/little-lich-dev/SKILL.md`: the UI never changes game state itself, it only calls commands on the manager and redraws when an event fires.

## What the screen is
A planning screen only. The player looks at the units they have unlocked and drags them into a 3 by 3 formation. That is all.

It does **not** have: a summoning bar, a battle simulation, a reset button, presets, or any locked icons. Do not add them. If something is not unlocked it is simply not shown.

## Layout
Two columns inside the Army page of the Crypt, under the normal top bar.

```
+-----------------------------------------------------------+
| Units box (left)  |  Back      Mid       Front            |
|  [Base | Custom]  |  [slot]    [slot]    [slot]   row 1   |
|  unit card        |  [slot]    [slot]    [slot]   row 2   |
|  unit card        |  [slot]    [slot]    [slot]   row 3   |
|  unit card        |  hint line                            |
|  ...              |  +----------------------------------+ |
|                   |  | Details box (3 columns)          | |
|                   |  +----------------------------------+ |
+-----------------------------------------------------------+
```

The mockup is 680 px wide. Keep the proportions when you fit it to the page content area from `phase-00-ui-spec.md`:
- Units box: about 25% of the width, full height of the page area, scrolls inside if the list is long.
- Formation: the rest of the width. Three equal columns with a 6 px gap (scale the gap). Slot height about 70 of 680 px width.
- Details box: directly under the hint line, full width of the formation column, minimum height about 118.

Use the existing `Tokens` class from phase 0 for colours, borders, font sizes and radii. Map by role, do not invent new values:
- Panel (Units box, Details box): surface-1 fill, 0.5 px strong border, 6 px radius, 8 px padding.
- Unit card: surface-2 fill, strong border, 6 px radius. Hover and selected: accent border; selected also accent fill.
- Filled slot: accent fill, accent border, accent text, solid border.
- Empty slot: no fill, dashed strong border, muted text "Empty".
- Secondary text is the 11 px tag style.

## Units box (far left)
- Title `Units`.
- A two-part toggle: `Base` and `Custom`. The selected part has the accent fill. It switches which list is shown.
- Cards, one per unit, in this order: name (medium weight), then one line `Type · HP n` (for example `Melee · HP 60`).
- `Base` lists the unlocked base units. `Custom` lists the units the player built in the Unit Builder.
- Only unlocked units appear. Units appear when research unlocks them.
- Empty custom list shows `No custom units yet. Build them in the Unit Builder.`
- A card is draggable. Clicking a card selects it (click again to deselect).

## Formation
- Column headers `Back`, `Mid`, `Front` above the grid. The enemy is on the right of the screen, so Front is the right-hand column.
- Nine slots. Index = `row * 3 + col`, where col 0 is Back and col 2 is Front. Row 0 is the top.
- A filled slot shows the unit name (medium weight), the type line below it, and a small `x` in the top right corner.
- Drag a card from the Units box onto a slot to place it. The same unit can go in more than one slot.
- Drag a filled slot onto another slot to swap the two. Dragging onto an empty slot moves the unit.
- Click a slot while a card is selected to place it there (this works without drag and drop, for touch). Clear the selection after placing.
- The `x` clears the slot.
- While dragging over a slot, outline it with the accent colour.
- A hint line under the grid: `Drag a unit onto a slot, or click a unit then a slot. Drag between slots to swap.`

## Details box (below the slots)
Fills when the pointer is over a unit, either a card in the Units box or a filled slot. Nothing needs to be clicked. When the pointer leaves, show the empty state: title `Details` and the text `Hover over a unit to see its stats and actions.`

Three equal columns:
1. **Name and stats.** Name (title style). Under it `Base · Melee` or `Custom · Swordsman + iron plate` (custom units show what they were built from). Then rows with the label on the left and the value on the right: `Health`, `Damage`, `Attack speed` (shown as `2s`).
2. **Actions.** Title `Actions`. One block per action (maximum 3): the action name in medium weight with `every Ns` on the right, and a one-line description under it.
3. **Supply.** Title `Supply`. `Uses` then what one summon spends (for example `sword, 2 bones`). Then rows `Stock` (how many of the unit the Crypt can still supply) and `In formation` (`3 of 9`, the number of slots holding this unit).

## Data model (C#)
```csharp
public class ArmyFormation : ISaveable {
    public string[] Slots = new string[9];   // unit type id, or null. Index = row * 3 + col (col 0 = Back, 2 = Front)
}

public class UnitDetails {                    // read-only view for the Details box
    public string Id, Name, Type;             // Type: Melee, Ranged, Magic
    public bool IsCustom;
    public string BuiltFrom;                  // custom units only, for example "Swordsman + iron plate"
    public double Health, Damage, AttackSpeed;
    public UnitAction[] Actions;              // 1 to 3
    public string SummonCostText;             // "sword, 2 bones"
    public long Stock;
}
public class UnitAction { public string Name, Description; public double CooldownSeconds; }
```

Commands and queries on the Unit Builder (it lives in `CryptManager`). The UI calls these and nothing else:
```csharp
IReadOnlyList<string> GetUnlockedUnitIds(bool custom);
UnitDetails GetUnitDetails(string unitId);
string GetSlot(int index);
int CountInFormation(string unitId);
bool SetSlot(int index, string unitId);       // false if the unit is not unlocked
void ClearSlot(int index);
void SwapSlots(int a, int b);
event Action FormationChanged;                // redraw slots and, if open, the details box
event Action UnlockedUnitsChanged;            // redraw the Units box
```

Rules enforced in the manager, not in the UI:
- `SetSlot` refuses a unit that is not unlocked.
- Swapping or moving never loses a unit; an empty slot is a valid value.
- The formation saves and loads with the game.

Unit JSON gains an `actions` list in phase 10 (up to 3 entries with `name`, `description`, `cooldownSeconds`). Until then, fill the Details box from placeholder data. Do not block this screen on phase 10.

## Game rules this screen sits on (not UI)
These are for the managers, not the screen. The screen never shows them.
- Each filled slot is a squad of up to 5 figures in battle.
- The lich keeps the army alive: whenever a squad is below 5 figures, the lich summons a replacement. **Base summoning speed is one unit every 4 seconds** (`LichManager`, a modifier target so research and skills can speed it up).
- Each summon uses up one unit's weapon, armour and items from the Crypt stock. If the stock is zero, that unit is skipped.
- The lich fills the slot that is missing the most figures first. On a tie, the more forward column (Front before Mid before Back) goes first.
- A newly placed slot starts at 0 figures and fills as the lich summons.
- Open question: does a summon replace one figure or a whole squad? Build it as one figure per summon, behind a single constant, so it is easy to change.

## Tasks
- [x] `ArmyFormation` with save and load, and the nine-slot rules.
- [x] Commands and queries above on the Unit Builder, and the two events.
- [x] `UnitDetails` view built from the content database (placeholder actions for now).
- [x] Army page in the Crypt tab bar, with the Units box, the toggle and the card list.
- [x] Formation grid with drag and drop, swap, click to place, and clear.
- [x] Details box that follows the pointer, with the empty state.
- [x] Hand the formation to `BattleController` (phase 8) to create the army sections.
- [x] Lich summoning at the 4 second base rate, as described above, with a unit test for the slot choice and the stock rule.

## Done when
- The Army page matches `ui-reference/images/14-army-builder.png`.
- Unlocked base units show under `Base`, built units under `Custom`, and nothing locked is drawn.
- A unit can be dragged into any slot, swapped, moved and cleared, and a card can be placed by click.
- Hovering a card or a filled slot fills the Details box with stats, actions and supply. Moving away shows the empty state.
- Quitting and reloading restores the formation.
- With a formation set, the lich refills missing figures every 4 seconds and the gear stock drops by exactly the amount used.

## Out of scope
Presets, a summoning bar or simulation on this screen, per-slot summon priorities, warnings about low stock, the Unit Builder screen itself, unit levelling.

## Decisions (implemented 2026-11-18)
- **The formation is the army; the roster is gone** (asked the user). `UnitBuilder` no longer keeps a roster or the 8-slot `Formation`s; the World map sends the 3 by 3 formation's squads as they stand. The Summoning Circle summons necromancers only; army units (wisps, skeletons, built loadouts) come from the lich. Old saves keep their unit types; their roster, old formations, garrison and battle army are dropped.
- **The lich summons all the time** (asked the user): `LichSummoner` runs on the tick (world step, before the battle), at home, during battle and offline. One figure every 4 s (`BaseSecondsPerSummon`), stat `lich_summon_speed` (multiplier, base 1), `FiguresPerSummon = 1` is the single constant for the open question. When nothing can be summoned the progress waits, full, for one summon. Progress saves with the army.
- **Squads in battle:** each filled slot is one battle group (section from its column) with capacity 5; the HP bar is out of five. New figures join the running battle (`BattleController.Reinforce`) and losses are written back to the squads as the fight runs. Healing never raises a squad past the figures it fielded. A slot with 0 figures still enters the battle and can be refilled; sending needs at least one figure standing.
- **The formation is locked while a battle runs** (`SetSlot` returns false, clear and swap do nothing; the page shows a toast). A battle still restarts from the beginning on load, now with the squads as saved.
- **Base vs custom:** a base unit's id is its body id (`skeleton`) and needs no stored type; a custom unit is a built type with gear or items. A gear-less build counts as the base unit.
- **Placeholder action text** is generated from the action's effect (`UnitBuilder.Describe`), since action JSON has no description yet.
- **Mapping to tokens:** panel = BgCard / BorderCard, unit card = BgCardAlt, accent = SelectedBg / SelectedBorder / Green, radius = RadiusButton (smallest token), borders 1 px (Godot has no 0.5 px). Geometry is the mockup times 1030 / 680. The Army tab sits after Unit Builder; the capture run saves it as `15-army-builder.png` (14 is Settings).
- Follow-ups: the battle view's section panels show the first four squads only; the Unit Builder could link straight to placing its loadout.
