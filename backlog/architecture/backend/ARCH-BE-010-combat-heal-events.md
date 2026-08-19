# ARCH-BE-010: Combat heal events for frontend VFX

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P1 combat VFX |

## Summary

`ExecuteHealAoE` already heals allies in radius. Emit a combat event the frontend can render (`heal` with targetId, amount, x, y).

## Context

Overworld combat has no VFX hook for heals. Group heal confirmation needs a payload, not a new heal formula.

## Acceptance criteria

- [ ] Each healed entity (including self) produces a `heal` event in the combat/events snapshot.
- [ ] Event includes `targetId`, `amount`, `x`, `y`, `sourceId`.
- [ ] Existing heal numbers unchanged.
- [ ] AOT registered.

## Out of scope

Floating numbers and circle VFX (ARCH-UI-009, ARCH-UI-011).

## Suggested files

- `CombatSystem.cs`
- `OverworldCombatSync.cs`
- `Program.cs`

## Dependencies

- Pairs with ARCH-UI-011.
