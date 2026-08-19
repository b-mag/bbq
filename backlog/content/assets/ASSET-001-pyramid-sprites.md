# ASSET-001: Large pyramid sprites

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (art) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | SPRITE_TECHNICAL.md tentacle_pyramids palette; new world landmarks |

## Summary

Draw large, feet-anchored pyramid sprites (multi-tile / tall) in the `tentacle_pyramids` palette: desert ochre, pyramid gold, black sky-entity accents. Not isometric photos — SNES-readable.

## Context

Tiles are 32×32. Buildings in repo are 64×64 to 64×80; dream_ship is 192×112. Pyramids should read as *large* (e.g. 96×96 to 128×160) with collision matching CONT-BE-002. Feet on the bottom. No magenta.

## Acceptance criteria

- [ ] At least one great pyramid + one smaller variant.
- [ ] PNG + `sprites/manifest.json` keys + `catalog.json` row.
- [ ] Palette stays inside `tentacle_pyramids` / `gold`.
- [ ] Rebuild frontend so wwwroot matches.

## Out of scope

World placement (CONT-BE-002). Interior.

## Suggested files

- `src/frontend/public/assets/sprites/`
- `manifest.json`
- `catalog.json`

## Dependencies

- CONT-BE-002 consumes.
