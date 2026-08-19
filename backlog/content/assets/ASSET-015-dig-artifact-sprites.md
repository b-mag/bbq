# ASSET-015: Dig artifact trinket sprites

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (art) |
| **Priority** | P2 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | SPRITE_TECHNICAL.md §6; 16×20 or 16×16 trinkets |

## Summary

Sprites for the 12 dig artifacts. Key Items UI can stay text until these exist. Manifest keys when sprites land.

## Context

Ids locked. Secrets should not look like glowing exclamation marks.

## Acceptance criteria

- [ ] 12 small sprites + shovel optional.
- [ ] manifest + catalog. One palette per item, from the named set.

## Out of scope

Passive wiring (ARCH-BE-025).

## Suggested files

- `sprites/`
- `manifest.json`
- `KeyItemsPanel.tsx icon hook`

## Dependencies

- None for art. UI hook optional.
