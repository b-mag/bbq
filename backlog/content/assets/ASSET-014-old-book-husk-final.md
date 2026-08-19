# ASSET-014: old_book_husk final art

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (art) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | SPRITE_TECHNICAL.md placeholder 16×20; README §16 |

## Summary

Replace placeholder `old_book_husk.png` in place. Same cell size 16×20, gold-brown palette, salt-stiff binding. Keep collision 0.25 so Merek stays talkable.

## Context

Safe swap: same filename and cell size. Do not change collision without walking it in-game.

## Acceptance criteria

- [ ] Final art dropped in place.
- [ ] catalog/palette notes updated. extract-palettes.py run.
- [ ] Quest pickup still works (type string unchanged).

## Out of scope

New quest steps.

## Suggested files

- `sprites/old_book_husk.png`
- `catalog.json`

## Dependencies

- None.
