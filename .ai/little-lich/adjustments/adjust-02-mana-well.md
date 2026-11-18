# Adjustment 02: Mana Well rework

Rebuild the Mana Well page to work like the old game. Same approach as the Summoning Circle (adjustment 01). Reference images:
- `ui-reference/images/01-workshop-mana-well.png` (the target, blocky)
- `ui-reference/old-game/03-old-game-mana-well.png` (how the old game looked: well graphic, `+8.00/s` output, necromancer stepper `4/4` with minus and plus, `Lvl 4 > Lvl 5` with costs and an UPGRADE button)

## What is wrong now
- The first mockup had a grid of channeling slots, an overflow chooser and a milestones list. Remove all three. The slot grid cannot scale to billions of necromancers.
- Nothing lets the player click the well.

## How it works
1. The page shows **the well** (a large graphic). **Clicking the well channels mana by hand** (`+1 mana per click`, from JSON).
2. **Necromancers assigned to the well** each add mana every tick (`+2.00 /tick` each as a placeholder).
3. **Output** is shown as `+8.00 /tick` (necromancers x per-necromancer output x level bonus x other modifiers). The game works in ticks, so show /tick, not /s.
4. A stepper (`- 4 / 6 +`) and the Assign by selector (1, 1k, 1M, All free) set how many necromancers are assigned. The number after the slash is the most the well can hold at its current level. For large counts show abbreviated numbers (4.2M / 10M) and exact values in a tooltip.
5. An **output breakdown** lists: per necromancer, necromancers x count, level bonus, click power.
6. A **level card** shows `Lvl N > Lvl N+1`, the output change (`+0.03% > +0.04%`), the costs as owned / needed (red when short, green when met) and an **UPGRADE** button. The button is disabled with a short reason ("Not enough mana yet") until every cost is met.
7. Mana stored shows as a bar against the cap. Mana is added to Storage through `CryptManager.Storage.Add` and stops at the cap.

## No locked icons
Same rule as the Summoning Circle: nothing locked is drawn. Features appear when unlocked, by level now and by research later.

## Dropped from the old mockup
Overflow routing and well milestones are removed from this page. Overflow just stops at the storage cap for now. If routing to the Infuser is wanted later, bring it back as its own adjustment.

## Data model (C#)
```csharp
public class ManaWell : BuildingBase {
    public int Level;
    public double ClickPower = 1;                        // mana per click
    public double OutputPerNecromancer = 2.0;            // per tick, placeholder from JSON
    public long AssignedNecromancers;                    // via LichManager.TryAssign("mana_well","main",n)
    public long MaxNecromancers => /* from level table in JSON */;

    public void Click();                                  // channels ClickPower mana now
    public double OutputPerTick();                        // necromancers x per-necro x level bonus x modifiers
    public bool CanUpgrade(out string reason);
    public bool Upgrade();                                // spends costs, raises Level
    public override void Tick(long ticks);                // adds OutputPerTick x ticks to Storage, capped
}
```
- The UI asks the manager for these numbers; it never calculates them itself.
- `Click()` and `Upgrade()` are commands. The UI calls them.
- `Tick(n)` must equal n single ticks (offline).

## JSON
```json
{ "id": "mana_well",
  "click_power": 1,
  "output_per_necromancer": 2.0,
  "levels": [
    { "level": 5, "max_necromancers": 6, "cost": { "mana": 610, "infused_mana": 300, "coin": 300 }, "output_bonus": 0.0004 }
  ] }
```
The level table keeps going. Numbers are placeholders.

## UI (match image 01)
- **Card A:** title `Mana Well` with RUNNING pill, one-line description, big clickable well block (`CLICK TO CHANNEL`, `+1 mana per click`), an OUTPUT box (`+8.00 /tick` in blue), and a Mana stored bar (`521 / 850`).
- **Card B (Necromancers):** description, a row with the necromancer block, `4 / 6` and the `-` `+` buttons, the Assign by selector, and the OUTPUT BREAKDOWN rows. Footer: "Free necromancers come from the Summoning Circle. The pool is shown in the top bar."
- **Card C (Well level):** `Lvl 4 > Lvl 5`, output change, cost rows, UPGRADE button, disabled reason text.
- Holding the mouse on the well repeats clicks (cap about 10 per second). Show a floating `+1` on each click.
- Blocky only. Shared components and tokens from `phases/phase-00-ui-spec.md`.

## Tasks
- [ ] Remove the slot grid, overflow chooser and milestones from the Mana Well page.
- [ ] Rework `ManaWell` to the data model above, with `Click()`, `OutputPerTick()`, `CanUpgrade()` and `Upgrade()`.
- [ ] Level table and click power in JSON.
- [ ] Batched `Tick(long ticks)` with a unit test (100 ticks in one call equals 100 single calls).
- [ ] Mana added to Storage, capped.
- [ ] Assign and unassign through `LichManager` (owner `mana_well`).
- [ ] Build Cards A, B, C and wire the well click, stepper, selector and upgrade.
- [ ] Screenshot compare with image 01.

## Done when
- Clicking the well raises mana by the click power and the resource bar updates.
- Assigning necromancers raises output per tick and lowers Free in the top bar.
- Upgrade is disabled until every cost is met, then spends the costs and raises the level and the max necromancers.
- 100 ticks in one call equals 100 single ticks.
- The page matches image 01 in layout, wording and colours.
