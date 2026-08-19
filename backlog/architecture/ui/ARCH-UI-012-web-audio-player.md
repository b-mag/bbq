# ARCH-UI-012: Web Audio sound player (buses)

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | New. Settings masterVolume is a stub; README §16 audio pass. Needed near-term. |

## Summary

Add a small Web Audio mixer in the frontend: buses for master / SFX / music / ambient / weather. Play one-shot SFX and looping beds by id. No gameplay requires matchmaking.

## Context

Grep shows no `AudioContext` / Howler usage. Master volume slider saves and does nothing. Keep zero extra npm deps if possible (`AudioContext` + `GainNode`). Unlock on first user gesture (browser autoplay policy). Fail silent if a file is missing.

## Acceptance criteria

- [ ] `lib/audio/` (or similar): `playSfx(id)`, `playLoop(bus, id)`, `stopLoop(bus)`, `setBusVolume(bus, 0..1)`.
- [ ] Buses: `master`, `sfx`, `music`, `ambient`, `weather`.
- [ ] Manifest JSON mapping id → `/assets/audio/...` so missing files do not crash.
- [ ] First click/key in the game canvas resumes the AudioContext.
- [ ] Master volume setting immediately drives the master gain (even before ARCH-UI-014 split sliders).
- [ ] One placeholder beep or silence is OK until ASSET-008+ land — the API must be ready.

## Out of scope

Composing music. Spatial stereo. Backend involvement (audio is client-only aside from weather id).

## Suggested files

- `new src/frontend/lib/audio/`
- `SettingsPanel.tsx`
- `OverworldView.tsx`

## Dependencies

- Unblocks ASSET-008–013 and ARCH-UI-014.

## Notes

Weather loops duck under music. SFX never duck music to zero. Lynch/Giger beds should be able to sit on `ambient` while rain sits on `weather`.
