# Phase 5: Workshop managers (CryptManager)

## Goal
The Crypt: storage and resources, plus the Workshop buildings that turn mana and souls into necromancers and coin.

## Depends on
Phases 1 to 4.

## CryptManager responsibilities
- Owns the Workshop building classes, ResearchTome classes (phase 7), Unit Builder (phase 9), and **Storage**.
- Storage: stock, cap and per-tick rate for each resource (the bottom resource bar reads this).
- Provides `Add(resourceId, amount)` (capped) and `TrySpend(...)` for everyone else.
- Does **not** own the necromancer pool (LichManager does). The Summoning Circle calls `LichManager.AddToPool`.

## Building classes
```csharp
public abstract class BuildingBase : ITickable, ISaveable {
    public string Id; public int Level;
    // staffed by necromancers through LichManager assignments
    public abstract void Tick(long ticks);
}
public class ManaWell : BuildingBase { }
public class SummoningCircle : BuildingBase { }
public class Infuser : BuildingBase { }
public class SoulWell : BuildingBase { }
public class Transmuter : BuildingBase { }
public class UndeadMarket : BuildingBase { }
```

## Flow order each tick
Mana Well, Infuser, Soul Well, Transmuter, Summoning Circle, Undead Market.

## Building roles (v0.1)
- **Mana Well:** produces mana. The player can click the well for click power, and assigned necromancers add output every tick. Level raises output and the maximum necromancers. No slot grid (does not scale to billions). See `adjustments/adjust-02-mana-well.md`.
- **Infuser:** turns mana plus a material into infused material.
- **Soul Well:** produces souls.
- **Transmuter:** resource conversion only for now.
- **Summoning Circle:** has several summon slots (more appear as they unlock, by level now and research later; locked slots are never shown). The player picks the unit for each slot by clicking its icon (tooltip shows costs). Each slot has a progress bar filled by **click power** (the player clicking the circle graphic) plus **necromancer power per tick** (assigned necromancers). A full bar spends the cost and summons one unit; summoned necromancers are added to the free pool through `LichManager.AddToPool`. See `adjustments/adjust-01-summoning-circle.md` and `ui-reference/images/02-workshop-summoning-circle.png` and the old-game screenshots.
- **Undead Market:** sells goods for coin. Coins buy upgrades for the Workshop, research and similar.
- Every building has its own mechanic and its own page; only Mana Well and Summoning Circle are required for the first slice.

## Upgrades
Coin purchases raise building level, output or storage cap. Upgrade definitions live in JSON (phase 11).

## Tasks
- [ ] `CryptManager` autoload and `Storage` (stock, cap, rate).
- [ ] `BuildingBase` and the six building classes (Mana Well and Summoning Circle first).
- [ ] Staffing through `LichManager` assignments (owner id = building id).
- [ ] Summoning Circle: slots, unit choice per slot, click power, necromancer power, progress and summon, adds to the pool through `LichManager.AddToPool`.
- [ ] `SummoningCircle.Click()` command for the circle graphic.
- [ ] Coin currency and upgrade purchase.
- [ ] Events for the resource bar and building pages.
- [ ] Save/load.

## Done when
- Mana Well produces mana, the Summoning Circle turns mana into necromancers, and the free count in the top bar goes up.
- Storage caps work; a full resource stops production without losing assigned necromancers.

## Out of scope
Final numbers, remaining building pages beyond the first slice, Undead Market item pricing.
