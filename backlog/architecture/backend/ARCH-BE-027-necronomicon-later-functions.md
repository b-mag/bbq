# ARCH-BE-027: Necronomicon functions after See Beyond

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | README.md §16 P1 content; QuestProgression |

## Summary

Each later boss binds a named Necronomicon page/ability, not only `NecronomiconRank`. See Beyond remains the first function.

## Context

Drowned Dock victory binds See Beyond. Later bosses currently raise rank and replant the marker. Design wants named functions per dungeon in the chain: Temple → Cave → Quay → Crypt.

## Acceptance criteria

- [ ] Documented function ids per dungeon in QuestProgression.
- [ ] Use-item dispatches the newly bound function (even if the later ones are stubs that plant the next marker).
- [ ] Rank still increments; functions are additive.
- [ ] Tests for each bind.

## Out of scope

Full unique gameplay for every page (can stub). See Beyond already works.

## Suggested files

- `QuestProgression.cs`
- `KeyItemsPanel.tsx (if new use modes)`
- `QuestProgressionTests.cs`

## Dependencies

- ARCH-BE-003 for distinct dungeon ids. CONT-BE-011 for the chain order.
