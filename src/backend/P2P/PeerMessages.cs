// =============================================================================
// PeerMessages.cs — P2P Mesh Network Message Types
// =============================================================================
//
// OVERVIEW:
// Defines all message types exchanged between peers in the mesh network.
// These are SEPARATE from the client-facing GameMessage types (Messages.cs) —
// peer messages are for inter-server communication, not player UI.
//
// MESSAGE CATEGORIES:
//   1. Handshake — Version negotiation and identity exchange
//   2. State Sync — Overworld player position/state broadcasting
//   3. Peer Exchange — Sharing known peer lists for mesh discovery
//   4. Chat Relay — Forwarding chat messages across the mesh
//   5. Party Sync — Party state synchronization between peers
//   6. Admin — Server-wide announcements from the tracker
//   7. Validation — Anti-cheat alerts and vote-kick
//   8. Keepalive — Connection health monitoring
//
// DESIGN PRINCIPLES:
//   - Same envelope pattern as GameMessage (type discriminator + nullable payloads)
//   - AOT-compatible (source-generated serialization)
//   - Forward-compatible (unknown fields are ignored by older peers)
//   - Minimal payload size (only send what changed)
//
// NAMING CONVENTION:
//   All peer message types are prefixed with "Peer" to distinguish from
//   client-facing types. Wire format uses snake_case (camelCase via serializer).
// =============================================================================

using System.Text.Json.Serialization;

namespace Carcosa.Server.P2P;

/// <summary>
/// The universal message envelope for peer-to-peer communication.
/// Only one payload field is non-null for any given message.
/// </summary>
public sealed class PeerMessage
{
    /// <summary>Message type discriminator.</summary>
    public required string Type { get; init; }

    // --- Handshake ---
    public PeerHandshakePayload? Handshake { get; set; }
    public PeerHandshakeResponsePayload? HandshakeResponse { get; set; }

    // --- State Sync ---
    public PeerStateUpdatePayload? StateUpdate { get; set; }

    // --- Peer Exchange ---
    public PeerExchangePayload? PeerExchange { get; set; }

    // --- Chat ---
    public PeerChatRelayPayload? ChatRelay { get; set; }

    // --- Party ---
    public PeerPartyUpdatePayload? PartyUpdate { get; set; }

    // --- Admin ---
    public PeerAdminBroadcastPayload? AdminBroadcast { get; set; }

    // --- Validation / Anti-Cheat ---
    public PeerViolationPayload? Violation { get; set; }
    public PeerVoteKickPayload? VoteKick { get; set; }

    // --- Keepalive ---
    public PeerKeepalivePayload? Keepalive { get; set; }
    public PeerKeepaliveAckPayload? KeepaliveAck { get; set; }
}

/// <summary>
/// Message type constants for peer-to-peer protocol.
/// </summary>
public static class PeerMessageTypes
{
    // Handshake
    public const string Handshake = "handshake";
    public const string HandshakeResponse = "handshake_response";

    // State sync
    public const string StateUpdate = "state_update";

    // Peer exchange
    public const string PeerExchange = "peer_exchange";

    // Chat relay
    public const string ChatRelay = "chat_relay";

    // Party sync
    public const string PartyUpdate = "party_update";

    // Admin
    public const string AdminBroadcast = "admin_broadcast";

    // Anti-cheat
    public const string Violation = "violation";
    public const string VoteKick = "vote_kick";

    // Keepalive
    public const string Keepalive = "keepalive";
    public const string KeepaliveAck = "keepalive_ack";
}
