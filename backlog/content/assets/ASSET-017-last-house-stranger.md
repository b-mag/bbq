# ASSET-017: Last House interior + Stranger sprite

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (art) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | VERTICAL_SLICE The Last House / The Stranger |

## Summary

Fixed interior tiles for The Last House and a Stranger sprite (Pallid Mask energy, 32×32, 4-dir walk optional). Uncanny shop, not a tavern.

## Context

Character sheets: walk rows first. Facing down,left,right,up.

## Acceptance criteria

- [ ] Interior floor/wall/counter tiles.
- [ ] `npc_stranger` manifest + catalog.
- [ ] No true-name labeling on the sprite sheet filename if possible — `npc_stranger.png` is fine.

## Out of scope

Shop API. Dialogue writing (CONT-BE-010).

## Suggested files

- `tilesets/`
- `sprites/npc_stranger.png`
- `manifests`

## Dependencies

- ARCH-BE-015
