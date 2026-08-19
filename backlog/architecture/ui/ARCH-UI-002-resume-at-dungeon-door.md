# ARCH-UI-002: Resume at dungeon door from save

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P0 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P0-D2 P0-D3 |

## Summary

Map load and dungeon complete must spawn from save / player-stats position (`LastX/LastY`, `WasInDungeon`), not always the village `spawnPoint`.

## Context

Backend already stores `WasInDungeon` + `LastSafeOverworldX/Y`. Combat sync restores them onto `_localPlayer`. Overworld frontend ignores this and teleports to village spawn.

## Acceptance criteria

- [ ] Quit/crash inside a dungeon → next launch is in front of that entrance.
- [ ] Stepping on the exit portal applies returned coords immediately.
- [ ] `WasInDungeon` clears after a successful overworld resume so a later village logout does not snap back to the door.

## Out of scope

Backend complete endpoint (ARCH-BE-002).

## Suggested files

- `OverworldView.tsx`
- `useOverworldInput.ts`
- `usePlayerStats.ts`

## Dependencies

- ARCH-BE-002
