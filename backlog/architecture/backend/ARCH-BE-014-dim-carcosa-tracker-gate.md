# ARCH-BE-014: Dim Carcosa overlay gated on tracker

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P1 Dim Carcosa |

## Summary

When matchmaking is up, deep-water tiles in the Hali ellipse become a ruined city overlay. Offline, the lake is ordinary unwalkable water.

## Context

Lake is painted around ~(32%, 42%) on the 640 map (old docs said 55,90 on 200×200). Overlay is the same geography for every shard, visibility gated on `TrackerClient.IsTrackerOnline`. Not a random dungeon.

## Acceptance criteria

- [ ] Bootstrap/map payload includes `dimCarcosaVisible: bool` from tracker online.
- [ ] When visible, specified deep-water tiles become walkable drowned-street types.
- [ ] When tracker drops, overlay hides; players standing on former streets are pushed to nearest shore (fail closed, no drown).
- [ ] Cannot spend Cryptol while overlay is hidden.

## Out of scope

Street art (ASSET-016). Last House instance (ARCH-BE-015). Shop API (ARCH-BE-016).

## Suggested files

- `OverworldWorldGen.cs`
- `OverworldBootstrap.cs`
- `TrackerClient.cs`
- `Program.cs`

## Dependencies

- CONT-BE-009 for the actual street layout data.
