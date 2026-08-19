# ASSET-003: Megalith / stonehenge sprites

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (art) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | New. Standing stones + lintels |

## Summary

Sprite kit for a stone circle: standing stone, lintel pair, fallen stone. Giger-adjacent weathering OK; keep SNES silhouette. Walkable gaps.

## Context

Composed in CONT-BE-004. Prefer pieces the world gen can stamp in a ring rather than one giant PNG that blocks the interior.

## Acceptance criteria

- [ ] Piece ids documented (upright, lintel, fallen).
- [ ] Collision per piece, not one huge radius.
- [ ] Palette: `gold` / `chartreuse` / swamp-adjacent, not bright grey granite.

## Out of scope

Ritual VFX.

## Suggested files

- `sprites/`
- `manifest.json`
- `catalog.json`

## Dependencies

- CONT-BE-004
