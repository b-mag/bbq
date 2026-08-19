# ARCH-UI-007: Inspect overlay

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md ui-stack inspect unused |

## Summary

`ui-stack` already lists `'inspect'`. Open a read-only card: name, level, party status. No other-player inventory (local-authority items).

## Context

Inspect must not leak backpack/Key Items (those are local). Show only meshed public fields (name, hp if names/health setting on, status).

## Acceptance criteria

- [ ] Inspect opens from player menu.
- [ ] ESC/X closes via ui-stack.
- [ ] Does not show other players’ inventory, quest, or dig state.

## Out of scope

Equipment viewing in PvP.

## Suggested files

- `new InspectPanel.tsx`
- `ui-stack.ts`
- `OverworldView.tsx`

## Dependencies

- ARCH-UI-004
- ARCH-UI-006
