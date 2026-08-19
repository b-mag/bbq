# ARCH-UI-019: Live Cryptol shop panel

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md shop; CryptolShopPanel exists? |

## Summary

The Stranger’s shop lists matchmaking catalog, shows Cryptol balance, buy round-trips through the local backend. “Acquire Cryptol” can be disabled/coming soon. Fail closed.

## Context

HUD should show Cryptol (at least when > 0, or always for dev). Flame toast “A pale coin in the ash.” is separate copy (CONT-UI).

## Acceptance criteria

- [ ] Catalog from proxy, not hardcoded SKUs in React.
- [ ] Buy disabled if tracker down or cannot afford.
- [ ] Success grants the unique item to inventory/Key Items as the SKU specifies.
- [ ] Pale Marks are not spendable here.

## Out of scope

Payment pipeline.

## Suggested files

- `CryptolShopPanel.tsx`
- `GameHUD.tsx`
- `FlameOfferingPanel.tsx`

## Dependencies

- ARCH-BE-016
- ARCH-BE-009 for HUD drip toast
