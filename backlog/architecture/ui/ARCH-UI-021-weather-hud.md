# ARCH-UI-021: Weather / time-of-day HUD

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P2 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | New. Twin suns / cloud-waves readability |

## Summary

A discreet HUD or map chrome shows time-of-day and current weather id (flavor name, not debug). Optional; default on for slice testers, off for streamer-clean.

## Context

Players need to learn that morning fog is a thing. Do not make it a weather-app widget.

## Acceptance criteria

- [ ] Shows e.g. “Morning — pallid mist” / “Night — black-star snow”.
- [ ] Hides in streamer mode if that would leak nothing useful — actually weather is diegetic, keep it.
- [ ] Does not cover ability bar.

## Out of scope

Forecast for tomorrow.

## Suggested files

- `GameHUD.tsx`
- `OverworldView.tsx`

## Dependencies

- ARCH-BE-022
- CONT-UI-003 for names
