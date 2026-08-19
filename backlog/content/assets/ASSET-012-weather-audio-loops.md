# ASSET-012: Weather audio loops

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Assets (audio) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | New. Rain, wind, thunder, snow hush, lake cloud-waves |

## Summary

Looping weather beds on the weather bus: rain, heavy rain, wind, snow hush, distant thunder bed, Hali wave/cloud wash, swamp insects/bubbles (slightly wrong).

## Context

Intensity from weather snapshot. Duck under dialogue if we ever add VO — not now. Thunder should sometimes have no lightning (Lynch).

## Acceptance criteria

- [ ] Loops for clear (optional wind), fog, rain, storm, snow.
- [ ] Volume follows intensity 0–1.
- [ ] Stops cleanly when weather clears.

## Out of scope

Event stingers (ASSET-013).

## Suggested files

- `assets/audio/weather/`
- `lib/audio/`
- `weather snapshot consumer`

## Dependencies

- ARCH-UI-012
- ARCH-BE-022
