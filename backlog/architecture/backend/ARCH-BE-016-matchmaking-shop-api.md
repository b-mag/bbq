# ARCH-BE-016: Matchmaking shop catalog + buy

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md shop rules |

## Summary

Catalog comes from matchmaking so every connected player sees the same rotation. Spend deducts Cryptol only after matchmaking accepts the buy. Fail closed if matchmaking drops mid-trade.

## Context

These SKUs are the only Cryptol sinks. Unique item ids, not in `ItemRegistry` drop tables. Slice listing: one item at 1000 Cryptol. Do not use leftover wave-mode Cryptol awards in `SessionManager`.

## Acceptance criteria

- [ ] `GET /api/shop/catalog` and `POST /api/shop/buy` on matchmaking.
- [ ] Game server proxies; never invents listings locally.
- [ ] Buy fails if tracker/matchmaking is down; no local charge.
- [ ] `CryptolStore` deducts only after 2xx from matchmaking.
- [ ] Dev catalog: one 1000-Cryptol unique.
- [ ] Stop leftover wave victory Cryptol grants (or keep them disabled).

## Out of scope

Real-money Cryptol purchase. Dashboard editor can be a follow-up.

## Suggested files

- `src/matchmaking/Program.cs`
- `CryptolShopCatalog.cs`
- `CryptolStore.cs`
- `SessionManager.cs`

## Dependencies

- ARCH-BE-017 for starting balance. ARCH-UI-019 for the panel.
