# ASSET-011: Ambient music beds per biome

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (audio) |
| **Priority** | P1 |
| **Estimate** | L |
| **Status** | Todo |
| **Source** | New. User: in-game music / ambient. README audio pass |

## Summary

Looping music/ambient beds: village, Pallid Shore / Hali, Waste, Great Swamp, mountains/Black Stars, ruins/palaces. Crossfade on biome change. Music bus.

## Context

Twin Peaks / Badalamenti-adjacent dread is welcome; chiptune hero fanfare is not. Beds should work under rain on the weather bus. Length 1–3 min seamless loops.

## Acceptance criteria

- [ ] ≥5 biome beds + a fallback.
- [ ] Crossfade 2–4s, no double-music.
- [ ] Respect music volume slider.
- [ ] Dungeon can reuse a darker bed or a dedicated dock loop (if time).

## Out of scope

Adaptive combat music layers (P2).

## Suggested files

- `assets/audio/music/`
- `lib/audio/ glue`

## Dependencies

- ARCH-UI-012
