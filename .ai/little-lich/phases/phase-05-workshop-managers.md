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
- **Mana Well:** produces mana. Level and staffed necromancer count drive output (the mockup grid does not scale to billions; use level + necromancer count).
- **Infuser:** turns mana plus a material into infused material.
- **Soul Well:** produces souls.
- **Transmuter:** resource conversion only for now.
- **Summoning Circle:** spends mana and souls to summon necromancers, adds them to the free pool, ready for automation.
- **Undead Market:** sells goods for coin. Coins buy upgrades for the Workshop, research and similar.
- Every building has its own mechanic and its own page; only Mana Well and Summoning Circle are required for the first slice.

## Upgrades
Coin purchases raise building level, output or storage cap. Upgrade definitions live in JSON (phase 11).

## Tasks
- [x] `CryptManager` autoload and `Storage` (stock, cap, rate).
- [x] `BuildingBase` and the six building classes (Mana Well and Summoning Circle first).
- [x] Staffing through `LichManager` assignments (owner id = building id).
- [x] Summoning Circle adds to the pool through `LichManager.AddToPool`.
- [x] Coin currency and upgrade purchase.
- [x] Events for the resource bar and building pages.
- [x] Save/load.

## Done when
- Mana Well produces mana, the Summoning Circle turns mana into necromancers, and the free count in the top bar goes up.
- Storage caps work; a full resource stops production without losing assigned necromancers.

## Out of scope
Final numbers, remaining building pages beyond the first slice, Undead Market item pricing.

> **Note (phase 4):** a minimal `CryptManager` with `Storage` (resources: stock, base cap, net rate per tick) and `Stockpile` already exists (`Scripts/Crypt`, `Scripts/Managers/CryptManager.cs`). Build on it: add cap upgrades, buildings and the Workshop panel.

## Status: complete (2026-11-17)

Building mechanics in the design doc are proposals; v0.1 uses the spec's roles with simple, data-driven placeholder numbers (`Data/buildings.json`, `Data/workshop_recipes.json`).

### Decisions
- **Built on the phase 4 minimal Crypt.**
  - `CryptManager.Initialize(content, lich)` creates Storage, Inventory and the seven buildings, registers the ticking ones in `TickGroup.Buildings` in flow order (100 … 600), and returns all Crypt saveables.
  - **Boot load order:** lich, storage, inventory, buildings, skills.
  - `CryptManager.Add` / `TrySpend` are the shared stock API; `Storage.TrySpend` spends exactly or nothing.
- **Classes** (plain C#, `Scripts/Crypt/Buildings/`): `BuildingBase` plus `ManaWell`, `Infuser`, `SoulWell`, `Transmuter`, `SummoningCircle`, `UndeadMarket` and `MagicalStorage` (the design doc lists Magical Storage as a Workshop building; it raises caps and does not tick). `ConvertingBuilding` holds the shared conversion maths for Infuser, Soul Well and Transmuter. `BuildingFactory.CreateAll` makes them in flow order. Each building saves its own section, `building_<id>`.
- **Shared tuning** (`BuildingDefinitionBase`, one JSON type per building kind):
  - Work per tick = (`base_work` + staffed x `work_per_necromancer`) x (1 + `level_bonus` x (level − 1)).
  - Slots = `base_slots` + `slots_per_level` x (level − 1).
  - Upgrade cost in coins = `upgrade_cost` x `upgrade_growth`^(level − 1).
- **Staffing** goes through LichManager assignments, with owner = building id and activity = `"main"`. The pool stays the single source of truth. `AddNecromancer` is limited by free slots and returns how many were staffed.
- **Bootstrapping** (not in the spec; needed because a new game has 0 necromancers): the Mana Well (1 mana/tick) and Summoning Circle (0.2 work/tick) have base work, so they run unstaffed. A new game summons its first necromancer at tick 49 (25 mana to afford it plus 5 work at 0.2/tick, overlapping by one tick).
- **Building roles (v0.1):**
  - **Mana Well:** 1 work = 1 mana.
  - **Infuser:** 2 mana → 1 infused mana per 1 work.
  - **Soul Well:** 5 infused mana → 1 soul per 2 work. This follows the design doc's chain (the spec said "produces souls").
  - **Transmuter:** runs one chosen `transmutation`; resources only, enforced by content validation (the v0.1 decision on the Smithing/Transmuter overlap).
  - **Summoning Circle:** runs the first `summon` recipe (necromancer: 25 mana, 5 work) and adds to the pool through `LichState.AddToPool`. It stops at the pool max or at the player's **target** total (0 = up to max). Each level adds `max_necromancers_per_level` (10) as a `building:summoning_circle` modifier on `max_necromancers`, added before the old one is removed. One circle only; unit recipes and several circles come in phase 9.
  - **Undead Market:** 1 work sells 1 unit from the **sell list** (in order, keeping each line's reserve) at its `sell_price` (new optional field on resources and items, placeholder prices). It stops when coins are full; goods are never lost.
  - **Magical Storage:** each level adds `cap_bonus_per_level` (0.5) x base cap to every resource through `Storage.CapMultiplier`. Stock above a cap is kept.
- **Status per building:** `Running`, `Idle`, `MissingInput` (+ input id), `OutputFull` (+ id), `NothingToDo` (no recipe or nothing to sell), `PoolFull` (+ `max`/`target`) or `Passive`. A full output or missing input pauses the building, resets its progress, and keeps its staff. Statuses are not saved; they are recomputed on the next tick.
- **Exact maths:** `Core/FixedPoint` (moved out of the skills, which now use it too) runs every building in Int128 micro-units. Each building on its own gives the same result for `Tick(n)` as for n x `Tick(1)`. Chains across buildings (well → infuser → soul well) are batched in flow order, as agreed in phase 3.
- **Signals on CryptManager:** `StockChanged`, `InventoryChanged`, `StorageRatesUpdated`, `StorageCapsChanged`, `BuildingLevelChanged`, `BuildingStatusChanged`, `BuildingStaffChanged`.
- **Upgrade definitions:** the spec says these live in JSON. For now they are the building's own fields (`upgrade_cost`, `upgrade_growth`, `max_level`); phase 11 can split them into `upgrade` definitions.

### Verified
- `dotnet test tests/LittleLich.Tests`: 139 tests (20 new; the test world now includes the buildings). Covered:
  - **Done when:** from a new game with no necromancers, the Mana Well makes mana and the Summoning Circle turns it into a free necromancer at tick 49. Staffing the well raises output, and the free count keeps rising. A full mana store stops the well, its 5 staff stay assigned, and production resumes when there's room.
  - **Also:**
    - Summoning stops at the target and at the max; circle levels raise Max.
    - Slot limits; upgrade costs and growth; Magical Storage caps.
    - The Infuser → Soul Well chain; missing-input pauses, and idle (not "missing") when unstaffed.
    - Transmuter recipe choice.
    - Market reserves, coin cap and item sales.
    - Every building matches live vs batched on its own at 1, 37 and 400 ticks.
    - Save/reload of levels, staff, target, recipe and sell list, including derived effects (Max, caps) and identical continuation.
    - Content validation (transmutations resources-only, unknown building ids, unsupported summons).
- **Godot 4.4.1 at 1280x720, new game via `--dev-cmd`:**
  - 49 ticks gave the first necromancer.
  - It staffed the well, and the Market sold 15 iron bars (keeping a reserve of 5) for 180 coins.
  - That paid for Mana Well level 2 and Summoning Circle level 2 (Max 10 → 20).
  - After 30 more ticks the pool was 4.
  - Screenshots of the Mana Well, Summoning Circle (target reached) and Undead Market pages. Fixed on the way: labels wrapping one letter per line inside rows, building names cut off in the list, an unstaffed converter shown as "missing input", and the post-load idle text.

### Follow-ups (not in this phase)
- Mana Well overflow choice (Infuser / Summoning Circle / discard) and the slot grid UI from the mockup.
- Several Summoning Circles, unit recipes and stack targets (phase 9); Infuser elements and Soul Well grades.
- Storage tiers per resource and item inventory space (phase 6).
- Research gating of buildings (Infusion etc., phase 7).
- Balance: every number in `buildings.json` and `workshop_recipes.json` is a placeholder.
