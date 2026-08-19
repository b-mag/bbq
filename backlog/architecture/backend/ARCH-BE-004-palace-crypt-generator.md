# ARCH-BE-004: Distinct Palace Crypt map generator

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | README.md §16; VERTICAL_SLICE_BACKLOG P0-D5 layouts |

## Summary

Palace Crypt gets its own `MapGenerator` layout (size, tile set, encounter rhythm) instead of cloning Temple of Hali.

## Context

Both palace crypt and temple currently share the `temple` scenario (100×100 temple generator). Crypt should feel underground / cyclopean / gold-on-black, not the same rooms with a different door.

## Acceptance criteria

- [ ] `MapScenario.PalaceCrypt` generates an 80×80 (or documented size) map distinct from Temple.
- [ ] Uses palace/crypt tile ids, not drowned-dock tiles.
- [ ] Seed-stable: same seed → same map.
- [ ] xUnit: two scenarios with the same seed are not identical grids.

## Out of scope

Art pass (ASSET-019). Boss unique AI.

## Suggested files

- `MapGenerator.cs`
- `DungeonInstanceManager.cs`
- `src/tests/MapGeneratorTests.cs`

## Dependencies

- ARCH-BE-003
