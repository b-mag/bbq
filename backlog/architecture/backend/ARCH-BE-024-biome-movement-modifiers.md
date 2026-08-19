# ARCH-BE-024: Biome movement modifiers

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | DigSystem artifact effects; OVERWORLD_VISION snow peaks; swamp |

## Summary

Snow/ice slows or costs stamina; swamp can drain stamina; shallow water slows — unless the matching dig artifact passive is owned (once ARCH-BE-025 wires them).

## Context

Tiles already exist: Snow=19, Swamp=17, ShallowWater=2, MountainPath=18, Ladder=23. Artifact bible already names the counters (`hali_tide_glass`, `yhtill_reed_whistle`, `hyades_black_star_nail`). Modifiers should live in one place so weather ice-storms can stack later.

## Acceptance criteria

- [ ] Documented default modifiers per tile (speed and/or stamina drain).
- [ ] Authoritative movement on the backend still reconciles (no client-only ice).
- [ ] Deep water remains lethal / unwalkable as today.
- [ ] Hooks for artifact overrides without implementing all passives in this ticket if ARCH-BE-025 follows immediately — at least the data table exists.

## Out of scope

Full artifact wiring (ARCH-BE-025). Weather event overrides (CONT-BE-008).

## Suggested files

- `OverworldCombatSync.cs`
- `useOverworldInput.ts (prediction must match)`
- `DigSystem.cs comments`

## Dependencies

- None. ARCH-BE-025 consumes this.
