# ARCH-BE-002: Dungeon exit portal returns to the entrance

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P0 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P0-D2 |

## Summary

Every instanced dungeon ends with a teleport zone. Stepping on it (or interacting) returns the player to the overworld just outside the entrance they used.

## Context

`CompleteDungeonAsync` and `MarkLeftDungeon` already restore `LastSafeOverworldX/Y`. Nothing in the live dungeon path calls complete except old wave `game_over` / `victory` that never fire. Place an exit tile/zone in the generated map and honor `POST /api/gameplay/dungeon/complete`.

## Acceptance criteria

- [ ] Generated maps include a marked exit portal/zone past the boss room (or at the far end for empty maps until ARCH-BE-005).
- [ ] `POST /api/gameplay/dungeon/complete` clears `in_dungeon`, restores last-safe overworld coords, and returns those coords to the client.
- [ ] Victory vs leave-without-clear are distinct flags so quest (`NotifyDungeonComplete`) only fires on victory.
- [ ] Quit mid-dungeon still leaves `WasInDungeon` set (ARCH-UI-002 consumes this).

## Out of scope

Frontend applying the position (ARCH-UI-002). Boss packing (ARCH-BE-005).

## Suggested files

- `DungeonInstanceManager.cs`
- `MapGenerator.cs`
- `Program.cs`
- `PlayerSave.cs`

## Dependencies

- ARCH-BE-001
