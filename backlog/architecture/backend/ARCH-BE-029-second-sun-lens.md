# ARCH-BE-029: Second-Sun Lens day/night swap

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P2 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | SPRITE_TECHNICAL.md carcosa_second_sun_lens; README.md §16 |

## Summary

Using the secret NE-corner lens swaps the day/night (twin-sun) palette for ~20s. Until then the documented +0.3 dusk move speed remains.

## Context

Needs a real world clock (ARCH-BE-022) so “swap” has two palettes to exchange. Active use from Key Items.

## Acceptance criteria

- [ ] `POST /api/gameplay/key-items/use` on the lens starts a 20s swap.
- [ ] Frontend receives `paletteOverride` or `timeOfDayOverride` in the weather/clock snapshot.
- [ ] Cooldown documented. Does not persist across logout unless you choose to — document it.

## Out of scope

Full day/night art pass beyond existing palettes.

## Suggested files

- `QuestProgression.cs`
- `WeatherSystem.cs`
- `KeyItemsPanel.tsx`
- `palettes.ts`

## Dependencies

- ARCH-BE-022
- ARCH-BE-025 dusk speed
