# ASSET-010: World interaction SFX

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (audio) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | New. Pickup, dig, flame, door, NPC |

## Summary

One-shots: pickup, empty dig, productive dig, Offer to the Flame, door enter/exit, NPC talk blip, UI open/close (quiet).

## Context

Flame toast already has copy. Audio should feel like ash, not a shop till.

## Acceptance criteria

- [ ] Ids in manifest. Wired to the existing REST success paths on the client.
- [ ] UI clicks optional and very quiet.

## Out of scope

Voice acting.

## Suggested files

- `assets/audio/sfx/`
- `OverworldView.tsx glue`

## Dependencies

- ARCH-UI-012
