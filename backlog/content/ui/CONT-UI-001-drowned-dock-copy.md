# CONT-UI-001: Drowned Dock HUD / copy leftovers

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | UI (React) |
| **Priority** | P0 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P0-D4 |

## Summary

Frontend strings, loading text, and join prompts say The Drowned Dock, not The Warehouse.

## Context

Backend entrance name already updated. UI/join toasts may still say Warehouse.

## Acceptance criteria

- [ ] Grep of src/frontend for Warehouse is clean except comments/changelog.
- [ ] Party join prompt uses the entrance name.

## Out of scope

Scenario id split (ARCH-BE-003).

## Suggested files

- `OverworldView.tsx`
- `npc-dialogue.ts`
- `page.tsx`

## Dependencies

- CONT-BE-001
