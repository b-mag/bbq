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

    /// <summary>Advertised available CPU headroom at connect time (percent).</summary>
    public int AvailableCpuPercent { get; set; }

    /// <summary>Advertised available memory at connect time (MB).</summary>
    public long AvailableMemoryMb { get; set; }

    /// <summary>Advertised upload bandwidth capacity (Mbps).</summary>
    public float UploadBandwidthMbps { get; set; }

    /// <summary>Advertised download bandwidth capacity (Mbps).</summary>
    public float DownloadBandwidthMbps { get; set; }
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

/// <summary>
/// UDP hole-punch hello. The joiner sends this to the glyph's STUN-mapped
/// address; the host replies with Ack=true from the same socket (opens the NAT).
/// Payload also carries TCP/UDP candidates so the host can punch back.
/// </summary>
public sealed class PeerUdpPunchPayload
{
    public required string PeerId { get; init; }
    public string DisplayName { get; set; } = "";
    public string TcpAddress { get; set; } = "";
    public string UdpAddress { get; set; } = "";
    public string WorldId { get; set; } = "";
    public bool Ack { get; set; }
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

    /// <summary>
    /// Event/action: invite, accept, leave, sync, update, formed, joined, left, disbanded, leader_changed.
    /// </summary>
    public string Event { get; init; } = "sync";

    /// <summary>Alias used by mesh party manager (same as Event).</summary>
    public string Action
    {
        get => Event;
        init => Event = value;
    }

    /// <summary>Current party leader's peer ID.</summary>
    public required string LeaderId { get; init; }

    public string LeaderPeerId
    {
        get => LeaderId;
        init => LeaderId = value;
    }

    /// <summary>All current member peer IDs.</summary>
    public required string[] MemberIds { get; init; }

    public string[] MemberPeerIds
    {
        get => MemberIds;
        init => MemberIds = value;
    }

    /// <summary>Member display names (same order as MemberIds). May be empty.</summary>
    public string[] MemberNames { get; init; } = Array.Empty<string>();

    /// <summary>Invite target or leave subject.</summary>
    public string? TargetPeerId { get; init; }

    /// <summary>Peer that sent this update.</summary>
    public string? SenderPeerId { get; init; }
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

// =============================================================================
// COMBAT SYNC PAYLOADS (Phase B — Overworld Combat)
// =============================================================================
// These payloads enable real-time combat in the P2P overworld. The shard host
// processes combat actions, runs enemy AI, and broadcasts results to all peers.

/// <summary>
/// Sent by a non-host peer when their player uses an ability.
/// The shard host receives this, processes the ability against overworld enemies,
/// and broadcasts PeerDamageEvent results to all peers.
/// 
/// Also sent by the host to itself (local processing) — same data structure
/// allows uniform handling regardless of host/non-host status.
/// </summary>
public sealed class PeerCombatActionPayload
{
    /// <summary>Peer ID of the player performing the combat action.</summary>
    public required string PeerId { get; init; }

    /// <summary>Ability ID being used (e.g., "ember_spray", "void_bolt").</summary>
    public required string AbilityId { get; init; }

    /// <summary>Aim angle in radians (0 = right, π/2 = down). Direction the ability fires toward.</summary>
    public float AimAngle { get; set; }

    /// <summary>Player X position when the ability was used (for server-side validation).</summary>
    public float SourceX { get; set; }

    /// <summary>Player Y position when the ability was used.</summary>
    public float SourceY { get; set; }

    /// <summary>UTC timestamp when the action was performed (for lag compensation).</summary>
    public long Timestamp { get; set; }
}

/// <summary>
/// Broadcast by the shard host at 10Hz (every 2nd tick) containing all enemy positions.
/// Non-host peers use this to render enemies in their correct positions.
/// 
/// WHY 10Hz (not 20Hz): Enemies move slowly (1.5-3 tiles/sec). 10Hz provides smooth
/// enough updates while halving combat sync bandwidth. Client-side interpolation
/// fills the gaps using velocity hints.
/// </summary>
public sealed class PeerEnemySyncPayload
{
    /// <summary>Array of all enemy entity states for rendering on non-host peers.</summary>
    public required PeerEnemySyncEntry[] Enemies { get; init; }

    /// <summary>Active projectiles for rendering on non-host peers. Null if no projectiles.</summary>
    public PeerProjectileSyncEntry[]? Projectiles { get; set; }
}

/// <summary>
/// State of a single projectile as broadcast by the shard host.
/// Allows non-host peers to render ability effects (ember spray, void bolt, etc.).
/// </summary>
public sealed class PeerProjectileSyncEntry
{
    /// <summary>Unique projectile ID.</summary>
    public required string Id { get; init; }

    /// <summary>Ability that created this projectile (for visual rendering).</summary>
    public string SubType { get; set; } = "";

    /// <summary>World X position.</summary>
    public float X { get; set; }

    /// <summary>World Y position.</summary>
    public float Y { get; set; }

    /// <summary>Velocity X (for interpolation).</summary>
    public float VelocityX { get; set; }

    /// <summary>Velocity Y.</summary>
    public float VelocityY { get; set; }
}

/// <summary>
/// State of a single enemy entity as broadcast by the shard host.
/// Contains everything a non-host peer needs to render and display the enemy.
/// </summary>
public sealed class PeerEnemySyncEntry
{
    /// <summary>Unique enemy entity ID.</summary>
    public required string Id { get; init; }

    /// <summary>Enemy sub-type (e.g., "gronk") for rendering.</summary>
    public string SubType { get; set; } = "";

    /// <summary>World X position (tile coordinates).</summary>
    public float X { get; set; }

    /// <summary>World Y position.</summary>
    public float Y { get; set; }

    /// <summary>Velocity X (for interpolation between sync updates).</summary>
    public float VelocityX { get; set; }

    /// <summary>Velocity Y.</summary>
    public float VelocityY { get; set; }

    /// <summary>Current health (for HP bar display).</summary>
    public int Health { get; set; }

    /// <summary>Maximum health (for HP bar percentage).</summary>
    public int MaxHealth { get; set; }

    /// <summary>Whether the enemy is alive (dead = show corpse).</summary>
    public bool IsAlive { get; set; }

    /// <summary>Peer ID of the player who tagged this enemy (for loot rights indicator).</summary>
    public string? TaggedBy { get; set; }
}

/// <summary>
/// Broadcast by the shard host whenever damage is dealt to any entity.
/// All peers use this for visual feedback (damage numbers, hit effects).
/// Also broadcast when an enemy dies (IsKill = true) for death animations.
/// 
/// WHY SEPARATE FROM ENEMY SYNC: DamageEvent is immediate (fired on the tick
/// damage occurs) while EnemySync is periodic (every 2nd tick). Separating them
/// ensures damage feedback is instant regardless of sync cycle timing.
/// </summary>
public sealed class PeerDamageEventPayload
{
    /// <summary>Peer ID of the player who dealt the damage (for kill attribution).</summary>
    public required string SourcePeerId { get; init; }

    /// <summary>Entity ID of the target that was hit (enemy or player).</summary>
    public required string TargetEntityId { get; init; }

    /// <summary>Amount of damage dealt (after defense reduction).</summary>
    public int Damage { get; set; }

    /// <summary>Target's health after this damage was applied.</summary>
    public int NewHealth { get; set; }

    /// <summary>True if this damage killed the target (triggers death animation).</summary>
    public bool IsKill { get; set; }

    /// <summary>World X position where the hit occurred (for floating damage numbers).</summary>
    public float X { get; set; }

    /// <summary>World Y position where the hit occurred.</summary>
    public float Y { get; set; }
}

// =============================================================================
// LOOT SYNC PAYLOADS (Phase 1 — Distributed Loot Distribution)
// =============================================================================

/// <summary>
/// Broadcast when an elite enemy is defeated. Each attacker generates a personal drop from seed.
/// </summary>
public sealed class PeerEliteDefeatedPayload
{
    public required string EliteId { get; init; }
    public required string EliteSubType { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
    public long ServerTickWhenDefeated { get; init; }
    public string[] AttackerPeerIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Broadcast active loot drops to peers (on creation or despawn sync).
/// </summary>
public sealed class PeerLootDropSyncPayload
{
    public required PeerLootDropEntry[] Drops { get; init; }
    public long SenderTick { get; init; }
}

/// <summary>
/// Serializable loot drop entry for P2P sync.
/// </summary>
public sealed class PeerLootDropEntry
{
    public required string DropId { get; init; }
    public required string ItemId { get; init; }
    public int Quantity { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public string[] EligiblePeerIds { get; init; } = Array.Empty<string>();
    public bool IsCollected { get; set; }
    public long CreatedAtServerTick { get; init; }
    public int DespawnAfterTicks { get; set; } = DeterministicLootGeneratorDefaults.DespawnAfterTicks;
    public string DropMode { get; set; } = "solo";
    public string? GenerationSeed { get; set; }
    public string? CollectedByPeerId { get; set; }
    public long CollectedAtTick { get; set; }
}

/// <summary>
/// Wire-format defaults referenced from gameplay without a project reference cycle.
/// </summary>
public static class DeterministicLootGeneratorDefaults
{
    public const int DespawnAfterTicks = 2400;
}

/// <summary>
/// Broadcast when any peer picks up loot (autonomous — no host approval).
/// </summary>
public sealed class PeerLootPickupPayload
{
    public required string DropId { get; init; }
    public required string PickedUpByPeerId { get; init; }
    public long ServerTick { get; init; }
}

/// <summary>
/// Broadcast when a solo-owned drop becomes fair game for all peers.
/// </summary>
public sealed class PeerLootFairGamePayload
{
    public required string DropId { get; init; }
    public long ServerTick { get; init; }
}

/// <summary>
/// Periodic peer capability metrics broadcast.
/// </summary>
public sealed class PeerMetricsUpdatePayload
{
    public required string PeerId { get; init; }
    public int CurrentCpuUsagePercent { get; set; }
    public float CurrentUploadUtilization { get; set; }
    public float CurrentDownloadUtilization { get; set; }
    public long Timestamp { get; set; }
}

/// <summary>
/// Host broadcasts XP awards for a kill. Eligible peers apply locally.
/// Full base XP each + party bonus when EligiblePeerIds.Length >= 2.
/// </summary>
public sealed class PeerXpAwardPayload
{
    public required string EnemyId { get; init; }
    public required string EnemySubType { get; init; }
    public int XpAmount { get; set; }
    public required string[] EligiblePeerIds { get; init; }
    public long ServerTick { get; init; }
}

/// <summary>Mesh dungeon instance start — shared deterministic seed, no matchmaking.</summary>
public sealed class PeerDungeonStartPayload
{
    public required string InstanceId { get; init; }
    public required string HostPeerId { get; init; }
    public required string Scenario { get; init; }
    public int Seed { get; set; }
    public int AvgLevel { get; set; }
    public required string[] PartyMemberIds { get; init; }
    public float EntranceX { get; set; }
    public float EntranceY { get; set; }
}

/// <summary>Host → members dungeon entity snapshot (compact).</summary>
public sealed class PeerDungeonStatePayload
{
    public required string InstanceId { get; init; }
    public int Tick { get; set; }
    public required PeerEnemySyncEntry[] Entities { get; init; }
    public int Wave { get; set; }
    public string Phase { get; set; } = "playing";
}

/// <summary>Dungeon ended — return to overworld.</summary>
public sealed class PeerDungeonCompletePayload
{
    public required string InstanceId { get; init; }
    public bool Victory { get; set; }
    public int XpBonus { get; set; }
}

/// <summary>Member → host dungeon input (ability use / movement hint).</summary>
public sealed class PeerDungeonInputPayload
{
    public required string InstanceId { get; init; }
    public required string PeerId { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
    public float AimAngle { get; set; }
    public string? AbilitySlot { get; set; }
}
