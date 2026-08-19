# ARCH-BE-030: Overworld death and respawn

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P2 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P2 |

## Summary

Dying on the overworld respawns at a safe point (village bed / last shrine / last-safe tile) with a defined HP/item penalty. Distinct from dungeon spectate.

## Context

Overworld death vs dungeon spectate is currently unclear. Last-safe coords already exist for dungeon quit.

## Acceptance criteria

- [ ] 0 HP on overworld triggers respawn, not a stuck corpse.
- [ ] Respawn position is documented (recommend fishing village spawn or last-safe overworld tile).
- [ ] Dungeon death does not use this path.
- [ ] Mesh: other peers see you reappear; no duplicate bodies.

## Out of scope

UI overlay (ARCH-UI-022). Hardcore permadeath.

## Suggested files

- `OverworldCombatSync.cs`
- `PlayerSave.cs`
- `Program.cs`

## Dependencies

- None.
