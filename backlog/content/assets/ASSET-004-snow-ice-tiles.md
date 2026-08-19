# ASSET-004: Snow / ice tiles and black-star sky

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (art) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | OVERWORLD_VISION Black Stars; carcosa_realms.png already has snow cells |

## Summary

Polish snow/ice cells in `carcosa_realms.png` (or a dedicated strip) and a north sky treatment (black stars) so the peaks are not generic white noise.

## Context

Tile size 32, atlases 8 columns. `tilesets/manifest.json` maps ids 0–23. Snow is tile 19. Do not shift cell indices without updating the manifest.

## Acceptance criteria

- [ ] Readable ice vs snow vs mountain.
- [ ] Black-star north mood (sky/wash, not a 3D skybox).
- [ ] extract-palettes.py re-run if colors change.
- [ ] manifest indices still correct.

## Out of scope

Props (ASSET-020). Weather flakes (ASSET-006).

## Suggested files

- `tilesets/carcosa_realms.png`
- `tilesets/manifest.json`
- `palettes.ts`

## Dependencies

- CONT-BE-006
