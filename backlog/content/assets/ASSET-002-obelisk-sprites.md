# ASSET-002: Obelisk sprite variants

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (art) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | New. Distinct from ruined_pillar / bone_spire |

## Summary

2–3 obelisk variants (weathered gold, black ichor, pale limestone) ~24×48 to 32×64, collision ~0.3–0.5.

## Context

`ruined_pillar` is 24×40. Obelisks should be taller, tapering, King-in-Yellow / cyclopean — not Egyptian tourist props with hieroglyph clipart.

## Acceptance criteria

- [ ] Variants share a sheet or separate PNGs with manifest keys.
- [ ] Feet-anchored. catalog.json updated.

## Out of scope

Placement (CONT-BE-003).

## Suggested files

- `sprites/`
- `manifest.json`
- `catalog.json`

## Dependencies

- CONT-BE-003
