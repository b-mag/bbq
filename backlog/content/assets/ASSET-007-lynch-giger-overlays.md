# ASSET-007: Lynch / Giger weather overlays

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (art) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | New. red_room and hanging_creature palettes already named |

## Summary

Full-screen or camera-space overlays for rare events: ichor rain (viscous drips), cloud-wave distortion, red-room chevron flash (very short), wet-brass Giger mist. Subtle. Not a jumpscare PNG.

## Context

Reference palettes: `red_room`, `organic_vessel`, `hanging_creature`, `tentacle_pyramids`. Overlays should multiply/wash, not opaque-block gameplay.

## Acceptance criteria

- [ ] Assets for the event ids in CONT-BE-008.
- [ ] Alpha-friendly. No unreadable combat.
- [ ] catalog notes usage.

## Out of scope

Event rarity tables (CONT-BE-008).

## Suggested files

- `assets/vfx/`
- `catalog.json`
- `palettes.json`

## Dependencies

- CONT-BE-008
- ARCH-UI-013
