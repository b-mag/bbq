# ARCH-UI-009: Overworld combat VFX

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P1 combat VFX |

## Summary

Port dungeon `VisualEffectsSystem` placeholders onto the overworld canvas: slash, ember cone, bolt impact, heal circle, shield ring, shadow-step afterimage.

## Context

`useOverworldCombat` POSTs abilities with no local effect. `lib/engine/effects.ts` is wired only in dungeon `GameCanvas`. Placeholders are enough.

## Acceptance criteria

- [ ] Pale Blade / Bone Cleaver: slash arc in aim direction.
- [ ] Ember Spray: cone / spray particles.
- [ ] Void Bolt / Hex Dart: impact spark on hit.
- [ ] Warding Light / Grim Howl: expanding circle to AreaRadius, then fade.
- [ ] Iron Veil / Cinder Ward: brief ring.
- [ ] Shadow Step: afterimage / dissolve.

## Out of scope

AoE ghost preview (ARCH-UI-010). Heal numbers (ARCH-UI-011). Final VFX art.

## Suggested files

- `lib/engine/effects.ts`
- `OverworldCanvas.tsx`
- `useOverworldCombat.ts`
- `GameCanvas.tsx`

## Dependencies

- ARCH-BE-010 for heal events.
