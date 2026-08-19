# CONT-BE-002: Place large pyramids on the overworld

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | New. Tentacle-over-pyramids palette already named. World should have large pyramids. |

## Summary

Place several large pyramid world-objects on the 640×640 map — Waste / desert approaches and at least one near the Yellow Palaces — with collision that matches a multi-tile footprint.

## Context

No pyramid objects exist in `OverworldWorldGen.Objects` today. Palette `tentacle_pyramids` is named for desert ochre + black sky-entity + pyramid gold. These are landmarks, not dungeons (unless a later ticket adds an entrance on one face).

## Acceptance criteria

- [ ] At least 3 large pyramids at authored coordinates (not pure RNG scatter).
- [ ] Collision radius / footprint blocks walking through the mass.
- [ ] Landmarks registered (CONT-BE-016 can be the same PR).
- [ ] Uses sprite ids from ASSET-001 (placeholder rect OK until art drops, but the type string must be stable: `pyramid_great`, etc.).
- [ ] Does not replace existing palace/ruin tiles.

## Out of scope

Interior of a pyramid. Tentacle boss. Art (ASSET-001).

## Suggested files

- `OverworldWorldGen.cs`
- `sprites/manifest.json (id even if placeholder)`

## Dependencies

- ASSET-001 for final art. CONT-BE-016 landmarks.
