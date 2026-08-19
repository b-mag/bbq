# ARCH-BE-015: Last House fixed instance

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md The Last House |

## Summary

Entering The Last House uses dungeon instance rules (vanish, return to the lake door) but a fixed layout — no seed roll, no trash/elites/boss for the slice.

## Context

Name locked: **The Last House** (not Pallid Exchange). Scenario key `last_house`, not in the random dungeon roster. Exit returns to Dim Carcosa streets, not the fishing village.

## Acceptance criteria

- [ ] `last_house` is a fixed map, identical for every player.
- [ ] Enter/complete follow the same status flags as dungeons.
- [ ] Exit coords are the lake-street door, not village spawn.
- [ ] No combat required to walk to The Stranger.

## Out of scope

Stranger shop SKU (CONT-BE-010). Shop buy API (ARCH-BE-016). Interior art (ASSET-017).

## Suggested files

- `DungeonInstanceManager.cs`
- `MapGenerator.cs`
- `OverworldWorldGen.cs`

## Dependencies

- ARCH-BE-001
- ARCH-BE-014
