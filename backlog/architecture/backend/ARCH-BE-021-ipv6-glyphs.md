# ARCH-BE-021: IPv6 Glyph codec

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | PARKED |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | NAT_TURN_GAP.md; README.md §16 |

## Summary

Extend `GlyphCodec` beyond IPv4. Parked with NAT work.

## Context

Glyph format is WORD-WORD-suffix encoding IPv4 + port + world index. IPv6 players cannot share a working code.

## Acceptance criteria

- [ ] IPv6 addresses encode and decode round-trip.
- [ ] Old IPv4 glyphs still decode.
- [ ] Existing tests in GlyphNatTests extended.

## Out of scope

UI changes beyond showing the longer code.

## Suggested files

- `GlyphCodec.cs`
- `src/tests/GlyphNatTests.cs`

## Dependencies

- PARKED.
