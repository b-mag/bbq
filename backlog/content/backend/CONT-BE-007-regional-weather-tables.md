# CONT-BE-007: Regional weather weight tables

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | New. Swamp rains more; Waste rains less; north snows; Hali morning fog. |

## Summary

Data table: per biome, weights for clear / fog / rain / storm / snow plus morning-fog bias. Consumed by WeatherSystem. No code branches per biome scattered through UI.

## Context

Biomes on the 640 map: snow peaks, mountains, Court of the Dragon ash, Yellow Palaces, Waste, Lake Hali / Pallid Shore, Marshes of Yhtill, Dark Forest, village, west hamlet, southern shore.

## Acceptance criteria

- [ ] Table covers every overworld tile type or landmark region.
- [ ] Yhtill / swamp: high rain. Waste / ash: low rain, rare cinder-wind. Black Stars: snow. Hali/shore: morning fog. Village: mild. Forest: fog + rain.
- [ ] Documented in the story or a comment so artists know what loops to record.
- [ ] xUnit: swamp rain weight > waste rain weight.

## Out of scope

Dream events (CONT-BE-008). Sim ticker (ARCH-BE-022).

## Suggested files

- `WeatherSystem.cs or WeatherTables.cs`

## Dependencies

- ARCH-BE-022 (can land together).
