# ARCH-BE-006: Unify dungeon onto the mesh (retire /ws GameLoop path)

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | L |
| **Status** | Todo |
| **Source** | README.md §16 P1 mesh future; VERTICAL_SLICE_BACKLOG P0-D1 note |

## Summary

Instanced dungeons run on the same combat/sync model as the overworld (REST + `/ws/peer`), not a second WebSocket lobby into the wave-shooter `GameLoop`.

## Context

Today mesh `DungeonInstanceManager` allocates, then the UI drops into `/ws` `GameLoop` / `WaveSystem`. That is why dungeon feel lags the overworld. Long-term: one host, shared map, party members join the instance without a second protocol.

## Acceptance criteria

- [ ] Dungeon combat ticks through `OverworldCombatSync` (or a sibling instance tick) using the generated `TileMap`.
- [ ] Frontend no longer opens `/ws` for overworld-entered dungeons.
- [ ] Party members already in the mesh are pulled via existing `GET /api/gameplay/dungeon` poll.
- [ ] Legacy `/ws` may remain for botclient / headless tests until those are migrated; document the deprecation.
- [ ] Solo still works with nobody else online.

## Out of scope

TURN. Wave-mode nostalgia content.

## Suggested files

- `DungeonInstanceManager.cs`
- `OverworldCombatSync.cs`
- `GameLoop.cs`
- `SessionManager.cs`
- `Program.cs`

## Dependencies

- ARCH-BE-001
- ARCH-UI-001
