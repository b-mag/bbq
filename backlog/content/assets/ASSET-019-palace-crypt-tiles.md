# ASSET-019: Palace Crypt distinct tiles

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (art) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | README.md Palace Crypt vs Temple share tiles; yellow_palaces.png |

## Summary

Crypt tiles distinct from Temple of Hali: underground gold-on-black, cyclopean blocks, fewer “palace floor” cells. Dungeon tile ids 0–5 live in drowned_docks and others — add a crypt mapping without breaking dock.

## Context

Do not steal drowned_docks cells. New sheet or unused columns. MapGenerator crypt uses the new ids (ARCH-BE-004).

## Acceptance criteria

- [ ] Crypt wall/floor/door/entrance-glow distinct from temple and dock.
- [ ] manifest dungeon mapping documented.
- [ ] No magenta.

## Out of scope

Generator (ARCH-BE-004).

## Suggested files

- `tilesets/`
- `tilesets/manifest.json`
- `MapGenerator.cs tile choices`

## Dependencies

- ARCH-BE-004
