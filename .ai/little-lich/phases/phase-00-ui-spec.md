# Little Lich: UI build spec (blocky version of the mockups)

For Claude Opus (or any developer) to build in Godot 4 + C#. This replaces the loose description in `phase-00-dev-ui.md` as the thing to build. Keep that file's rules (UI reads state, never mutates it; refresh on events).

## Goal

Build the UI exactly as laid out in the Design canvas mockups, but **blocky and plain**: rectangles, text, bars and buttons only. No art, icons, animation, or custom shaders. The purpose is to test the game and improve it, so every screen must be easy to read and every number must come from real game state.

"Exactly like the mockups" means: same screens, same positions, same panels, same wording, same colours, same behaviour. Not the same polish.

Source mockups (Design canvas "Little Lich: Battle Screen UI"): Workshop (Mana Well), WorkshopSummoning, Research, UnitBuilder, SpellBook, Lich, WorldMap, BossPrep, BossFight, Mining, Smithing, Alchemy, Enchanting. Main and Formation are old and outdated; ignore them.

## Reference images

Screenshots of every screen are in `ui-reference/images/` (see `ui-reference/README.md` for the index and the differences to apply). **Look at the matching image before building each page.** `ui-reference/html/` holds each board as a static page with the exact CSS (sizes, spacing, hex values) to copy from. Where an image and this text disagree on layout, follow the image.

## Hard rules

1. **One window, one shell, no loading screens.** Pages swap inside the content area.
2. **Reference size 1280 x 760.** Set the project viewport to 1280x760, stretch mode `canvas_items`, aspect `expand`. All layout below is in that reference space, built with containers and anchors so it stretches.
3. **Blocky style only.** `PanelContainer` + `StyleBoxFlat` (flat colour, 1px border, 8 to 12 px corner radius is fine, or 0 for even blockier), `Label`, `Button`, `ProgressBar` (flat), `HBox/VBox/GridContainer`, `ScrollContainer`, `TabContainer` or button rows. No textures.
4. **Abbreviate big numbers** (2.4B, 97.6B, 12.5k, 1.2M). Exact value in a tooltip. Necromancers reach 100 billion, so never draw per-necromancer icons.
5. **All values are live** from managers (`LichManager`, `CryptManager`, `TickManager`, skill classes). Where a manager does not exist yet, use a `MockData` class with the placeholder values from this spec, behind the same interface, so it can be swapped.
6. **UI never changes state directly.** Buttons call manager methods. Refresh on events (`TickEnded`, `NecromancerPoolChanged`, `LevelChanged`...). Only the tick bar updates every frame.
7. **Build from data.** Skill lists, activities, recipes, buildings, tomes, units and items are generated from the content database, so adding JSON adds UI.
8. **All numbers in mockups are placeholders.** Names, stats and costs come from JSON later.

## Design tokens

Define these once in `UI/Theme/Tokens.cs` (static class) and use everywhere. No colour literals in screens.

### Colours
| Token | Hex | Use |
|---|---|---|
| BgApp | #0D110F | Window background |
| BgBar | #101512 | Top bar and resource bar |
| BgPanelDark | #0F1411 | Sidebar, Crypt tab row, inset boxes |
| BgCard | #121815 | Cards and panels |
| BgCardAlt | #141A17 | Resource boxes |
| BgInset | #0F1411 | Rows inside cards |
| Border | #26302B | Default 1px border |
| BorderCard | #2A352F | Card border |
| TextMain | #E8E4D6 | Primary text (bone) |
| TextSoft | #C9D2CB | Buttons, secondary |
| TextMuted | #9FB0A6 | Descriptions |
| TextDim | #7C8A81 | Section labels, locked text |
| Track | #252E29 | Empty part of any bar |
| Green | #7FE0A8 | Good, met, running, selected text accent |
| GreenDeep | #2FA866 | Tick bar start |
| SelectedBg | #1A2B22 | Selected button background |
| SelectedBorder | #2F6B4D | Selected button border |
| RunningPill | #12201A | "RUNNING" pill background |
| Yellow | #E3C26B | Warning, idle, coin |
| YellowBorder | #5C4F25 | Idle circle border |
| Red | #E0745F | Unmet, damage |
| Orange | #F0A07C | Relief countdown |
| Purple | #A58BFF | Mana, spells, resource fill bars |
| PurpleDeep | #7B6BC9 | Necromancer bar |
| NecroBg | #161230 | Necromancer counter / assigned slot background |
| NecroBorder | #4A3F7A | Necromancer counter border |
| NecroText | #C7B8FF | Assigned slot text |
| Blue | #7B9CFF | Mana numbers, mana bar |
| Teal | #4FD1C5 | Infused mana |
| Locked | #1B1F1D / #1F2824 | Locked slot fill and border (draw at 55% opacity) |

Meaning: green = met/good, red = unmet/damage, purple = mana and spells. Selected = SelectedBg fill + SelectedBorder border + TextMain text.

### Type
- Headings (page and card titles, Crypt tabs): **Cinzel** 600, 14 to 19 px. If Cinzel is not available, use the default font bold.
- Everything else: **Inter** (or Godot default). Body 12 to 13 px, small labels 10.5 to 11 px bold uppercase with letter spacing for section labels ("CHANNELING SLOTS"), numbers 15 px bold.
- Minimum text size 11 px in the reference space.

### Shared components (build these first)
| Component | Look | Used for |
|---|---|---|
| `Card` | PanelContainer, BgCard, border BorderCard, radius 12, padding 14 | Every panel |
| `InsetRow` | BgInset, Border, radius 10, padding 9x11 | Rows in a card |
| `SectionLabel` | 10.5 px bold, TextDim, uppercase, spaced | Panel sub-headings |
| `Pill` | radius 999, bold 11 px; "RUNNING" green on RunningPill | Status |
| `StatBar` | Track background, coloured fill, height 3 to 10 px | All bars |
| `ResourceChip` | "owned / needed" text, green when met, red when not | Costs |
| `ActionButton` | Border button, SelectedBg/SelectedBorder when primary, text Green bold 12 px | Level up, Equip, Craft |
| `HoldButton` | ActionButton that needs hold-to-confirm (0.6 s fill) | Purchases |
| `Stepper` | `-` `[n]` `+` plus a step selector | Assigning necromancers |
| `StepSelector` | Buttons 1, 1k, 1M, All free; one shared value | Assign amount |
| `Toast` | Small pop-up, bottom-right, 3 s | "+12 Iron Ore (1,980 stored)" |

## The shell (every page)

Five fixed regions. Coordinates are in the 1280x760 reference space.

```
y=0    +-------------------------------------------------------------+ TopBar h48
y=48   | THE CRYPT  [Workshop][Research][Unit Builder][Spell Book][Lich] | CryptTabs h46
y=94   +--------+----------------------------------------------------+
       | Side   | Content area (swaps per page)                      | h606
       | bar    |                                                    |
       | w210   |                                                    |
y=700  +--------+----------------------------------------------------+
       | ResourceBar h60                                              |
y=760  +-------------------------------------------------------------+
```

### Top bar (h48, BgBar, bottom border, padding 0 20)
Left to right:
1. **Tick bar** (w300). Row: "TICK" left (10.5 px bold, TextMuted), "0.7s / 2.0s" right (TextMain). Under it a 7 px bar, fill gradient GreenDeep to Green. Bound to `TickManager` progress. Updates every frame.
2. **Necromancer counter** (w254, NecroBg, NecroBorder, radius 10, padding 5x12). Row: `Free 2.4B` (Green bold), `Assigned 97.6B` (TextMain bold), `Max 100B` (TextMuted), 11.5 px. Under it a 4 px bar, fill PurpleDeep = assigned / max. Tooltip shows exact numbers.
3. **Siege strip** (GreenDeep-ish: SelectedBg fill, SelectedBorder border, radius 10, padding 6x12). Text: `SIEGE · GREYWATCH HOLD` (10.5 px bold Green, spaced), `Walls 62%` (12 px), `Relief 02:14:00` (Orange), a 64 px bar (purple to red gradient). In v0.1 the siege targets are the two areas (Graveyard, Woods): `SIEGE · THE GRAVEYARD`. Hidden when no siege is active. Clicking it opens the Battle view.
4. **Map** and **Settings** buttons (BgCard, Border, radius 8, padding 6x10, 12 px bold TextSoft).

### Crypt tabs (h46, BgPanelDark, bottom border, padding 0 20, gap 8)
Left label `THE CRYPT` (10.5 px bold TextDim, w190). Then five tab buttons: **Workshop, Research, Unit Builder, Spell Book, Lich**. Cinzel 14 px, padding 7x18, radius 8. Selected: SelectedBg/SelectedBorder/TextMain. Others: transparent, Border, TextSoft-ish (#A8B4AB). Clicking swaps the content area. These tabs stay visible on every page; when a skill page is open, no tab is highlighted.

### Skills sidebar (x0, y94, w210, h606, BgPanelDark, right border, padding 10)
Skills only. Three groups with section labels:
- **LICH:** Health, Necromancy, Magic (passive, no page, no `›`).
- **GATHERING:** Mining, Fishing, Hunting, Herbalism (each `›`).
- **CRAFTING:** Smithing, Alchemy, Enchanting (each `›`).

Each skill is a button (BgCard `#121815`, border `#222C27`, radius 10, padding about 7x9). Row: a 9 px dot (green when selected, else #3A4740), name (12.5 px bold), `›` in Green if it has a page, and on the right `level/999` (12 px TextMuted). Under it a 3 px XP bar, Green fill. Selected: SelectedBg/SelectedBorder/TextMain. Clicking a gathering/crafting skill opens its page in the content area. Lich skills show a tooltip with XP and bonuses only. Nothing else lives in the sidebar.

Placeholder levels: Health 14, Necromancy 22, Magic 19, Mining 31, Fishing 12, Hunting 17, Herbalism 9, Smithing 26, Alchemy 15, Enchanting 8.

### Resource bar (y700, h60, BgBar, top border, padding 0 16)
Seven resource boxes, each w164, BgCardAlt, border #2A352F, radius 8, padding 7x10. Inside: a 22 px circle in the resource colour, stock (15 px bold), `/ cap` (10.5 px TextDim), the rate on the right (`+3,096/tick`, 10.5 px bold Green), and a 3 px purple bar for fill under it. Built from the resource list in JSON; scroll horizontally if there are more than seven.

Placeholder resources: Coin #E3C26B, Mana #7B9CFF, Infused mana #4FD1C5, Souls #A58BFF, Bones #D9D2B8, Iron ore #9AA6AE, Herbs #7FB069.

### Content area
Position x210, y94, w1070, h606, padding 16x20. Pages fill it. Standard page layout is a horizontal row of Cards with a 14 px gap.

### Navigation behaviour
- State `CurrentPage` = one of: `Crypt/Workshop`, `Crypt/Research`, `Crypt/UnitBuilder`, `Crypt/SpellBook`, `Crypt/Lich`, `Skill/<id>`, `Map`, `Settings`, `Battle`, `BossPrep`.
- Crypt tab click, skill click, Map button and the siege strip all set `CurrentPage`. The shell never reloads; it swaps the child of the content area.
- Starting page: Workshop, Mana Well selected.

## Pages

Each page below: what it contains, in order, left to right. Build them as scenes `UI/Pages/<Name>.tscn` implementing `IPage { Bind(); Refresh(); }`.

### 1. Workshop
Left: **Buildings list** (w172). `BUILDINGS` label, then seven buttons: Mana Well, Summoning Circle, Infuser, Soul Well, Transmuter, Undead Market, Storage. Each: name (13 px bold) and an 8 px status dot (Green running, Yellow needs input). Selected = SelectedBg style. Footer text: "Green dot: running. Yellow: needs input." (11 px TextDim).
Right: the selected building's own page. Only Mana Well and Summoning Circle are drawn; the other five show a placeholder Card with the building name, its level, staffed count, output per tick, and a Level up button until their pages are designed.

#### 1a. Mana Well page (three cards) - image 01
Reworked from the old game (`ui-reference/old-game/03-old-game-mana-well.png`). The first mockup (slot grid, overflow chooser, milestones) is removed. Full brief: `adjustments/adjust-02-mana-well.md`.

Card A (w~280): title `Mana Well` with RUNNING pill, description, the **clickable well graphic** (`CLICK TO CHANNEL`, `+1 mana per click`), an OUTPUT box (`+8.00 /tick`), and a `Mana stored 521 / 850` bar.
Card B (w~276): `Necromancers`. Row with the necromancer block, `4 / 6` and `-` `+` buttons; Assign by selector (1, 1k, 1M, All free); OUTPUT BREAKDOWN rows (per necromancer, necromancers x count, level bonus, click power).
Card C: `Well level`. `Lvl 4 > Lvl 5`, output change, COST rows as owned / needed (red when short), UPGRADE button (disabled with a reason until affordable).
Big counts: abbreviate, exact value in a tooltip. No slot grid. Nothing locked is shown.

#### 1b. Summoning Circle page (three cards) - image 02
Reworked from the old game (`ui-reference/old-game/01` and `02`). The first mockup (circles with a recipe list on the right) was wrong. A circle is not assigned a recipe from a list. The player **clicks a slot's icon to choose the unit**, and **clicks the circle graphic to add click power**.

Card A (w~246): the circle.
- Title `Summoning Circle` with `RUNNING` pill, one-line description.
- **The circle graphic** (big clickable block, 176 px tall, NecroBg fill, purple border, label `CLICK TO SUMMON`, `+1 power per click`). Pressing it adds **click power** to the unit currently being summoned (the active slot's bar). Holding or rapid clicking should work. In the real game this is the circle art; blocky version is a labelled square with a circle.
- **Necromancer stepper** under it: `- 1 / 4 +` with the label `necromancers` (necromancers assigned to this circle; the number after the slash is the most it can hold at the current level). Beneath it the shared `Assign by 1 / 1k / 1M / All` selector.
- Footer: power per tick (`+0.2 per necromancer`), click power, and "Summoned necromancers join the free pool."

Card B (w~340): `Summon slots`.
- A list of slot rows, in summoning order. Each row: a **unit icon button on the left** (44 px square), the unit name and time per unit, a progress bar with the percentage in the middle (`66%`), and an `x` button to clear the slot.
- **Clicking the icon opens the unit chooser** (a popup list under the slot, "CHOOSE UNIT"). Each entry shows name and cost (`Necromancer 64 mana · 32 souls`, `Wisp 50 mana`, `Skeleton 150 mana · 10 souls · 10 bones`). Selecting one fills the slot.
- **Hovering a unit** in the chooser shows a **tooltip**: name, a one-line description, and cost lines as `owned / needed` (red when not enough, green when met).
- States: active slot (purple border, filled bar) and empty slot (`PICK`, yellow border, `Idle`). **No locked slots or locked icons are shown.** Slots and units simply appear when unlocked (by circle level now, by research later).
- The bar fills from click power + necromancer power per tick. When full it summons one unit, spends the cost, and starts over. A slot waits (bar stops) when it cannot pay.
- Only unlocked units appear in the chooser. Units come from the unit types the player has built (Unit Builder) plus Necromancer and Wisp. Nothing locked is shown.
- See `adjustments/adjust-01-summoning-circle.md` for the full change brief.

Card C (w~230): `Circle level`.
- `Lvl 2 > Lvl 3` and the output change (`+0.02% > +0.03%`), one-line note that new slots and units appear as they unlock.
- `COST` list with owned / needed per resource (green when met, red when not).
- Big `UPGRADE` button.

Summoned necromancers are added to the free pool through `LichManager.AddToPool` (top bar Free goes up).

### 2. Research - image 03
Reworked from the real research data. Full design: `design/research-revamp.md`.
- **Tome list (left, w~164):** only found tomes. Name, researched count (`5/33`) and a note. A small legend (done, researching, available, circle, square) and the line "Nodes appear when everything they need is done. Tomes appear when found."
- **Node tree (centre):** pan and zoom canvas. Circles for one-off nodes (56 px), rounded squares for upgrade nodes (48 px) with a `0/100` badge. States: done (green, check), researching (purple glow), available (grey ring), hovered (green glow). **No locked or dimmed nodes.** Inner dot colour by type. Lines join each node to the nodes it requires. Node positions come from `layout.x` and `layout.y` in the JSON (x column index, y layer; scale about 100 px by 104 px).
- **Hover card:** name, time at current bench speed, `ONE-OFF` or `REPEATABLE · level / max`, effect text, work and requirements, "Click to start on a free bench".
- **Benches (right, w~270):** a card per bench: job, necromancer stepper, progress bar with time left, work and rate. Only unlocked benches appear. Below: `Bench level 2 > 3`, work per tick, costs (coin and mana), UPGRADE.
- Research costs **work only**, no resources. Time = work / work per tick x tick length.

### 3. Unit Builder
- **Unit list** (left, w~180): the base units; unresearched ones dimmed; yellow dot when gear is running short.
- **Unit page** (centre): header with role and a one-line description. Four stat boxes: Health, Damage, Attack speed, Armour, each showing base value and value with current loadout. Weapon and armour slots showing the equipped item's tier and enchantment trait. A tabbed list of crafted gear: tier, bonus, trait, stock, `Equip` button; locked gear shows what is to come.
- **Stack panel** (right): stack size vs target count, formation slot, Summoning Circle name, target stepper. **Gear supply** meters for weapon and armour stock with a warning when one will run out. Each summon uses up one weapon and one armour, so the Summoning Circle pauses when one runs out. A `Re-equip` button with a mana cost per unit.
One loadout per unit type.

### 4. Spell Book
- **Spells** (left, widest): search box, filter pills `Attack`, `Support`, `Siege`, and a grid of spell tiles showing name, Insight cost and mana cost. Learned = lit; not found = dimmed with the tome that holds it. Below the grid, the **modifiers** area for the selected spell: base vs modified stats, variants unlocked, and a row of modifier chips (on / available to buy / locked).
- **Insight** (middle): a meter with `Free`, `Used`, `Max`, and a note "Max grows with Magic level and gear". The doc still uses both "Insight" and "Max Focus"; use **Insight** in the UI.
- **Channelled** (right): each equipped spell shows mana cost, effect, Insight cost, a cooldown bar with `Ready` or time left, an `Auto` or `Manual` tag, the auto rule (for example "when a stack is under 40%") and a Remove button. At the bottom, free Insight with a prompt to channel another spell.

### 5. Lich
- **Throne** (left): the lich in the middle (a labelled box) with seven slot buttons around it: Hood, Amulet, Ring, Robe, Staff, Grimoire, Boots. Each slot shows the equipped item's rarity colour. Under it: Health bar with `Health level/999` and regeneration per second.
- **Items** (centre): tabs `Items`, `Loadouts`; `Auto Equip` button; filters for type and rarity; a grid of item tiles with stack counts. Hover shows bonuses and what it replaces. A bottom `Sell` bar sells selected items for coin.
- **Stats and bonuses** (right): Max Health with breakdown (skill vs gear), Max Insight, and a colour-coded bonus list (health red/green, mana and spell speed blue, summon speed gold).

### 6. Skill pages
#### 6a. Gathering page (Mining, Fishing, Hunting, Herbalism share one scene)
- **Header Card:** skill name, level, XP bar, XP per tick, next unlock, and a progress bar for the lich gathering by hand.
- **Necromancer pool Card:** `Free X of Y`, plus the shared `StepSelector` (1, 1k, 1M, All free).
- **Gathering items grid:** one Card per activity: level requirement, what it gives, XP and time in ticks, stock in Storage, and the rate from assigned necromancers. Buttons: `Gather by hand`, and a Stepper (`-`, assigned count, `+`) that calls `AddNecromancer` / `RemoveNecromancer` on the skill manager with the selected step. Locked cards are dimmed and show "Unlocks at level N".
- **Automation Card:** toggles for auto-assign idle necromancers, switch item when storage is full, stop at a stock target, keep going while away.
- **Tool upgrade Card:** the skill's tool (pickaxe for Mining), next tier, effect, cost, and an auto-upgrade toggle with a coin reserve.
- Mining items (placeholders): Stone, Iron ore, Silver ore, Grave iron, Soul crystal.

#### 6b. Crafting page (Smithing, Alchemy, Enchanting share one scene)
- Header Card as above.
- Tabs: Smithing `Weapons` / `Armour`; Alchemy `Potions` / `Bombs`; Enchanting `Runes` plus an `Attach rune` panel (choose a weapon or armour, a new rune replaces the old one).
- Recipe rows: name, ingredients as `ResourceChip`s (owned/needed), time and XP, stock in storage, necromancers assigned with Stepper, stock target, `Craft` button for hand crafting. Locked recipes dimmed with what unlocks them.
- Automation: pause when an ingredient is low, move idle necromancers to the next recipe, keep crafting while away, and for Enchanting an auto-attach rule. An anvil or cauldron upgrade card raises speed.

### 7. World map (v0.1 version)
The mockup draws six rings; **v0.1 does not use rings.** Build the same layout with the v0.1 content:
- **Map area** (left, large): a set of nodes joined by paths. Two areas only: **The Graveyard** (another lich's small undead army, the graveyard gates are the walls) and **The Woods** (wild people). Node colours: taken, besieged, available, locked.
- **Siege banner** (top of the map card): active siege, wall progress, relief countdown (only if the design keeps relief armies; hide otherwise), buttons `Watch battle` and `Recall`.
- **Area panel** (right): the selected area's defenders and layers, rewards, and a Host card showing unit count and strength vs defences, with a `Send the Host` button and a link to edit the formation.
- One siege at a time for now.

### 8. Battle view (siege and boss)
Opens over the content area; sidebar and resource bar stay.
- **Prep screen** (BossPrep): Left Boss card (health, damage, speed, what the player has learned, attempts, best result; hidden patterns stay hidden). Centre Encounter loadout: three summon slots in priority order (unit, gear, cost per summon, summon time, auto-summon toggle, `Change`), channelled spells with Insight use and Auto/Manual, saved loadouts per boss. Right Supply check meters, rules (re-summon on death, retry after loss, swap rules), `Begin fight` and an auto-farm toggle.
- **Fight screen** (BossFight): blocky battlefield. Lich box on the left with a health bar, three summon boxes in front, boss box on the right with a large health bar. A telegraph line under the boss: next attack, timing, target. Floating damage numbers as plain labels. Each slot has a unit name, summon progress bar and a `Summon now` button. Spell buttons with cooldown bars (manual = click, auto = shown with rule). Battle log (scrolling) and `Retreat` button.
- **v0.1 battle rule:** each army section has up to 3 actions with their own cooldown bars (for example Strike every 2 s, Brace every 10 s). HP shown as bars per section. Show each action as a small bar under the section.

### 9. Settings
Simple page: resolution/window mode, tick speed (when available), save slots (new, save, load, delete), debug toggle. Opens in the content area.

### 10. Away recap
A modal Card over the content when returning: time away, resources gained, XP gained, anything capped, strongholds/areas affected. One `Continue` button.

## Debug additions (keep from phase-00-dev-ui)
Keep the debug console, event log and commands (`addnecro`, `give`, `xp`, `level`, `ticks`, `offline`, `speed`, `pause`, `save`, `load`, `unlock`, `battle`). Place it as a collapsible bottom drawer or a right side overlay toggled with backtick (`), so it does not change the layout above. Debug build only.

## Build order
1. `Tokens`, shared components, and a gallery scene that shows every component.
2. Shell: top bar, Crypt tabs, sidebar, resource bar, page swapping. Fill with `MockData`.
3. Workshop (Mana Well, Summoning Circle) wired to real managers as they land.
4. Gathering page (Mining) and crafting page (Smithing).
5. Research, Unit Builder, Spell Book, Lich.
6. World map, battle prep and fight.
7. Settings, away recap, toasts.
8. Remaining building pages and skill pages.

## Done when
- The shell and every page above render at 1280x760 and at 1920x1080 without clipping.
- Every number on screen comes from game state (or `MockData` behind the same interface) and updates on events.
- Pressing `+` on an item in Mining moves necromancers from Free to Assigned in the top bar and raises the ore stock each tick.
- A tester can open every page from the sidebar, Crypt tabs, Map button or siege strip and never leaves the one window.
- A screen-by-screen comparison with the mockups shows the same layout, wording and colours.

## Out of scope
Art, icons, fonts beyond Cinzel and Inter, animation, sound, mobile layout, unfinished building pages beyond a placeholder.
