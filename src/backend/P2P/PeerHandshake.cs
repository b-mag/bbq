// =============================================================================
// PeerHandshake.cs — Handshake Negotiation Logic
// =============================================================================
//
// OVERVIEW:
// Handles the initial handshake when two peers connect. The handshake ensures:
//   1. Protocol compatibility (both peers speak the same wire format)
//   2. Game version compatibility (both run compatible simulations)
//   3. World shard matching (both are in the same world)
//   4. Identity exchange (each peer learns the other's ID and name)
//
// HANDSHAKE FLOW:
//   ┌────────┐                          ┌────────┐
//   │ Peer A │                          │ Peer B │
//   └───┬────┘                          └───┬────┘
//       │                                   │
//       │── WebSocket Connect ─────────────►│
//       │                                   │
//       │── Handshake (identity+version) ──►│
//       │                                   │  ← Validate versions
//       │◄── HandshakeResponse (accept) ────│
//       │                                   │
//       │   Connection established!         │
//       │   Begin state sync / PEX          │
//       │                                   │
//
// REJECTION CASES:
//   - Protocol version mismatch → reject with explanation
//   - Game major version mismatch → reject with explanation
//   - World shard mismatch → reject (wrong world)
//   - World full (>=100 peers) → reject with "world_full"
//   - Duplicate peer ID (already connected) → reject with "duplicate"
//
// THREAD SAFETY:
// Handshake methods are called from WebSocket handler threads (one per connection).
// They are stateless — all state lives in PeerIdentity and the PeerMesh.
// =============================================================================

namespace Carcosa.Server.P2P;

/// <summary>
/// Result of validating an incoming peer handshake.
/// </summary>
public sealed class HandshakeValidationResult
{
    /// <summary>Whether the handshake should be accepted.</summary>
    public bool Accepted { get; init; }

    /// <summary>Rejection reason (null if accepted).</summary>
    public string? RejectionReason { get; init; }

    /// <summary>Rejection code for programmatic handling.</summary>
    public string? RejectionCode { get; init; }

    /// <summary>Create a successful result.</summary>
    public static HandshakeValidationResult Accept() => new() { Accepted = true };

    /// <summary>Create a rejection result.</summary>
    public static HandshakeValidationResult Reject(string code, string reason) =>
        new() { Accepted = false, RejectionCode = code, RejectionReason = reason };
}

/// <summary>
/// Stateless handshake validation and message construction.
/// Called when a new peer connection is established.
/// </summary>
public static class PeerHandshake
{
    /// <summary>
    /// Create the outgoing handshake message for initiating a connection.
    /// Sent as the first message after WebSocket connection is established.
    /// </summary>
    /// <param name="localIdentity">Our peer identity.</param>
    /// <returns>A PeerMessage ready to send.</returns>
    public static PeerMessage CreateHandshakeMessage(PeerIdentity localIdentity)
    {
        return new PeerMessage
        {
            Type = PeerMessageTypes.Handshake,
            Handshake = new PeerHandshakePayload
            {
                PeerId = localIdentity.PeerId,
                DisplayName = localIdentity.DisplayName,
                ProtocolVersion = localIdentity.ProtocolVersion,
                GameVersionMajor = localIdentity.GameVersionMajor,
                GameVersionMinor = localIdentity.GameVersionMinor,
                GameVersionPatch = localIdentity.GameVersionPatch,
                WorldId = localIdentity.WorldId,
                PublicAddress = localIdentity.PublicAddress,
                Capabilities = new[] { "chat", "party", "dungeon", "pex", "loot_sync", "metrics" },
                AvailableCpuPercent = 50,
                AvailableMemoryMb = 2048,
                UploadBandwidthMbps = 50,
                DownloadBandwidthMbps = 100,
            }
        };
    }

    /// <summary>
    /// Validate an incoming handshake from a remote peer.
    /// Checks version compatibility, world shard, and capacity.
    /// </summary>
    /// <param name="incoming">The handshake payload from the remote peer.</param>
    /// <param name="localIdentity">Our own identity (for world/version comparison).</param>
    /// <param name="currentPeerCount">Number of peers currently in our world.</param>
    /// <param name="isAlreadyConnected">Function to check if peer ID is already in mesh.</param>
    /// <returns>Validation result indicating accept or reject with reason.</returns>
    public static HandshakeValidationResult Validate(
        PeerHandshakePayload incoming,
        PeerIdentity localIdentity,
        int currentPeerCount,
        Func<string, bool> isAlreadyConnected)
    {
        // Check 1: Protocol version must match exactly
        if (incoming.ProtocolVersion != PeerProtocol.ProtocolVersion)
        {
            var reason = PeerProtocol.GetIncompatibilityReason(
                incoming.ProtocolVersion, incoming.GameVersionMajor);
            return HandshakeValidationResult.Reject("protocol_mismatch",
                reason ?? "Protocol version mismatch");
        }

        // Check 2: Game major version must match
        if (incoming.GameVersionMajor != PeerProtocol.GameVersionMajor)
        {
            var reason = PeerProtocol.GetIncompatibilityReason(
                incoming.ProtocolVersion, incoming.GameVersionMajor);
            return HandshakeValidationResult.Reject("version_mismatch",
                reason ?? "Game version mismatch");
        }

        // Check 3: World shard must match (if we have one set)
        if (!string.IsNullOrEmpty(localIdentity.WorldId) &&
            !string.IsNullOrEmpty(incoming.WorldId) &&
            incoming.WorldId != localIdentity.WorldId)
        {
            return HandshakeValidationResult.Reject("world_mismatch",
                $"World shard mismatch (local: {localIdentity.WorldId}, " +
                $"remote: {incoming.WorldId})");
        }

        // Check 4: World capacity
        if (currentPeerCount >= PeerProtocol.MaxPeersPerWorld)
        {
            return HandshakeValidationResult.Reject("world_full",
                $"World is full ({currentPeerCount}/{PeerProtocol.MaxPeersPerWorld} peers)");
        }

        // Check 5: Duplicate peer ID (prevent self-connection or reconnect without cleanup)
        if (isAlreadyConnected(incoming.PeerId))
        {
            return HandshakeValidationResult.Reject("duplicate",
                $"Peer {incoming.PeerId} is already connected");
        }

        // Check 6: Prevent connecting to ourselves
        if (incoming.PeerId == localIdentity.PeerId)
        {
            return HandshakeValidationResult.Reject("self_connection",
                "Cannot connect to self");
        }

        // All checks passed
        return HandshakeValidationResult.Accept();
    }

    /// <summary>
    /// Create the handshake response message (sent back to the initiating peer).
    /// </summary>
    /// <param name="localIdentity">Our identity.</param>
    /// <param name="validationResult">The result of validating the incoming handshake.</param>
    /// <param name="currentPeerCount">Current world peer count (informational).</param>
    /// <returns>A PeerMessage ready to send.</returns>
    public static PeerMessage CreateHandshakeResponse(
        PeerIdentity localIdentity,
        HandshakeValidationResult validationResult,
        int currentPeerCount)
    {
        return new PeerMessage
        {
            Type = PeerMessageTypes.HandshakeResponse,
            HandshakeResponse = new PeerHandshakeResponsePayload
            {
                Accepted = validationResult.Accepted,
                PeerId = localIdentity.PeerId,
                DisplayName = localIdentity.DisplayName,
                RejectionReason = validationResult.RejectionReason,
                WorldPlayerCount = currentPeerCount,
                PublicAddress = localIdentity.PublicAddress,
            }
        };
    }
}
