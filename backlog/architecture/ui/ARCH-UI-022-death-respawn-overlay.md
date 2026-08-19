# ARCH-UI-022: Death / respawn overlay

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P2 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P2 death |

## Summary

On overworld death, a short overlay then respawn. Not dungeon spectate UI.

## Context

Pairs with ARCH-BE-030.

## Acceptance criteria

- [ ] Overlay on 0 HP overworld.
- [ ] Input blocked until respawn completes.
- [ ] Dungeon death does not use this overlay.

## Out of scope

Permadeath.

## Suggested files

- `OverworldView.tsx`
- `new DeathOverlay.tsx`

## Dependencies

- ARCH-BE-030
