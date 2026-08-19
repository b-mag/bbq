# ARCH-BE-009: Flame offer 1% Cryptol drip + response DTO

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P1-A4 |

## Summary

Every successful Offer to the Flame pays the rarity Pale Marks table and rolls 1% to award exactly 1 Cryptol. Response includes both amounts.

## Context

Endpoint already burns a backpack slot and pays fixed Marks. Decision 2026-08-15: F-anywhere, no altar. Cryptol drip is flavor, never scaled by rarity. Pale Marks stay the common sink.

## Acceptance criteria

- [ ] Response `{ paleMarksGained, cryptolGained: 0|1 }`.
- [ ] 1% chance, always 1 Cryptol, independent of rarity.
- [ ] No nearby-altar check.
- [ ] AOT DTO registered.
- [ ] xUnit: 100% Marks table; drip is 0 or 1.

## Out of scope

Shop spend (ARCH-BE-016). HUD toast copy (CONT-UI, ARCH-UI).

## Suggested files

- `Program.cs offer-to-flame`
- `Cryptol/CryptolStore.cs`

## Dependencies

- None.
