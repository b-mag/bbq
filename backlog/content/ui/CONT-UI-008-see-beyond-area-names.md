# CONT-UI-008: See Beyond area names

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | UI (React) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | QuestProgression See Beyond marker; OverworldMapPanel |

## Summary

See Beyond pulse label uses the real area name (Temple of Hali, Mountain Cave, …) not leftover Warehouse / generic “dungeon”.

## Context

Marker already ignores fog. Labels must match ARCH-BE-003 ids.

## Acceptance criteria

- [ ] Each chain step has a player-facing label.
- [ ] Pulse stops copy is clear when the boss is dead.

## Out of scope

New functions (ARCH-BE-027).

## Suggested files

- `OverworldMapPanel.tsx`
- `QuestProgression.cs marker label`

## Dependencies

- ARCH-BE-003
- CONT-BE-011
