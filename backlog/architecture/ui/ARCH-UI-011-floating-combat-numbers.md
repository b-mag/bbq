# ARCH-UI-011: Damage / heal floating numbers

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P1 + P2 |

## Summary

Damage numbers on enemies (white) vs players (red). Heals: small red arrows up + floating `+X HP` on each healed peer.

## Context

Needs heal events from ARCH-BE-010. Damage events likely already exist on the overworld snapshot — hook them.

## Acceptance criteria

- [ ] Enemy damage white, player damage red.
- [ ] Heal: `+X HP` at target feet/head.
- [ ] Numbers rise and fade; do not persist.

## Out of scope

Crit styling. Elite telegraph (P2 leftover).

## Suggested files

- `OverworldCanvas.tsx`
- `effects.ts`
- `useOverworldEnemies.ts`

## Dependencies

- ARCH-BE-010
