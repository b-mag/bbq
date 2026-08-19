# CONT-UI-004: Dungeon enter flavor strings

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | UI (React) |
| **Priority** | P2 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md “The dock exhales…” |

## Summary

One loading line per scenario: Drowned Dock, Temple of Hali, Mountain Cave, Sunken Quay, Palace Crypt, Last House.

## Context

Wired by ARCH-UI-024.

## Acceptance criteria

- [ ] Strings live in one module, keyed by scenario id.
- [ ] No Warehouse wording.

## Out of scope

The loading UI state machine (ARCH-UI-024).

## Suggested files

- `new dungeon-flavor.ts or npc-dialogue.ts`

## Dependencies

- ARCH-BE-003 ids
