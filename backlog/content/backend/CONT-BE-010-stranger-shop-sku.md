# CONT-BE-010: The Stranger NPC and shop SKU

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md The Stranger |

## Summary

Place The Stranger in The Last House. Pallid Mask energy, no true name. Slice catalog: one unobtainable-elsewhere item at 1000 Cryptol.

## Context

Talk/shop only while matchmaking is up. Unique item id not on drop tables.

## Acceptance criteria

- [ ] NPC id stable (`npc_stranger`).
- [ ] Dialogue refuses a true name.
- [ ] SKU unique, 1000 Cryptol, not in ItemRegistry drops.
- [ ] No other Cryptol vendor.

## Out of scope

Shop API (ARCH-BE-016). Sprite (ASSET-017).

## Suggested files

- `OverworldWorldGen.cs or Last House map`
- `ItemRegistry.cs`
- `npc-dialogue.ts`

## Dependencies

- ARCH-BE-015
- ARCH-BE-016
