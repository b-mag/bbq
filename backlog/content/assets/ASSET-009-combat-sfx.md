# ASSET-009: Combat SFX

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (audio) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | VERTICAL_SLICE P2 footstep/hit/heal audio stubs |

## Summary

One-shots: melee swing/hit, ember whoosh, bolt impact, heal chime (uneasy, not cute), shield, death. Placeholders OK if original and in-palette sonically.

## Context

Wire to overworld VFX casts if possible. Grim Howl should not sound like a cartoon wolf.

## Acceptance criteria

- [ ] Mapped to ability ids / hit / death.
- [ ] SFX bus. No music ducking required.
- [ ] Missing file = silence, not exception.

## Out of scope

Unique layer for every enemy type.

## Suggested files

- `assets/audio/sfx/`
- `useOverworldCombat.ts glue`

## Dependencies

- ARCH-UI-012
- ARCH-UI-009 nice-to-have same time
