# ARCH-BE-013: Tracker GET /worlds population

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P1 shard dropdown |

## Summary

Matchmaking exposes `{ worldId, playerCount, maxPlayers=100 }[]`. Game server polls only when the tracker is reachable.

## Context

`GET /api/p2p/shard` is local-only. `switchShard` already POSTs. Tracker register returns same-world peers. Need a worlds list for the UI dropdown.

## Acceptance criteria

- [ ] `GET /api/tracker/worlds` on matchmaking.
- [ ] Game server proxies or the frontend polls via local backend only if `TrackerClient.IsTrackerOnline`.
- [ ] maxPlayers is 100.
- [ ] Offline / tracker down: no call, no error toast spam.

## Out of scope

Dropdown UI (ARCH-UI-017). Friends bias (ARCH-BE-018).

## Suggested files

- `src/matchmaking/Program.cs`
- `TrackerClient.cs`
- `src/backend/Program.cs`

## Dependencies

- None.
