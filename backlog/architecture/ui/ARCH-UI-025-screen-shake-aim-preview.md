# ARCH-UI-025: Screen shake + ability aim setting

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P1 settings |

## Summary

Settings: Screen shake (default on) for boss hits / Pale Blade. Show ability aim / AoE preview (default on) gates ARCH-UI-010.

## Context

Accessibility + crowded shards.

## Acceptance criteria

- [ ] Shake can be disabled; heavy hits no longer move the camera.
- [ ] AoE preview toggle gates the ghost.
- [ ] Persisted like ShowFps.

## Out of scope

Shake intensity slider (nice-to-have).

## Suggested files

- `SettingsPanel.tsx`
- `OverworldCanvas.tsx`
- `PlayerSave.cs`

## Dependencies

- ARCH-UI-010 for preview. Shake can ship with ARCH-UI-009.
