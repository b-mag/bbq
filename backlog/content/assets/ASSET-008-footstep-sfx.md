# ASSET-008: Footstep SFX per biome

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (audio) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | New. VERTICAL_SLICE mentioned footstep stubs; user: SFX player near-term |

## Summary

Short footstep one-shots: dirt, sand/waste, shallow water, swamp, snow, stone/palace, wood/dock. Loop-safe (no obvious seam). Play from ARCH-UI-012 on stride.

## Context

No audio files exist yet. Keep them quiet, wet, and slightly wrong — Carcosa, not AAA boots. License-clear or original.

## Acceptance criteria

- [ ] ≥6 surface ids mapped in the audio manifest.
- [ ] Levels consistent (no one biome twice as loud).
- [ ] Works with master/SFX buses.

## Out of scope

The player (ARCH-UI-012). Animation stride wiring can be this PR’s UI glue or a tiny follow-up — prefer wiring playSfx on stride in this ticket if the player exists.

## Suggested files

- `src/frontend/public/assets/audio/sfx/`
- `audio manifest`

## Dependencies

- ARCH-UI-012
