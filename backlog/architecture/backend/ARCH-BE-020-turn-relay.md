# ARCH-BE-020: TURN relay fallback

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | PARKED |
| **Estimate** | L |
| **Status** | Todo |
| **Source** | NAT_TURN_GAP.md; implementations/VERTICAL_SLICE_BACKLOG.md PARKED |

## Summary

Optional TURN (coturn) URL in settings. Relay only when direct P2P fails (symmetric NAT / CGNAT). Do not start this during the vertical slice.

## Context

STUN + UPnP + Glyph + PEX exist. UPnP talks to the home router, not Carcosa-hosted process. TURN *would* be hosted. Decision: leave parked unless a new live failure appears.

## Acceptance criteria

- [ ] Direct WS attempted first; TURN only on failure.
- [ ] If TURN URL unset, behavior is unchanged.
- [ ] Offline mode never requires TURN.

## Out of scope

Everything else. Do not pull this into a slice sprint.

## Suggested files

- `NatTraversalService.cs`
- `PeerMesh.cs`
- `UdpMeshTransport.cs`

## Dependencies

- PARKED. Ignore until NAT failures block testers again.
