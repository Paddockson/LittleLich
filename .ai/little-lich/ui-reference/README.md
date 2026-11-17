# UI reference images

Screenshots of every mockup board at 1280x760, the size the UI is built at. Build the Godot UI to match these, blocky (flat rectangles, text, bars) but same layout, wording and colours. `phase-00-ui-spec.md` describes each screen in words; these images show it.

Each `html/` file is the same board as a static page. Open it in a browser and read the inline CSS for exact sizes, spacing and colours (every position and hex value is there).

| Image | Screen | Spec section |
|---|---|---|
| images/01-workshop-mana-well.png | Workshop, Mana Well page (shows the shell too) | Shell, Workshop 1a |
| images/02-workshop-summoning-circle.png | Workshop, Summoning Circle page | Workshop 1b |
| images/03-research.png | Research: tomes, node tree, benches | Research |
| images/04-unit-builder.png | Unit Builder | Unit Builder |
| images/05-spell-book.png | Spell Book | Spell Book |
| images/06-lich.png | Lich: throne, items, stats | Lich |
| images/07-world-map.png | World Map | World map |
| images/08-boss-prep.png | Boss prep | Battle view |
| images/09-boss-fight.png | Boss encounter | Battle view |
| images/10-skill-mining.png | Gathering skill page (Mining) | Skill pages 6a |
| images/11-skill-smithing.png | Crafting skill page (Smithing) | Skill pages 6b |
| images/12-skill-alchemy.png | Crafting skill page (Alchemy) | Skill pages 6b |
| images/13-skill-enchanting.png | Crafting skill page (Enchanting) | Skill pages 6b |

## Differences between the images and what to build

- **World map (07):** the image shows six rings, six ring names and many strongholds. v0.1 has no rings. Keep the layout (map card, siege banner, area panel, Host card) but use paths and two areas only, the Graveyard and the Woods.
- **Mana Well (01):** the slot grid cannot hold billions of necromancers. Use level, necromancer count and the Stepper instead, as written in the spec.
- **Fonts:** images may show a fallback serif for headings. Cinzel is the intended heading font.
- **All names, numbers and stats** in the images are placeholders.
- Old boards "Main" (siege battle) and "Formation" are outdated and not included.
- Where the image and the spec text disagree on layout, follow the image. Where they disagree on game rules, follow the spec and the design doc.
