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
- [ ] `CryptManager` autoload and `Storage` (stock, cap, rate).
- [ ] `BuildingBase` and the six building classes (Mana Well and Summoning Circle first).
- [ ] Staffing through `LichManager` assignments (owner id = building id).
- [ ] Summoning Circle adds to the pool through `LichManager.AddToPool`.
- [ ] Coin currency and upgrade purchase.
- [ ] Events for the resource bar and building pages.
- [ ] Save/load.

## Done when
- Mana Well produces mana, the Summoning Circle turns mana into necromancers, and the free count in the top bar goes up.
- Storage caps work; a full resource stops production without losing assigned necromancers.

## Out of scope
Final numbers, remaining building pages beyond the first slice, Undead Market item pricing.

> **Note (phase 4):** a minimal `CryptManager` with `Storage` (resources: stock, base cap, net rate per tick) and `Stockpile` already exists (`Scripts/Crypt`, `Scripts/Managers/CryptManager.cs`). Build on it: add cap upgrades, buildings and the Workshop panel.
