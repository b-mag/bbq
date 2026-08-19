# CONT-BE-008: Dreamlike / Lynch / Giger weather events

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | New. Alien/dreamlink/Lynch/Giger/Carcosa weather — not generic rain. |

## Summary

Catalog rare weather *events* with ids the renderer and audio player can hook: cloud-waves on Hali, ichor/oil rain on flesh/Giger ground, black-star snow, distant unexplained thunder, rare red-room chevron flash, twin-sun smear at dusk.

## Context

Keep events uncommon so they stay uncanny. Morning fog and regional rain are the baseline (CONT-BE-007). Events may be region-gated (ichor rain near giger houses / flesh tiles; cloud-waves only on lake/mist tiles).

## Acceptance criteria

- [ ] At least 5 event ids with region gates + rarity + duration.
- [ ] Ids stable for UI/audio/assets (`cloud_waves`, `ichor_rain`, `black_star_snow`, `lynch_thunder`, `red_room_flash`, `twin_sun_smear`).
- [ ] Never required for quest progress.
- [ ] Document tone: Chambers cloud-waves, Lynch dread (sound without source), Giger wet machinery — not comedy weather.

## Out of scope

Final overlay art (ASSET-007). Stingers (ASSET-013). Canvas hooks (ARCH-UI-013).

## Suggested files

- `WeatherSystem.cs`
- `maybe WeatherEvents.cs`

## Dependencies

- ARCH-BE-022
- CONT-BE-007
