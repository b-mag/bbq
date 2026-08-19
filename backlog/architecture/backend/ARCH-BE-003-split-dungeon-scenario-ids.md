# ARCH-BE-003: Split drowned_dock / warehouse / sunken_quay scenario ids

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P0 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | README.md §16; OverworldWorldGen Entrances(); QuestProgression.NormalizeDungeonId |

## Summary

Sunken Cyclopean Quay must stop collapsing into Drowned Dock. Palace Crypt must stop sharing the `temple` id with Temple of Hali.

## Context

`DungeonInstanceManager.ParseScenario` maps `warehouse` → Drowned Dock. `QuestProgression.NormalizeDungeonId` also folds `warehouse` into `drowned_dock`, so See Beyond never treats the Quay as a separate clear. Palace Crypt uses `scenario: temple` today. Keep `warehouse` as a deprecated *alias for drowned_dock only if the caller is the old dock*, not the Quay.

## Acceptance criteria

- [ ] Wire keys: `drowned_dock`, `sunken_quay` (new), `temple_of_hali`, `palace_crypt`, `mountain_cave`.
- [ ] `warehouse` remains a deprecated alias for `drowned_dock` only.
- [ ] See Beyond chain can distinguish Quay vs Dock vs Crypt vs Temple.
- [ ] Entrances in `OverworldWorldGen` use the new keys.
- [ ] Existing saves that stored `warehouse` still count as Drowned Dock.

## Out of scope

New map art (ASSET-019). Distinct Crypt generator (ARCH-BE-004).

## Suggested files

- `DungeonInstanceManager.cs`
- `QuestProgression.cs`
- `OverworldWorldGen.cs`
- `MapGenerator.cs`

## Dependencies

- None. Do before CONT-BE-011.
