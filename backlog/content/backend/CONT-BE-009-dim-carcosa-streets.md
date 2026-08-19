# CONT-BE-009: Dim Carcosa drowned street layout

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md lake overlay; OVERWORLD_VISION |

## Summary

Author the drowned-street tile layout inside the Hali ellipse: walkable streets, empty ruined shells, broken colonnades, The Last House at the center island. Same for every shard.

## Context

Lake island + causeway already painted (`PaintLakeIslandAndCauseway`). `lake_shop` object already sits on the island. Overlay visibility is ARCH-BE-014.

## Acceptance criteria

- [ ] Fixed layout, not rng per enter.
- [ ] Most buildings unenterable.
- [ ] One enterable Last House at island center.
- [ ] Offline: layout data can exist but tiles stay deep water.

## Out of scope

Tracker gate (ARCH-BE-014). Tileset (ASSET-016).

## Suggested files

- `OverworldWorldGen.cs`
- `OverworldBootstrap.cs`

## Dependencies

- ARCH-BE-014
- ASSET-016
