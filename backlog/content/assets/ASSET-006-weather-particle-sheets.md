# ASSET-006: Rain / fog / snow particle sheets

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (art) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | New. Feeds ARCH-UI-013 |

## Summary

Tiny particle sheets: rain streak, fog wisp, snow flake (pale and black-star variant). 8–16 frames or a few static stamps the canvas can scatter.

## Context

Keep them small. Magenta is chroma-key trash — use real alpha.

## Acceptance criteria

- [ ] Rain, fog, snow, black-star snow stamps.
- [ ] Documented in an audio/vfx manifest or catalog.
- [ ] Readable at 32px world scale.

## Out of scope

Lynch overlays (ASSET-007). Renderer (ARCH-UI-013).

## Suggested files

- `src/frontend/public/assets/vfx/ or sprites/`
- `catalog.json`

## Dependencies

- ARCH-UI-013
