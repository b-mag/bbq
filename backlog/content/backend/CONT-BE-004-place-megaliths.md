# CONT-BE-004: Place stonehenge-like megalith circles

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | New. Megaliths here and there — swamp, waste, peaks. |

## Summary

Place a handful of stone circles (lintels + standing stones) as composed layouts, not 400 rng trees. At least one in/near Marshes of Yhtill, one in The Waste, one on a mountain terrace.

## Context

These are mood and future ritual hooks (weather events can linger here). Walkable interior of the circle. Collision on stones only.

## Acceptance criteria

- [ ] ≥3 authored circles with stable ids (`megalith_circle` + stone pieces or one multi-sprite layout).
- [ ] Interior is walkable.
- [ ] Does not block Twin Suns Road or the river bridge.
- [ ] Landmark names (CONT-BE-016).

## Out of scope

Ritual gameplay. Art (ASSET-003).

## Suggested files

- `OverworldWorldGen.cs`

## Dependencies

- ASSET-003
- CONT-BE-016
