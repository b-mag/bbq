# ASSET-005: Great swamp tiles

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (art) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | OVERWORLD_VISION Marshes of Yhtill; swamp tile 17 |

## Summary

Richer swamp tiles: reeds, scum water, dark pools, Giger-adjacent mangrove/ribcage trees as props or extra cells. Sickly chartreuse / teal, not cartoon green.

## Context

Swamp is in `carcosa_realms.png`. Trees currently reuse generic `tree`. Optional `swamp_tree` / `reed` sprites.

## Acceptance criteria

- [ ] Swamp ground variants (≥2).
- [ ] At least one reed/pool prop sprite.
- [ ] Named palette (`chartreuse` / `teal` / `flesh` accents).
- [ ] catalog + manifest.

## Out of scope

Placement density (CONT-BE-005).

## Suggested files

- `tilesets/`
- `sprites/`
- `manifests`
- `catalog.json`

## Dependencies

- CONT-BE-005
