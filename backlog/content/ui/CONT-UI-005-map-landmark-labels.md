# CONT-UI-005: Map labels for pyramids / obelisks / megaliths

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | UI (React) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | OverworldMapPanel landmarks |

## Summary

Map icons/labels for new landmark types so pyramids and stone circles are readable when revealed.

## Context

Panel already draws landmarks if fog-revealed. May need a color/shape per Type.

## Acceptance criteria

- [ ] Pyramid, obelisk cluster, megalith types are distinct from village/lake.
- [ ] Unrevealed stay hidden (See Beyond exception unchanged).

## Out of scope

New icon PNGs can be placeholders.

## Suggested files

- `OverworldMapPanel.tsx`
- `lib/overworld-map.ts`

## Dependencies

- CONT-BE-016
