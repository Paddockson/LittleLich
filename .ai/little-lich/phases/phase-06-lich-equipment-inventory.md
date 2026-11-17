# Phase 6: Lich equipment and inventory

## Goal
The lich can wear gear, gear changes stats, and items can be held, stacked and used.

## Depends on
Phases 2, 4, 5. Item definitions arrive fully in phase 11; use a few placeholder items here.

## Equipment
- Slots (placeholder, match the Lich page mockup): weapon, armour pieces, accessories.
- `Equip(slotId, itemId)`, `Unequip(slotId)`; only items whose definition allows that slot.
- Equipped items add stat modifiers through the LichManager modifier system (phase 2).

## Inventory
- Stacks by item id with counts; no per-instance data in v0.1.
- Shares **Storage** with resources? Decision: resources live in Storage (capped, shown in the bottom bar); equippable and consumable items live in the inventory.
- `Add`, `Remove`, `Count`, with events for the UI.
- Gear used by summons (phase 9) is spent from here.

## Item base classes
```csharp
public abstract class ItemDefinition : Definition { public int StackSize; }
public class WeaponDefinition : ItemDefinition { /* damage, slot */ }
public class ArmourDefinition : ItemDefinition { /* defence, slot */ }
public class RuneDefinition : ItemDefinition { /* attachable modifier */ }
public class PotionDefinition : ItemDefinition { /* effect */ }
```
Subclasses are created from JSON by a type field.

## Insight / Focus
Open question: budget or cap. Resolve here if spell equipping needs it; otherwise defer to the SpellBook phase after v0.1.

## Tasks
- [x] Equipment slots and equip/unequip with validation.
- [x] Modifier application and removal on equip changes.
- [x] Inventory stacks and events.
- [x] Item base classes (abstract) and loader hooks.
- [x] Save/load.
- [x] Debug command to grant items.

## Done when
- Equipping a weapon changes a derived stat; unequipping restores it.
- Inventory survives save and reload.

## Out of scope
Rune attachment, item upgrading, spell book content.

> **Note (phase 4):** a minimal stack-only `Inventory` (counts by item id, no space limit, no equipping), placeholder `item` definitions with a `kind`, and a dev Inventory tab already exist. Build on them: add item subclasses, inventory space, equipping and use.

## Status: complete (2026-11-17)

### Decisions
- **Insight / Focus: deferred** to the SpellBook phase after v0.1. Spells are not equipped in v0.1, which is the spec's condition for deferring. Gear can still raise `max_insight` as an ordinary stat.
- **Slots:** the Lich page mockup's seven (`Core/LichSlots`): hood, amulet, ring, robe, staff, grimoire, boots. Modifier source per slot: `gear:<slot>`.
- **Item classes** (abstract `ItemDefinitionBase` with `stack_size` default 100 and `sell_price`; the JSON `type` picks the subclass; `Kind` = type):
  - `EquippableDefinitionBase` (`slots` = lich slots it fits, `modifiers` = `{stat, op: add|mul, value}` while worn) with `WeaponDefinition` (`damage`), `ArmourDefinition` (`defence`) and `AccessoryDefinition` (amulet, ring, grimoire).
  - `RuneDefinition` (`trait`; attaching is out of scope), `PotionDefinition` (`heal`, `effects`, `duration_ticks`), `BombDefinition` (`damage`, for battles), `MaterialDefinition`.
  - This replaces the phase 4 placeholder `item` type with `kind`.
  - **Unit gear:** gear with no lich `slots` (the phase 4 `copper_sword`, `iron_armour`) is unit gear for phase 9 and cannot be worn by the lich.
- **Placeholder content** (`Data/items.json`): one lich item per slot (Bone Staff, Grave Hood, Rotting Robe with an add and a mul, Crypt Boots, Amulet of Ash, Ring of Whispers, Dusty Grimoire). Also a buff potion, Clarity Draught (+5 Insight, Mining x1.25 for 300 ticks), with an Alchemy recipe at level 10. Lich gear is not craftable yet (give it with the console).
- **Inventory** (built on the phase 4 one):
  - Space is counted in slots: an id with count c uses ceil(c / stack size) slots.
  - Capacity is `SlotCapacity`, set by Magical Storage (`base_inventory_slots` 40, +`inventory_slots_per_level` 10). It is unlimited before Magical Storage loads.
  - `Room(id)` = the rest of the last stack plus free slots x stack size. Crafting, the Market and equipment all respect it; crafting into a full inventory shows Output full.
  - Has `Add`/`TryRemove`/`Remove`/`Count` and events `Changed` and `CapacityChanged`. Stacks over capacity after a load are kept.
- **Equipment** (`Scripts/Gear/Equipment.cs`, save section `equipment`, owned by LichManager as the design doc says):
  - **Equip and unequip:** `Equip(slot, itemId)` takes one item from the inventory and swaps the old one back in; `Unequip(slot)` puts it back.
  - **Results:** `EquipResult` gives the reason for any refusal (`WrongSlot`, `NotEquippable`, `NotInInventory`, `UnknownSlot`, `InventoryFull` when the replaced item has no room). A refused swap changes nothing.
  - **`EquipAuto(item)`:** uses the first free slot the item fits.
  - **Modifiers:** each change replaces exactly that slot's `gear:<slot>` modifiers, adding new ones before removing old ones so max health never dips.
  - **On load:** worn items are re-validated (one that no longer fits goes back to the inventory; unknown ids are dropped), and every slot's modifiers are rebuilt from the definitions (stale saved ones are removed). Load raises one `Reloaded` event instead of one per slot.
- **Item use** (`ItemUser`): potions only in v0.1. A potion heals, and/or applies its effects as timed modifiers with source `potion:<id>`. Drinking the same potion again refreshes the duration instead of stacking. Bombs (battles) and runes (attachment) can't be used yet.
- **Boot:** `LichManager.InitializeGear(content, inventory)` runs after the Crypt and skills; equipment is registered (and so loaded) last.
- **Signals on LichManager:** `EquipmentChanged(slot)` (`""` = reloaded) and `ItemUsed(itemId)`.

### Verified
- `dotnet test tests/LittleLich.Tests`: 156 tests (17 new; the test project now also compiles `Scripts/Gear`). Covered:
  - **Done when:** equipping the Bone Staff raises Max Insight 10 → 12 and unequipping restores 10, with the item moving inventory → slot → inventory and no `gear:` modifiers left. Inventory and equipment survive save and reload: stacks, worn items, modifiers, health 137.5 not clamped during load, Insight 17 with the potion, 40 slots.
  - **Also:**
    - Add and mul gear stacking with the Health skill.
    - Every refusal reason; swap returning the old item; a swap into a full inventory refused with nothing changed; unequip refused when full; `EquipAuto`.
    - Stack-based space; crafting into a full inventory; Magical Storage slots.
    - Healing and buff potions (refresh, not stack; expire on time); only potions usable.
    - Load re-validating slots and removing stale gear modifiers; each item type loading as its class; bad slots, modifiers and potions rejected.
- **Godot 4.4.1 at 1280x720, new game via `--dev-cmd`:**
  - **Gear:** gave gear and potions; equipped staff, robe and amulet. Copper Sword was refused as unit gear, and Bone Staff to the hood was refused as wrong slot.
  - **Potions and stats:** drank a Clarity Draught; took 60 damage and drank Mend Flesh. Max Health 137.5, Max Insight 17, Max necromancers 15.
  - **Reload:** a second run with no commands showed identical slots, inventory and bonuses.
  - **Layout fixes:** the page was too wide (console pushed off-screen). Narrower columns, a wrapped hint and a shorter header fixed it.
  - **Shell fix:** the tick counter now refreshes when a game starts or loads.

### Follow-ups (not in this phase)
- Lich gear recipes (Smithing/Enchanting) and drops; rarity and the mockup's Loadouts / Auto Equip / Sell bar.
- Rune attachment, item upgrading, bombs in battles (phase 8), unit gear spent by summons (phase 9).
- Health regeneration as a gear stat: v0.1 has no regen, so not added.
- Insight budget vs cap: decide with the SpellBook.
