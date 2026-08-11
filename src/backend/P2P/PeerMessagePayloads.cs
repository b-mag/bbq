// =============================================================================
// PeerMessagePayloads.cs — Payload Definitions for P2P Messages
// =============================================================================
//
// Each payload class defines the data carried by a specific message type.
// Payloads are designed to be:
//   - Minimal: only include data the receiver needs
//   - Forward-compatible: new optional fields don't break old peers
//   - Serializable: all types are AOT-compatible (no polymorphism)
// =============================================================================

namespace Carcosa.Server.P2P;

// =============================================================================
// HANDSHAKE PAYLOADS
// =============================================================================

/// <summary>
/// Sent as the FIRST message when connecting to a peer.
/// Contains our identity and version info for compatibility checking.
/// 
/// Flow:
///   1. Peer A connects to Peer B via WebSocket
///   2. Peer A sends Handshake (this payload)
///   3. Peer B validates versions → sends HandshakeResponse
///   4. If accepted: connection proceeds. If rejected: disconnect.
/// </summary>
public sealed class PeerHandshakePayload
{
    /// <summary>Our unique peer ID (persistent across sessions).</summary>
    public required string PeerId { get; init; }

    /// <summary>Human-readable player display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>P2P wire protocol version. Must match exactly.</summary>
    public int ProtocolVersion { get; set; }

    /// <summary>Game major version. Must match for connection.</summary>
    public int GameVersionMajor { get; set; }

    /// <summary>Game minor version. Informational (doesn't gate connection).</summary>
    public int GameVersionMinor { get; set; }

    /// <summary>Game patch version. Informational.</summary>
    public int GameVersionPatch { get; set; }

    /// <summary>Which world shard we're in (for routing validation).</summary>
    public required string WorldId { get; init; }

    /// <summary>Our public address so the remote peer can share it with others.</summary>
    public string PublicAddress { get; set; } = "";

    /// <summary>Capabilities/features this peer supports (for future extensions).</summary>
    public string[] Capabilities { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Response to a handshake. Either accepts or rejects the connection.
/// </summary>
public sealed class PeerHandshakeResponsePayload
{
    /// <summary>True if the handshake was accepted and the connection can proceed.</summary>
    public bool Accepted { get; set; }

    /// <summary>Our peer ID (so the initiator knows who they connected to).</summary>
    public required string PeerId { get; init; }

    /// <summary>Our display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Human-readable rejection reason (null if accepted).</summary>
    public string? RejectionReason { get; set; }

    /// <summary>Current player count in this world (for capacity checking).</summary>
    public int WorldPlayerCount { get; set; }

    /// <summary>Our public address for Peer Exchange.</summary>
    public string PublicAddress { get; set; } = "";
}

// =============================================================================
// STATE SYNC PAYLOADS
// =============================================================================

/// <summary>
/// Broadcast by each peer every tick (or when state changes).
/// Contains the position and status of the local player controlled by this peer.
/// 
/// DESIGN: Each peer is AUTHORITATIVE over their own player's state.
/// They broadcast it to all peers, who render it and validate it.
/// This means a peer can only cheat about their OWN position (detectable
/// via speed/teleport checks), not about other players.
/// </summary>
public sealed class PeerStateUpdatePayload
{
    /// <summary>The peer ID of the player this state belongs to.</summary>
    public required string PeerId { get; init; }

    /// <summary>Display name (included for convenience, avoids lookup).</summary>
    public required string DisplayName { get; init; }

    /// <summary>World position X (tile coordinates, sub-tile precision).</summary>
    public float X { get; set; }

    /// <summary>World position Y.</summary>
    public float Y { get; set; }

    /// <summary>Velocity X (tiles/tick). Used for interpolation by receivers.</summary>
    public float VelocityX { get; set; }

    /// <summary>Velocity Y.</summary>
    public float VelocityY { get; set; }

    /// <summary>Player status: "exploring", "in_party", "in_dungeon".</summary>
    public string Status { get; set; } = "exploring";

    /// <summary>Party ID if in a party (null otherwise).</summary>
    public string? PartyId { get; set; }

    /// <summary>True if this player is their party's leader.</summary>
    public bool IsPartyLeader { get; set; }

    /// <summary>Server tick when this state was generated (for ordering/validation).</summary>
    public long Timestamp { get; set; }
}

// =============================================================================
// PEER EXCHANGE PAYLOADS
// =============================================================================

/// <summary>
/// Sent periodically to share our known peer list with connected peers.
/// This is how the mesh self-discovers: connect to 1 peer → learn about all peers.
/// 
/// HOW IT WORKS:
///   Every PeerExchangeIntervalSeconds, each peer sends this to all connections.
///   Recipients check for unknown peers and connect to them.
///   This ensures the mesh converges to full connectivity within ~2 exchange cycles.
/// </summary>
public sealed class PeerExchangePayload
{
    /// <summary>List of known peer addresses and IDs.</summary>
    public required PeerEndpoint[] Peers { get; init; }
}

/// <summary>
/// A single peer's connection information (shared via Peer Exchange).
/// </summary>
public sealed class PeerEndpoint
{
    /// <summary>The peer's unique ID.</summary>
    public required string PeerId { get; init; }

    /// <summary>Public address (IP:port) to connect to this peer.</summary>
    public required string Address { get; init; }

    /// <summary>Display name (informational).</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>World shard this peer is in.</summary>
    public string WorldId { get; set; } = "";
}

// =============================================================================
// CHAT RELAY PAYLOADS
// =============================================================================

/// <summary>
/// Chat message relayed between peers.
/// The originating peer sends to all connected peers. Peers do NOT re-relay
/// (to prevent infinite loops). Each message has a unique ID for deduplication.
/// </summary>
public sealed class PeerChatRelayPayload
{
    /// <summary>Unique message ID for deduplication (prevents loops).</summary>
    public required string MessageId { get; init; }

    /// <summary>Channel: "global", "nearby", "party".</summary>
    public required string Channel { get; init; }

    /// <summary>Peer ID of the original sender.</summary>
    public required string SenderId { get; init; }

    /// <summary>Display name of the sender.</summary>
    public required string SenderName { get; init; }

    /// <summary>Message text (already profanity-filtered by sender).</summary>
    public required string Text { get; init; }

    /// <summary>Sender's position at time of message (for "nearby" radius check).</summary>
    public float SenderX { get; set; }

    /// <summary>Sender's position Y.</summary>
    public float SenderY { get; set; }

    /// <summary>UTC timestamp when message was sent.</summary>
    public long Timestamp { get; set; }
}

// =============================================================================
// PARTY SYNC PAYLOADS
// =============================================================================

/// <summary>
/// Party state synchronization between peers.
/// When a party forms, changes, or disbands, the leader broadcasts the update.
/// </summary>
public sealed class PeerPartyUpdatePayload
{
    /// <summary>Party ID.</summary>
    public required string PartyId { get; init; }

    /// <summary>Event type: "formed", "joined", "left", "disbanded", "leader_changed".</summary>
    public required string Event { get; init; }

    /// <summary>Current party leader's peer ID.</summary>
    public required string LeaderId { get; init; }

    /// <summary>All current member peer IDs.</summary>
    public required string[] MemberIds { get; init; }

    /// <summary>Member display names (same order as MemberIds).</summary>
    public required string[] MemberNames { get; init; }
}

// =============================================================================
// ADMIN BROADCAST PAYLOADS
// =============================================================================

/// <summary>
/// Admin message from the matchmaking service (relayed through tracker-connected peers).
/// Displayed prominently to all players in the world.
/// </summary>
public sealed class PeerAdminBroadcastPayload
{
    /// <summary>The admin message text.</summary>
    public required string Message { get; init; }

    /// <summary>Message priority: "info", "warning", "critical".</summary>
    public string Priority { get; set; } = "info";

    /// <summary>How long to display the message (seconds). 0 = until dismissed.</summary>
    public int DurationSeconds { get; set; } = 15;

    /// <summary>UTC timestamp when the message was issued.</summary>
    public long Timestamp { get; set; }

    /// <summary>Unique ID for deduplication (admin may send to multiple peers).</summary>
    public required string MessageId { get; init; }
}

// =============================================================================
// ANTI-CHEAT PAYLOADS
// =============================================================================

/// <summary>
/// Raised when a peer detects another peer violating game rules.
/// If enough peers report the same violation, the offender is kicked.
/// </summary>
public sealed class PeerViolationPayload
{
    /// <summary>Peer ID of the alleged cheater.</summary>
    public required string OffenderId { get; init; }

    /// <summary>Peer ID of the reporter.</summary>
    public required string ReporterId { get; init; }

    /// <summary>Violation type: "speed_hack", "teleport", "wall_clip".</summary>
    public required string ViolationType { get; init; }

    /// <summary>Human-readable description of the violation.</summary>
    public string? Description { get; set; }

    /// <summary>UTC timestamp of the violation.</summary>
    public long Timestamp { get; set; }
}

/// <summary>
/// Vote to kick a peer from the world. Requires majority consensus.
/// </summary>
public sealed class PeerVoteKickPayload
{
    /// <summary>Peer ID to kick.</summary>
    public required string TargetId { get; init; }

    /// <summary>Peer ID casting the vote.</summary>
    public required string VoterId { get; init; }

    /// <summary>Reason for the kick vote.</summary>
    public string? Reason { get; set; }
}

// =============================================================================
// KEEPALIVE PAYLOADS
// =============================================================================

/// <summary>
/// Periodic ping sent to each connected peer to verify liveness.
/// If no KeepaliveAck is received within PeerTimeoutSeconds, the peer is
/// considered disconnected and removed from the mesh.
/// </summary>
public sealed class PeerKeepalivePayload
{
    /// <summary>Monotonically increasing sequence number for matching acks.</summary>
    public long Sequence { get; set; }

    /// <summary>Sender's current UTC timestamp (for RTT calculation).</summary>
    public long Timestamp { get; set; }
}

/// <summary>
/// Response to a keepalive ping. Echoes the sequence number.
/// </summary>
public sealed class PeerKeepaliveAckPayload
{
    /// <summary>Echoed sequence number from the Keepalive.</summary>
    public long Sequence { get; set; }

    /// <summary>Responder's timestamp (for clock drift estimation).</summary>
    public long Timestamp { get; set; }
}
