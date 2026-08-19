# ARCH-UI-014: Wire volume sliders to the mixer

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md volume SFX/music split; SettingsPanel stub |

## Summary

Settings expose Master / SFX / Music / Ambient / Weather sliders, persisted, driving ARCH-UI-012 gains.

## Context

Master volume already saves. Split was listed as P2 after real audio — pull it to P1 once the player exists.

## Acceptance criteria

- [ ] Five sliders (or master + four). Defaults 100% / sensible.
- [ ] Persist on `PlayerSave` settings.
- [ ] Mute-all via master at 0.
- [ ] Streamer mode does not force mute (separate concern).

## Out of scope

Per-ability SFX mix. Equalizer.

## Suggested files

- `SettingsPanel.tsx`
- `PlayerSave.cs`
- `lib/audio/`

## Dependencies

- ARCH-UI-012
