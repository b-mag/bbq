# NAT / TURN Gap (Mesh Sustainability)

Carcosa’s mesh can run **without matchmaking** via Glyph + PEX + `known-peers.json` cache. Hard NAT still blocks some peer pairs.

## Implemented

- STUN via `NatTraversalService` (public address for Glyph / tracker)
- Tracker register/reflect (optional bootstrap)
- Peer Exchange + cache bootstrap
- Keepalive + timeout prune (wired in `OverworldSync`)

## Not implemented (tracked)

| Gap | Impact | Suggested direction |
| --- | --- | --- |
| **TURN relay** | Symmetric NAT / CGNAT peers cannot form direct WS | Optional TURN (coturn) URL in settings; relay only when direct fails |
| **UPnP / NAT-PMP** | Home routers without manual port forward | Optional library; consent in settings |
| **IPv6 Glyphs** | Glyph codec is IPv4-oriented | Extend `GlyphCodec` |
| **Bounded neighborhood** | Full mesh O(n²) at 100 peers | Zone/nearby-only combat sync (plan Phase 5b) |

## Product rule

Offline mode skips tracker. Solo and party **dungeons never require matchmaking** — local seed + `/ws/peer` only.
