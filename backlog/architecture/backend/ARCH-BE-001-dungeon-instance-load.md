# ARCH-BE-001: Solo dungeon loads from DungeonInstanceManager

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P0 |
| **Estimate** | L |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P0-D1 |

## Summary

Pressing E at a dungeon entrance must load *that* instance in the same exe, solo, with no second process and no leftover wave-lobby.

## Context

`POST /api/gameplay/dungeon/enter` already creates an in-process instance and marks the player `in_dungeon`. The frontend then opens `/ws` into `GameLoop`, which only auto-starts if launched with `--seed=` / `--scenario=`. Testers vanish from the overworld and sit on “Entering Dungeon…”. Pass `entrance.scenario` through. Expose a REST snapshot of `DungeonInstanceManager.ActiveMap` (tiles + entities) so the UI can render without the lobby.

## Acceptance criteria

- [ ] Solo E at Drowned Dock, Temple of Hali, or Mountain Cave loads that scenario’s generated map in this process.
- [ ] `GET /api/gameplay/dungeon/map` (or equivalent) returns tiles, entities, seed, avgLevel for the active instance.
- [ ] Scenario is taken from the entrance, never hardcoded `mountain_cave`.
- [ ] Player status stays `in_dungeon` (they vanish from the overworld on purpose).
- [ ] No second `Carcosa.Server` process is spawned.
- [ ] AOT: new DTOs registered on `AppJsonContext`.
- [ ] xUnit coverage for enter → snapshot for at least one scenario.

## Out of scope

Party pull-in, exit portal (ARCH-BE-002), elites/boss packing (ARCH-BE-005), unifying onto mesh (ARCH-BE-006).

## Suggested files

- `DungeonInstanceManager.cs`
- `Program.cs`
- `OverworldCombatSync.cs`
- `SessionManager.cs`

## Dependencies

- Pairs with ARCH-UI-001.
