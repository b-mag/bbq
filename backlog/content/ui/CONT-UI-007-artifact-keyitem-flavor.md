# CONT-UI-007: Dig artifact Key Items flavor

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | UI (React) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | SPRITE_TECHNICAL.md §6; KeyItemsPanel.tsx |

## Summary

Key Items panel shows name + bible flavor + “not yet wired” vs live effect for each artifact. Keep ids locked.

## Context

Until sprites exist, UI is text-only (already planned). After ARCH-BE-025, drop “not yet wired” per item.

## Acceptance criteria

- [ ] All 12 artifacts + shovel + necronomicon have readable flavor.
- [ ] Secrets do not reveal map coordinates in the UI.

## Out of scope

Sprites (ASSET-015). Passive combat wiring (ARCH-BE-025).

## Suggested files

- `KeyItemsPanel.tsx`
- `ItemRegistry.cs descriptions`

## Dependencies

- None.
