# ARCH-UI-020: Key rebind table

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P1 settings |

## Summary

Small rebind table for WASD, E, I, F, G, K, ESC. Persist. Defaults documented.

## Context

Even a small table. Input currently lives in `lib/engine/input.ts` and `useOverworldInput.ts`.

## Acceptance criteria

- [ ] Rebind UI in Settings. Detect conflicts.
- [ ] Persisted. Reset to defaults.
- [ ] Overworld and dungeon paths both honor binds for the shared keys.

## Out of scope

Full dual-bind / gamepad map (can follow).

## Suggested files

- `SettingsPanel.tsx`
- `lib/engine/input.ts`
- `useOverworldInput.ts`
- `PlayerSave.cs`

## Dependencies

- None.
