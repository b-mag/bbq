# ARCH-UI-024: Dungeon loading flavor

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P2 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P2 loading tooltip |

## Summary

While the instance snapshot loads, show a scenario-specific line (“The dock exhales…”) instead of a generic spinner forever.

## Context

Copy lives in CONT-UI-004. This ticket wires the UI state.

## Acceptance criteria

- [ ] Loading state uses flavor string by scenario.
- [ ] Error state is distinct from flavor (do not leave the poetic line up on failure).

## Out of scope

Writing the strings (CONT-UI-004).

## Suggested files

- `page.tsx`
- `OverworldView.tsx`

## Dependencies

- ARCH-UI-001
- CONT-UI-004
