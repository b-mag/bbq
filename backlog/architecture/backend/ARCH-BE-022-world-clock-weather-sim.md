# ARCH-BE-022: World clock and weather simulation

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | L |
| **Status** | Todo |
| **Source** | New (not in existing docs). Twin-sun Carcosa day cycle + regional weather. |

## Summary

Authoritative overworld clock (Carcosa day with twin suns) and a weather sim: morning fog, regional rain, northern snow, plus hooks for dreamlike events. Shard host ticks weather; local solo ticks itself.

## Context

Settings have a master volume stub and no audio. There is fog-of-war, not meteorological fog. Snow tiles already paint on northern peaks (`OverworldWorldGen.PaintMountains`). Swamp (`Marshes of Yhtill`) exists as a biome. Weather must be deterministic enough that reconnecting peers converge, and regional: swamp rains more than The Waste; Black Stars can snow; Pallid Shore gets morning mist.

## Acceptance criteria

- [ ] New `WeatherSystem` (or similar) ticked by shard host / local solo at a slow rate (e.g. 1 Hz or every N combat ticks).
- [ ] World clock: at least dawn / morning / midday / dusk / night, derived from a Carcosa day length (document the minutes-per-day).
- [ ] REST snapshot: `{ timeOfDay, weatherId, intensity, regionHint, eventId? }` for the local player’s tile.
- [ ] Morning often rolls fog on Pallid Shore / Lake Hali / village; not mandatory every day.
- [ ] Rain chance is a per-biome weight (tables live in CONT-BE-007).
- [ ] Northern snow/ice biome can precipitate snow even when the south is clear.
- [ ] AOT DTOs registered. Save does **not** need to persist weather (recompute from clock + seed) unless an event is mid-play; document the choice.
- [ ] xUnit: biome weights change probabilities; clock wraps.

## Out of scope

Canvas particles (ARCH-UI-013). Audio loops (ASSET-012). Dream event catalog (CONT-BE-008). Mesh broadcast (ARCH-BE-023).

## Suggested files

- `new Gameplay/WeatherSystem.cs`
- `Program.cs`
- `OverworldCombatSync.cs`
- `ShardHost.cs`
- `PlayerSave.cs (only if persisting)`

## Dependencies

- CONT-BE-007 can land in the same PR as the tables, or stub equal weights first.

## Notes

Tone: Chambers cloud-waves, Lynch dread, Giger wet machinery — not generic Stardew rain. Keep the sim data-driven so content can add events without rewriting the ticker.
