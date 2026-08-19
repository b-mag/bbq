# ARCH-UI-015: Loot pickup prompt

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P2 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P2 |

## Summary

Ground loot shows an E prompt vs silent walk-over. Respect eligibility (if you cannot see it, no prompt).

## Context

Walk-over may already collect; testers need a readable prompt.

## Acceptance criteria

- [ ] When eligible loot is in range, show item name + E.
- [ ] Ineligible / expired: no prompt.
- [ ] Does not block movement.

## Out of scope

Need/greed rolls.

## Suggested files

- `OverworldView.tsx`
- `OverworldCanvas.tsx`
- `GameHUD.tsx`

## Dependencies

- ARCH-BE-011
