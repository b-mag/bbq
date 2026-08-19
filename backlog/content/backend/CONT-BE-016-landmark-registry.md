# CONT-BE-016: Landmark entries for new world objects

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | OverworldWorldGen.Landmarks(); OverworldMapPanel |

## Summary

Add map landmarks for pyramids, major obelisk clusters, megalith circles, and the Great Swamp if not already named. Fog hides them until revealed.

## Context

Landmarks() is a static list. Map panel skips unrevealed landmarks unless See Beyond.

## Acceptance criteria

- [ ] New landmarks have Name, X, Y, Type.
- [ ] See Beyond does not auto-point at secrets or nameless-city dune.
- [ ] Types are useful for map icons (pyramid, obelisk, megalith, swamp).

## Out of scope

Icon art.

## Suggested files

- `OverworldWorldGen.cs`
- `OverworldMapPanel.tsx`

## Dependencies

- CONT-BE-002–004
