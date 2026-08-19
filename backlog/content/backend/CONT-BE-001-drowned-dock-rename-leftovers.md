# CONT-BE-001: Remaining Warehouse → Drowned Dock labels

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Backend (.NET) |
| **Priority** | P0 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P0-D4 |

## Summary

User-facing and analytics strings still saying Warehouse / The Warehouse become The Drowned Dock. Keep wire alias `warehouse` → drowned_dock.

## Context

OverworldWorldGen entrance name is already “The Drowned Dock”. Leftovers may remain in OverworldBootstrap, matchmaking analytics, SessionManager wave copy, frontend (CONT-UI-001).

## Acceptance criteria

- [ ] Grep for Warehouse/warehouse in player-facing strings is clean except documented alias comments.
- [ ] Matchmaking analytics labels updated if they still say Warehouse.
- [ ] King in Yellow / Hali coastal gate flavor preserved.

## Out of scope

Splitting quay id (ARCH-BE-003).

## Suggested files

- `OverworldBootstrap.cs`
- `SessionManager.cs`
- `src/matchmaking`

## Dependencies

- CONT-UI-001 for React copy.
