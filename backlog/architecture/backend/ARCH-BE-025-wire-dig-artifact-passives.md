# ARCH-BE-025: Wire dig artifact passives

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | SPRITE_TECHNICAL.md §6; DigSystem.Artifacts; README.md §16 |

## Summary

The 12 named dig artifacts already grant Key Items. Wire the documented passives to combat, AI, stamina, fog radius, shop prices, and See Beyond.

## Context

Passives are catalogued, not wired. Shovel is not granted yet (ARCH-BE-026). Ids and flavor are locked.

## Acceptance criteria

- [ ] Each non-secret and secret passive in the bible either works or is explicitly still-later (Nameless City Key stays inert here — ARCH-BE-028).
- [ ] Pallid Mask Shard: Agwan/cultist aggro delay +0.4s.
- [ ] Hali Tide-Glass: shallow water no slow.
- [ ] Yhtill Reed-Whistle: swamp no stamina drain.
- [ ] Waste Cinder Compass: See Beyond pulse faster on ash/desert.
- [ ] Black-Star Nail: ladders/mountain path no stamina.
- [ ] Cassilda's Song-Coin: Cryptol prices −1 (min 1).
- [ ] Dagon Scale: dig radius +0.5; productive spots can hum (flag for UI).
- [ ] Torn Playbill: fog reveal radius +2.
- [ ] Ash-Heart: +5 max HP; fire enemies −1 damage.
- [ ] Ink-Tooth: See Beyond visible with map closed.
- [ ] Second-Sun Lens: +0.3 move on dusk tiles until ARCH-BE-029 adds the active swap.
- [ ] xUnit per effect or a table-driven test.

## Out of scope

Shovel grant. Nameless City door. Lens active swap. Artifact sprites (ASSET-015).

## Suggested files

- `DigSystem.cs`
- `QuestProgression.cs`
- `CombatSystem.cs`
- `EnemyAI.cs`
- `CryptolShopCatalog.cs`

## Dependencies

- ARCH-BE-024 for movement. ARCH-BE-026 so players can actually dig in a playtest.
