# CONT-UI-003: Weather flavor toasts

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | UI (React) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | New. Cassilda / cloud-waves lines when weather changes |

## Summary

When weather or a dream event starts, a short toast (once per change, not every tick): e.g. “The cloud-waves thicken.” / “Something like rain, but it does not wet the dust.”

## Context

Do not spam. One line per event start. Streamer-safe (no IP). Optional setting later.

## Acceptance criteria

- [ ] Map of weatherId/eventId → one line.
- [ ] Debounced; no toast on intensity jitter.
- [ ] Flame Cryptol toast style can be reused (“A pale coin in the ash.”).

## Out of scope

HUD clock (ARCH-UI-021).

## Suggested files

- `OverworldView.tsx`
- `new weather-flavor.ts`

## Dependencies

- ARCH-BE-022
- CONT-BE-008
