# ARCH-BE-017: DevStartingCryptol flag (0 in release)

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md currencies |

## Summary

First-time peer id grant of 1000 Cryptol is behind `Carcosa:DevStartingCryptol` (1000 in Development, 0 in release appsettings).

## Context

Ship starting Cryptol is 0. The 1000 grant is only so the slice can test a 1000-Cryptol listing without a payment pipeline.

## Acceptance criteria

- [ ] Development appsettings: 1000. Release: 0 / omitted.
- [ ] Grant only on first `CryptolStore` create for that player id.
- [ ] Flag name is explicit in appsettings (not a magic constant only in code).

## Out of scope

Steam/payment pipeline.

## Suggested files

- `appsettings.json`
- `appsettings.Development.json`
- `CryptolStore.cs`

## Dependencies

- None.
