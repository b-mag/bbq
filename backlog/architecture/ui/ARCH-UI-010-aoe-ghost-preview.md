# ARCH-UI-010: AoE ghost preview

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md AoE preview |

## Summary

While holding RMB on HealAoE / RangedAoE, show a ghost circle (heal) or ghost cone (ember) at max range. Client-side only.

## Context

Required once VFX land. Setting “Show ability aim / AoE preview” defaults on (ARCH-UI-025).

## Acceptance criteria

- [ ] Ghost appears while aiming, disappears on release/cast.
- [ ] Respects the settings toggle when that ships; default on if the setting is missing.
- [ ] Does not send extra network traffic.

## Out of scope

Server-side validation of aim.

## Suggested files

- `OverworldCanvas.tsx`
- `useOverworldCombat.ts`

## Dependencies

- ARCH-UI-009
