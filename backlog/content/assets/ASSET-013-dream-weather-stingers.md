# ASSET-013: Dream weather stingers

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (audio) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | New. One-shots for CONT-BE-008 events |

## Summary

Short stingers: cloud-waves swell, ichor drip cluster, black-star chime, red-room reverse-whoosh, twin-sun smear. Rare; must not annoy in a 2-player test.

## Context

Play once at event start on SFX or a dedicated one-shot path that still respects SFX volume.

## Acceptance criteria

- [ ] One stinger per event id (or silence documented).
- [ ] No looping stingers.
- [ ] Levels quieter than combat hit.

## Out of scope

Composing a full event score.

## Suggested files

- `assets/audio/stingers/`
- `CONT-UI-003 toast can fire the same moment`

## Dependencies

- ARCH-UI-012
- CONT-BE-008
