# ARCH-BE-023: Weather state mesh broadcast

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | New. Peers in the same shard should see the same rain. |

## Summary

Shard host broadcasts compact weather state on `/ws/peer` at low frequency. Fog-of-war stays private. Weather is shared atmosphere, not exploration knowledge.

## Context

Invariant: quest/fog/dig stay local. Weather is the exception that *should* match for players standing in the same biome, or the mesh feels broken (one peer in rain, one in sun).

## Acceptance criteria

- [ ] Host sends `weather_state` (or folded into existing state) ~1/s or on change.
- [ ] Non-hosts apply host weather; they do not simulate a divergent clock while meshed.
- [ ] Solo / host-less: local sim from ARCH-BE-022.
- [ ] Payload is small (ids + intensity), not particle lists.
- [ ] `PeerJsonContext` updated.

## Out of scope

Particle rendering. Per-player fog-of-war.

## Suggested files

- `OverworldSync.cs`
- `PeerMessagePayloads.cs`
- `PeerJsonContext.cs`
- `WeatherSystem.cs`

## Dependencies

- ARCH-BE-022
