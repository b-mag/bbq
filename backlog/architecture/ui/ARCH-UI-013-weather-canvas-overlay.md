# ARCH-UI-013: Weather canvas overlay

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P1 |
| **Estimate** | L |
| **Status** | Todo |
| **Source** | New. Meteorological fog/rain/snow + dreamlike events. Distinct from fog-of-war. |

## Summary

Overworld canvas draws weather from the backend snapshot: morning fog banks, rain streaks, snow, and rare Lynch/Giger/Carcosa overlays. Particles are local; state is host-authored.

## Context

Fog-of-war is exploration memory (`FogOfWar`). Weather fog is atmosphere. Palette washes already exist per biome (`palettes.ts`). Weather should tint/wash, not replace tiles. Performance: camera-culled, cap particle count, 60fps target on the existing canvas.

## Acceptance criteria

- [ ] Reads weather snapshot (ARCH-BE-022) each poll; no local divergent sim while meshed.
- [ ] Fog: soft banks, heavier at dawn on shore/lake; does not hide unexplored-map logic.
- [ ] Rain: streaks + optional ground speckle; intensity from snapshot.
- [ ] Snow: slower flakes on snow/ice biome and during snow weather.
- [ ] Event overlays (CONT-BE-008 ids): at least hooks for cloud-waves, ichor rain, black-star snow, rare red-room flash — can use placeholder colors until ASSET-007.
- [ ] Does not draw weather in UI panels; canvas only.
- [ ] Cheap enough that two local peers on one machine stay smooth.

## Out of scope

Audio (ASSET-012). Event catalog design (CONT-BE-008). HUD clock (ARCH-UI-021).

## Suggested files

- `OverworldCanvas.tsx`
- `new lib/engine/weather.ts`
- `palettes.ts`

## Dependencies

- ARCH-BE-022
- ARCH-BE-023 for mesh. ASSET-006 for real particles.
