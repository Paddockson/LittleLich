# Adjustment 01: Summoning Circle rework

The current Summoning Circle (data model and UI) is wrong. Rebuild it as described here. Reference images:
- `ui-reference/images/02-workshop-summoning-circle.png` (the target, blocky)
- `ui-reference/old-game/01-old-game-summoning.png` and `02-old-game-summoning-unit-tooltips.png` (how the old game worked, structure and behaviour only, not the art)
- `ui-reference/old-game/00-current-dev-build-summoning-WRONG.png` (do not copy)

## What is wrong now
- Circles are assigned a recipe from a recipe list on the right. Wrong.
- Locked slots, locked recipes and a "Show locked" filter are shown. Wrong.
- There is no way to click the circle for click power.

## How it works
1. The page shows **the circle** (a large graphic). **Clicking the circle adds click power** to the unit being summoned.
2. The circle has **summon slots**. Each slot summons one chosen unit type.
3. **Clicking a slot's icon** opens a list of units to choose from. Hovering a unit shows a tooltip with its description and costs (owned / needed).
4. Each slot has a **progress bar with a percentage**. The bar fills from click power plus necromancer power each tick.
5. When a bar is full, the slot **spends the unit's cost and summons one unit**, then starts again.
6. Necromancers assigned to the circle add power each tick (`+0.2` each as placeholder). A stepper (`- 1 / 2 +`) and the Assign by selector (1, 1k, 1M, All) set how many are assigned.
7. A **level card** shows the circle level, the output change at the next level, costs and an **UPGRADE** button.
8. Summoned necromancers are added to the free pool (`LichManager.AddToPool`).

## No locked icons
Nothing locked is shown anywhere on this page.
- **Slots:** only unlocked slots are drawn. When a new slot unlocks, it appears. There is no "LOCK" placeholder and no "unlocks at level N" text.
- **Units in the chooser:** only unlocked units are listed. A unit appears when it is unlocked. No dimmed or locked entries and no "Show locked" toggle.
- **How things unlock:** for now by circle level and by default content. Eventually by **research** (see phase 7). Build it so the unlock source does not matter to the UI: the UI asks the manager "which slots and units are unlocked" and draws those.
- If nothing can be chosen yet, an empty slot still shows `PICK` and the chooser shows only what exists (Necromancer is always available at the start).

## Data model (C#)
```csharp
public class SummoningCircle : BuildingBase {
    public int Level;
    public double ClickPower = 1;                       // added per click
    public double PowerPerNecromancerPerTick = 0.2;     // placeholder, from JSON
    public List<SummonSlot> Slots;                      // only unlocked slots exist in this list
    public long AssignedNecromancers;                   // via LichManager.TryAssign("summoning_circle","main",n)

    public void Click();                                // adds ClickPower to the active slot
    public bool SetUnit(int slotIndex, string unitId);  // choose unit from the unlocked list
    public void ClearSlot(int slotIndex);
    public IReadOnlyList<UnitOption> AvailableUnits();  // unlocked only
    public override void Tick(long ticks);              // necromancer power, fill, summon
}

public class SummonSlot {
    public string UnitId;      // null when empty (shows PICK)
    public double Progress;    // 0 to cost-to-summon (SecondsPerUnit x power scale)
    public double Required;    // from the unit definition
}
```
- Slots unlock through `UnlockManager` or the circle level table in JSON (`slots_by_level`). Research effects add slots and units later (phase 7 effect types `unlock_summon_slot`, `unlock_unit`).
- `Click()` is a command. The UI calls it; the UI does not change progress itself.
- A slot that cannot pay its cost keeps its bar full and shows a waiting state until stock arrives.
- Slots fill in order or all at once? **Decision: all active slots fill in parallel, but each tick's power is split evenly across non-empty slots; click power goes to the first non-empty slot.** (Change if you prefer otherwise.)

## Tick maths
- Per tick power = `AssignedNecromancers x PowerPerNecromancerPerTick x modifiers`.
- Progress per slot += its share of power (plus any click power since the last tick).
- Summons per slot = floor(Progress / Required); subtract and spend cost for each summon, limited by stock.
- Must be batched: `Tick(n)` equals n single ticks (needed for offline).
- Necromancer units go to the pool; other units go to the roster.

## JSON
```json
{ "id": "summoning_circle",
  "slots_by_level": { "1": 2, "3": 3, "5": 4 },
  "click_power": 1,
  "power_per_necromancer": 0.2,
  "levels": [ { "level": 2, "cost": { "mana": 270, "infused_mana": 270, "coin": 100 }, "output_bonus": 0.0003 } ] }
```
Unit entries gain `summon_required` (power needed per unit, shown as time per unit in the UI) and `summon_cost` (resources and gear). Costs shown in the tooltip come from `summon_cost`.

## UI (match image 02)
- **Card A (circle):** title, RUNNING pill, description, big clickable circle block (`CLICK TO SUMMON`, `+1 power per click`), necromancer stepper `- 1 / 2 +` with `necromancers`, Assign by selector, footer with power per tick and click power.
- **Card B (slots):** one row per **existing** slot: unit icon button (44 px, opens the chooser), unit name and time per unit, progress bar with %, `x` clear button. Empty slot: `PICK`, yellow border, `Idle`.
- **Unit chooser:** popup under the slot, title `CHOOSE UNIT`, list of unlocked units with name and cost. Hover shows tooltip: name, description, cost lines `owned / needed` (red when short, green when met). Click selects and closes.
- **Card C (level):** `Lvl N > Lvl N+1`, output change, cost list with owned / needed, `UPGRADE` button (hold-to-confirm optional).
- Holding the mouse on the circle repeats clicks (about 10 per second cap). Show a small floating `+1` label on each click.
- Blocky only. No art. Use shared components and tokens from `phase-00-ui-spec.md`.

## Tasks
- [ ] Remove the old circle-with-recipe UI and the recipe list.
- [ ] Rework `SummoningCircle` to the data model above; remove circle-recipe assignment.
- [ ] `Click()` command and `SetUnit` / `ClearSlot` commands.
- [ ] `AvailableUnits()` and slot list return unlocked items only.
- [ ] Tick maths, batched, with a unit test: 100 ticks as one call equals 100 single calls.
- [ ] Spend cost on summon, wait state when unaffordable, necromancers to the pool through `LichManager.AddToPool`.
- [ ] Save/load: level, slot unit ids, progress, assigned count.
- [ ] Build Cards A, B, C and the chooser popup and tooltip.
- [ ] Wire the circle click, slot icon click, stepper, selector and upgrade.
- [ ] Add the placeholder JSON and units (Necromancer, Wisp, Skeleton).
- [ ] Screenshot compare with image 02.

## Done when
- Clicking the circle visibly advances the active slot's bar.
- Clicking a slot icon opens the chooser; choosing Skeleton fills the slot; hovering shows costs.
- A full bar summons a unit and spends its cost; summoned necromancers raise Free in the top bar.
- Raising the circle level (or later, completing research) makes a new slot or unit appear with no locked placeholder shown.
- 100 ticks in one call equals 100 single ticks.
- The page matches image 02 in layout, wording and colours.
