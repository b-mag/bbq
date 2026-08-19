# CONT-BE-003: Scatter obelisks across the land

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | New. Obelisks dotting the land. |

## Summary

Scatter authored + lightly randomized obelisks on path edges, Waste, ash, and palace approaches. Collision so they are readable landmarks, not clutter in doorways.

## Context

We have `ruined_pillar` and `bone_spire` / `dark_tower`. Obelisks should feel intentional (King in Yellow / cyclopean), not another tree loop. Prefer a seeded list of ~12–20 plus a few rng in legal biomes.

## Acceptance criteria

- [ ] Obelisk type id stable (`obelisk`).
- [ ] Not placed in village streets, water, or dungeon entrance tiles.
- [ ] Visible on the map when fog is revealed (landmark optional; at least world objects).
- [ ] Collision ~0.3–0.5 tiles.

## Out of scope

Puzzle activation. Art variants (ASSET-002).

## Suggested files

- `OverworldWorldGen.cs`

## Dependencies

- ASSET-002
