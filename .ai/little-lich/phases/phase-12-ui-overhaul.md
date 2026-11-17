# Phase 12: UI overhaul (rebuild the whole UI from the reference images)

## Goal
Throw away the current UI and rebuild it from scratch so it matches the reference screenshots in `ui-reference/images/`: same layout, wording, colours and behaviour, built blocky (flat rectangles, text, bars, buttons). Every screen runs on real game state.

The current UI is unusable. Do not patch it, do not reuse its scenes or scripts. Only reuse the manager APIs it calls.

## Depends on
- Phases 1 to 3 (managers, tick) for the shell to have real data.
- Each page is wired to its system as that phase lands (4 skills, 5 Workshop, 6 Lich/inventory, 7 research, 8 map/battle, 9 units). Until then a page runs on `MockData` behind the same interface, so the UI can be built and judged before the systems finish. This phase can start right after phase 3.

## Source of truth
1. `ui-reference/images/*.png` (13 screens at 1280x760). Look at the matching image before building each page.
2. `ui-reference/html/*.html`: each board as a static page with exact CSS (sizes, spacing, hex values).
3. `phases/phase-00-ui-spec.md`: tokens, shared components, shell and per-page contents in words.
4. Where image and text disagree on layout, follow the image. Where they disagree on game rules, follow the design doc. Known differences (no rings on the map, Mana Well uses a stepper) are in `ui-reference/README.md`.

## Rules
- Reference size 1280x760, stretch `canvas_items`, aspect `expand`. Built from containers and anchors, no fixed positions except the shell regions.
- Blocky only: `PanelContainer` + `StyleBoxFlat`, `Label`, `Button`, `ProgressBar`, containers. No textures, icons, animation, shaders.
- All colours and sizes from one `Tokens` class and one Godot `Theme` resource. No literals in pages.
- UI reads state and calls manager methods. It never changes state. Refresh on events; only the tick bar updates per frame.
- Big numbers abbreviated (2.4B), exact value in a tooltip. No per-necromancer icons.
- Everything generated from the content database where it is a list (skills, activities, recipes, buildings, tomes, units, items).
- Dev console and event log from `phase-00-dev-ui.md` stay available (backtick toggle), outside the main layout.

## Work breakdown

### Step 0: Remove the old UI
- [ ] Move the old UI scenes and scripts to `UI/_old/` (or delete). Nothing in the game may reference them.
- [ ] Keep a list of every manager call the old UI made; the new UI must cover them.

### Step 1: Foundation
- [ ] `UI/Theme/Tokens.cs` with every colour and size from the spec.
- [ ] One `Theme` resource generated from the tokens (fonts: Cinzel for headings, Inter for the rest, with fallbacks).
- [ ] Shared components, each in its own scene or class: `Card`, `InsetRow`, `SectionLabel`, `Pill`, `StatBar`, `ResourceChip`, `ActionButton`, `HoldButton`, `Stepper`, `StepSelector`, `Toast`.
- [ ] `ComponentGallery.tscn` showing every component in every state (normal, selected, locked at 55% opacity, warning).
- [ ] `IPage` interface (`Bind`, `Refresh`) and a `PageRouter` that swaps the child of the content area.
- [ ] `MockData` implementing the same interfaces the managers expose.

### Step 2: Shell (compare with image 01, it shows the whole shell)
- [ ] Top bar: tick bar, necromancer counter, siege strip (hidden when no siege), Map and Settings buttons.
- [ ] Crypt tabs row: Workshop, Research, Unit Builder, Spell Book, Lich.
- [ ] Skills sidebar: three groups, level/999, XP bars, selected state, `›` on gathering/crafting skills.
- [ ] Resource bar: seven boxes from JSON, horizontal scroll if more.
- [ ] Navigation: Crypt tab, skill, Map button, siege strip all route through `PageRouter`. Start on Workshop / Mana Well.
- [ ] Screenshot test of the empty shell against image 01 (shell area only).

### Step 3: Pages, in this order (each one is done when its screenshot matches)
| # | Page | Reference image | Wired to |
|---|---|---|---|
| 1 | Workshop: Mana Well | 01-workshop-mana-well.png | CryptManager.ManaWell (use stepper, not slot grid) |
| 2 | Workshop: Summoning Circle | 02-workshop-summoning-circle.png | CryptManager.SummoningCircle, LichManager.AddToPool |
| 3 | Gathering page (Mining) | 10-skill-mining.png | MiningManager, LichManager assignments |
| 4 | Crafting page (Smithing) | 11-skill-smithing.png | SmithingManager |
| 5 | Crafting pages (Alchemy, Enchanting) | 12, 13 | AlchemyManager, EnchantingManager |
| 6 | Lich | 06-lich.png | LichManager, inventory, equipment |
| 7 | Research | 03-research.png | ResearchManager (Necromancy 1) |
| 8 | Unit Builder | 04-unit-builder.png | UnitBuilder, Roster |
| 9 | Spell Book | 05-spell-book.png | SpellBook |
| 10 | World map | 07-world-map.png | WorldManager (paths, Graveyard, Woods; no rings) |
| 11 | Boss prep | 08-boss-prep.png | BattleController setup |
| 12 | Boss fight | 09-boss-fight.png | BattleController |
| 13 | Other gathering skills (Fishing, Hunting, Herbalism) | reuse 10 | Their managers |
| 14 | Other Workshop buildings (Infuser, Soul Well, Transmuter, Undead Market, Storage) | none drawn | Placeholder card until designed |
| 15 | Settings, away recap, toasts | none drawn | GameManager, AwayRecap |

For each page:
- [ ] Open the reference image and its html. List every element in the image.
- [ ] Build the scene from shared components only.
- [ ] Bind every number and button to a manager or `MockData`.
- [ ] Add states the image implies: locked, empty, capped, warning, selected.
- [ ] Capture a screenshot at 1280x760 and compare with the reference (see below).
- [ ] Fix differences. Record deliberate differences in `ui-reference/README.md`.

### Step 4: Behaviour checks
- [ ] Pressing `+` on a Mining item: Free goes down and Assigned up in the top bar, ore stock rises each tick.
- [ ] Step selector (1, 1k, 1M, All free) changes how many each `+` and `-` moves.
- [ ] Summoned necromancers increase Free in the top bar.
- [ ] Hold-to-confirm buttons only fire after the hold completes.
- [ ] Locked content shows what unlocks it and cannot be used.
- [ ] Capped storage shows the capped state on its row and in the resource bar.
- [ ] Siege strip appears on every page during a siege and opens the battle view.
- [ ] Works at 1280x760 and 1920x1080 without clipping, and no page needs a loading screen.

### Step 5: Screenshot comparison tooling
- [ ] A `--capture-ui` launch flag that opens each page in turn with `MockData` and saves `captures/<page>.png` at 1280x760.
- [ ] A side-by-side script that puts each capture next to its reference image and writes `captures/compare/<page>.png`.
- [ ] A short checklist per page: layout, wording, colours, states. Reviewed by the owner before the page is called done.
- [ ] Exact pixel match is not required. Same layout, same text, same colours, same states is.

## Page checklists (copy into each page's task)

Shell: tick bar, necromancer counter, siege strip, Map/Settings, Crypt tabs, sidebar groups, resource boxes, selected states.

Workshop: buildings list with status dots, header with pill, stat row, level-up button, necromancer stepper, overflow choice, milestones, summoning circles with progress/target/state, recipes with filter pills and costs.

Gathering: header with XP and next unlock, activity grid (2 columns), gather by hand, stepper, rate text, necromancer pool card with step selector, automation toggles, tool upgrade card.

Crafting: header, tabs, recipe rows with chips, stock, stepper, target, craft button, automation, upgrade card; Enchanting attach panel.

Lich: throne with seven slots, health bar, item grid with filters, auto equip, sell bar, stats and bonuses list.

Research: tome list, pan and zoom tree with three node sizes and four states, hover card, benches with steppers, bench level card.

Unit Builder: unit list, stat boxes, slots, gear list with tabs, stack panel, gear supply meters, re-equip.

Spell Book: spell grid with search and filters, modifier chips, Insight meter, channelled list with Auto/Manual and cooldown bars.

World map: map card with paths and two areas, siege banner, area panel, Host card, Send the Host.

Boss prep and fight: boss card, three summon slots, channelled spells, supply check, rules, battlefield, telegraph, slot panels, spell buttons, log, retreat.

## Done when
- The old UI is gone and nothing references it.
- Every page in Step 3 (except 14 and 15, which are placeholders) matches its reference screenshot in layout, wording, colours and states, signed off by the owner using the side-by-side captures.
- Every number comes from game state (or `MockData` where the system does not exist yet) and updates on events.
- All Step 4 behaviour checks pass.
- Adding a JSON activity, recipe, building or tome adds its UI element with no scene edits.

## Out of scope
Art, icons, animation, sound, mobile layout, designing the pages marked "none drawn" (they stay placeholders), and changes to game rules. If the owner wants a screen changed, update the reference image first, then the build.
