# ARCH-BE-011: Loot eligibility windows and visibility

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | P2P_LOOT_DISTRIBUTION_PLAN.md Phase 1 |

## Summary

Drops follow owned (0–60s) → fair (60–120s) → despawn. Visibility = can pickup. Non-host peers see drops they are eligible for.

## Context

Plan Phase 1 boxes are still unchecked. Overworld elite drops exist; dungeon loot waits on instances. Visibility rule is specified in the plan file.

## Acceptance criteria

- [ ] Solo kill: killer-only 60s, then fair, despawn 120s.
- [ ] Party any-one: party eligible until despawn.
- [ ] Elite: each attacker rolls a personal drop from the deterministic seed; no fair conversion.
- [ ] Ineligible peers do not see the drop.
- [ ] xUnit for the three modes.

## Out of scope

Autonomous pickup broadcast (ARCH-BE-012). Task assignment Phase 2.

## Suggested files

- `LootSystem.cs`
- `LootDropVisibility.cs`
- `LootSystemEnhancements.cs`
- `src/tests/LootDistributionTests.cs`

## Dependencies

- None for overworld. Dungeon drops need ARCH-BE-005.
