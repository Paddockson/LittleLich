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
- [ ] Equipment slots and equip/unequip with validation.
- [ ] Modifier application and removal on equip changes.
- [ ] Inventory stacks and events.
- [ ] Item base classes (abstract) and loader hooks.
- [ ] Save/load.
- [ ] Debug command to grant items.

## Done when
- Equipping a weapon changes a derived stat; unequipping restores it.
- Inventory survives save and reload.

## Out of scope
Rune attachment, item upgrading, spell book content.
