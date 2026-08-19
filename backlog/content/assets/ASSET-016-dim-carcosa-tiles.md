# ASSET-016: Dim Carcosa drowned city tiles

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (art) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | VERTICAL_SLICE Dim Carcosa overlay; shore_of_hali / ruined_carcosa tilesets |

## Summary

Walkable drowned-street tiles, ruined shells, broken colonnades for the Hali overlay. Teal/gold/black. Must read as city-under-cloud-waves, not a second village.

## Context

May extend `ruined_carcosa.png` / `shore_of_hali.png` with unused cells — update manifest indices carefully.

## Acceptance criteria

- [ ] Street, rubble, sealed-door, colonnade cells.
- [ ] Works next to existing water tiles.
- [ ] manifest updated; no index collisions.

## Out of scope

Layout (CONT-BE-009). Tracker gate (ARCH-BE-014).

## Suggested files

- `tilesets/`
- `tilesets/manifest.json`

## Dependencies

- CONT-BE-009
