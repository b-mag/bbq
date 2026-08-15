// =============================================================================
// Messages.cs — Game Network Protocol Message Definitions
// =============================================================================
//
// WHY THIS DESIGN (Discriminated Union via "Type" field):
// All WebSocket messages use a single envelope type (GameMessage) with a string
// discriminator ("type") and nullable payload fields. This pattern is chosen because:
//   1. AOT-compatible: No polymorphic deserialization needed (which requires reflection)
//   2. Simple framing: One JSON object per WebSocket text frame
//   3. Flat structure: Easy to serialize/deserialize without type hierarchies
//   4. Language-agnostic: The TypeScript client mirrors this exactly
//
// The tradeoff is that the envelope class has many nullable fields (one per message type),
// but only one is populated per message. The WhenWritingNull serialization option ensures
// null fields are omitted from the wire format, keeping messages small.
//
// WHY NOT PROTOBUF/MESSAGEPACK:
// JSON is human-readable (critical for debugging during development), has native
// TypeScript support, and the source-generated serializer makes it fast enough.
// At 20 state updates/sec with 8 players, JSON bandwidth is ~50-100KB/s which is
// negligible. Binary formats would save bandwidth but add complexity. Can switch later.
//
// IMPORTANT FOR AOT:
// Every type referenced here MUST also be registered in GameJsonContext.cs.
// Adding a new payload type without registering it will silently break in AOT builds.
// =============================================================================

using System.Text.Json.Serialization;

namespace Carcosa.Server.Network;

// --- Base message envelope ---

/// <summary>
/// The universal message envelope for all WebSocket communication.
/// Uses a "type" string discriminator to identify which payload is present.
/// Only one payload field will be non-null for any given message.
/// 
/// WHY "required string Type": The init-only required property ensures every
/// message has a type set at construction time. The AOT serializer needs this
/// to be a concrete property (not a discriminator attribute).
/// </summary>
public sealed class GameMessage
{
    /// <summary>Message type discriminator. Determines which payload field is populated.</summary>
    public required string Type { get; init; }

    // --- Connection lifecycle messages ---
    public PlayerJoinedPayload? PlayerJoined { get; set; }
    public PlayerLeftPayload? PlayerLeft { get; set; }

    // --- Input messages (client → server) ---
    public PlayerInputPayload? PlayerInput { get; set; }

    // --- State messages (server → client) ---
    public GameStatePayload? GameState { get; set; }
    public MapDataPayload? MapData { get; set; }
    public GameEventPayload? GameEvent { get; set; }

    // --- Chat messages (bidirectional) ---
    public ChatMessagePayload? Chat { get; set; }

    // --- Session management messages ---
    public SessionInfoPayload? SessionInfo { get; set; }
    public SessionActionPayload? SessionAction { get; set; }

    // --- Connection health ---
    public PingPayload? Ping { get; set; }
    public PongPayload? Pong { get; set; }

    // --- Error messages (server → client) ---
    public ErrorPayload? Error { get; set; }
}

// --- Message type string constants ---
// WHY CONSTANTS: Avoids string typos and enables switch statement pattern matching.
// These must match exactly between server and client (TypeScript uses identical strings).

public static class MessageTypes
{
    public const string PlayerJoined = "player_joined";
    public const string PlayerLeft = "player_left";
    public const string PlayerInput = "player_input";
    public const string GameState = "game_state";
    public const string MapData = "map_data";
    public const string Chat = "chat";
    public const string SessionInfo = "session_info";
    public const string SessionAction = "session_action";
    public const string GameEvent = "game_event";
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string Error = "error";
}

// =============================================================================
// Payload Types
// =============================================================================

/// <summary>
/// Sent when a player connects to the server.
/// The server sends this to the connecting player (so they know their ID)
/// and broadcasts it to all other players (so they know someone joined).
/// </summary>
public sealed class PlayerJoinedPayload
{
    /// <summary>Server-assigned unique ID for this player session.</summary>
    public required string PlayerId { get; init; }
    /// <summary>Display name chosen by the player on the connect screen.</summary>
    public required string PlayerName { get; init; }
    /// <summary>Selected class (set later during lobby phase).</summary>
    public string? SelectedClass { get; set; }
}

/// <summary>
/// Broadcast when a player disconnects (clean or abrupt).
/// </summary>
public sealed class PlayerLeftPayload
{
    public required string PlayerId { get; init; }
    /// <summary>Reason for disconnect (e.g., "disconnected", "kicked").</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Client → Server: Player's input state for one tick.
/// 
/// WHY SEQUENCE NUMBER: Enables client-side prediction reconciliation.
/// The client sends inputs with incrementing sequence numbers. The server echoes
/// back the last processed sequence in GameStatePayload. The client then knows
/// which of its predicted inputs have been confirmed and which need replaying.
/// </summary>
public sealed class PlayerInputPayload
{
    /// <summary>Monotonically increasing input ID for reconciliation.</summary>
    public required int SequenceNumber { get; init; }
    /// <summary>Horizontal movement (-1 to 1). Normalized on client if diagonal.</summary>
    public float MoveX { get; set; }
    /// <summary>Vertical movement (-1 to 1). Normalized on client if diagonal.</summary>
    public float MoveY { get; set; }
    /// <summary>True if primary fire button is held this tick.</summary>
    public bool PrimaryFire { get; set; }
    /// <summary>True if secondary ability button is held this tick.</summary>
    public bool SecondaryAbility { get; set; }
    /// <summary>True if interact button is held this tick (revive, give items).</summary>
    public bool Interact { get; set; }
    /// <summary>True if the player is requesting to use a med kit this tick.</summary>
    public bool UseMedKit { get; set; }
    /// <summary>Aim direction in radians. Calculated from mouse position on client.</summary>
    public float AimAngle { get; set; }
    /// <summary>Client timestamp for latency measurement and input ordering.</summary>
    public long Timestamp { get; set; }
}

/// <summary>
/// Server → Client: Delta game state update sent every tick (20Hz).
/// 
/// WHY DELTA (dirty entities only): Sending all entities every tick would be wasteful.
/// Only entities whose state changed (position, health, etc.) are included.
/// The client maintains a local entity map and merges updates incrementally.
/// </summary>
public sealed class GameStatePayload
{
    /// <summary>Server tick number. Clients use this for interpolation timing.</summary>
    public required int Tick { get; init; }
    /// <summary>Array of entities that changed since last broadcast.</summary>
    public required EntityState[] Entities { get; init; }
    /// <summary>
    /// The sequence number of the last input processed for this specific player.
    /// Each player receives their own value here for reconciliation.
    /// Null if this player has no entity (e.g., spectating).
    /// </summary>
    public int? LastProcessedInput { get; set; }
}

/// <summary>
/// Snapshot of a single entity's state sent to clients.
/// This is the wire representation — the server's Entity class has additional
/// fields (cooldowns, source IDs) that clients don't need.
/// </summary>
public sealed class EntityState
{
    /// <summary>Unique entity ID (e.g., "player_abc123", "enemy_42", "proj_99").</summary>
    public required string Id { get; init; }
    /// <summary>Type discriminator: "player", "enemy", or "projectile".</summary>
    public required string EntityType { get; init; }
    /// <summary>X position in tile coordinates (float for sub-tile precision).</summary>
    public float X { get; set; }
    /// <summary>Y position in tile coordinates.</summary>
    public float Y { get; set; }
    /// <summary>X velocity (tiles/tick). Used by client for extrapolation between updates.</summary>
    public float VelocityX { get; set; }
    /// <summary>Y velocity (tiles/tick).</summary>
    public float VelocityY { get; set; }
    /// <summary>Current health points.</summary>
    public int Health { get; set; }
    /// <summary>Maximum health points (for rendering health bars as percentages).</summary>
    public int MaxHealth { get; set; }
    /// <summary>Sub-type for rendering: class name for players, enemy variant for enemies.</summary>
    public string? SubType { get; set; }
    /// <summary>Whether the entity is alive. Dead entities may persist briefly for death animations.</summary>
    public bool IsAlive { get; set; } = true;
    /// <summary>Number of med kits the player is carrying (only relevant for player entities).</summary>
    public int MedKits { get; set; }
    /// <summary>Remaining attack cooldown ticks. Clients play attack frames when this jumps up.</summary>
    public int AttackCooldown { get; set; }
}

/// <summary>
/// Chat message payload. Currently only supports pre-defined messages
/// (no free text) to keep communication family-friendly and reduce moderation needs.
/// </summary>
public sealed class ChatMessagePayload
{
    /// <summary>ID of the player who sent the message.</summary>
    public required string SenderId { get; init; }
    /// <summary>Display name of the sender (included so clients don't need to look it up).</summary>
    public required string SenderName { get; init; }
    /// <summary>The message content (one of the pre-defined chat options).</summary>
    public required string Message { get; init; }
    /// <summary>Timestamp for ordering messages in the chat log.</summary>
    public long Timestamp { get; set; }
}

/// <summary>
/// Server → Client: Complete map data sent once when the game starts.
/// 
/// WHY BASE64: The tile array is a raw byte[] (one byte per tile).
/// Base64 encoding in JSON is more compact than a JSON array of numbers
/// and decodes efficiently on the client via atob().
/// </summary>
public sealed class MapDataPayload
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    /// <summary>Map generation seed (for debugging/reproducibility).</summary>
    public required int Seed { get; init; }
    /// <summary>Base64 encoded byte array where each byte is a TileType enum value.</summary>
    public required string TilesBase64 { get; init; }
}

/// <summary>
/// Server → Client: Current session state (lobby info, player list, game state).
/// Broadcast whenever session state changes (player joins/leaves, readies up, game starts/ends).
/// </summary>
public sealed class SessionInfoPayload
{
    public required string SessionId { get; init; }
    /// <summary>Player ID of the current host (first player to connect).</summary>
    public required string HostId { get; init; }
    /// <summary>Current session state: "lobby", "playing", "game_over", "victory".</summary>
    public required string State { get; init; }
    /// <summary>All players currently in the session with their ready/class status.</summary>
    public required PlayerInfo[] Players { get; init; }
    public int MaxPlayers { get; set; } = 8;
    /// <summary>Current wave number (0 if in lobby).</summary>
    public int CurrentWave { get; set; }
    /// <summary>Selected scenario: "warehouse" or "temple".</summary>
    public string Scenario { get; set; } = "warehouse";
}

/// <summary>
/// Player info within a session (used in lobby display and party HUD).
/// </summary>
public sealed class PlayerInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Selected class: "gangster", "detective", "surgeon", or null if not yet chosen.</summary>
    public string? SelectedClass { get; set; }
    public bool IsReady { get; set; }
    /// <summary>True if this player is the session host (can start the game).</summary>
    public bool IsHost { get; set; }
}

/// <summary>
/// Client → Server: Ping for latency measurement.
/// The server echoes the clientTimestamp in the Pong response.
/// Client calculates RTT as (Date.now() - clientTimestamp).
/// </summary>
public sealed class PingPayload
{
    public required long ClientTimestamp { get; init; }
}

/// <summary>
/// Client → Server: Session actions (class selection, ready state, start game).
/// These modify lobby/session state rather than in-game state.
/// </summary>
public sealed class SessionActionPayload
{
    /// <summary>Action type: "select_class", "set_ready", "start_game", "return_to_lobby".</summary>
    public required string Action { get; init; }
    /// <summary>Action value (e.g., class name for select_class, "true"/"false" for set_ready).</summary>
    public string? Value { get; set; }
}

/// <summary>
/// Server → Client: Discrete game events for client-side effects (floating damage numbers,
/// wave announcements, victory/defeat screens). These are ephemeral — not stored in game state.
/// </summary>
public sealed class GameEventPayload
{
    /// <summary>Event type: "damage", "heal", "death", "revive", "wave_start", "game_over", "victory".</summary>
    public required string Event { get; init; }
    /// <summary>Entity that was affected (took damage, died, was revived).</summary>
    public string? TargetId { get; set; }
    /// <summary>Entity that caused the event (attacker, healer).</summary>
    public string? SourceId { get; set; }
    /// <summary>Numeric amount (damage dealt, health restored).</summary>
    public int Amount { get; set; }
    /// <summary>World position for positional effects (floating text, explosions).</summary>
    public float X { get; set; }
    /// <summary>World position Y.</summary>
    public float Y { get; set; }
    /// <summary>Wave number (for wave_start events).</summary>
    public int Wave { get; set; }
    /// <summary>Human-readable message (for HUD display).</summary>
    public string? Message { get; set; }
}

/// <summary>
/// Server → Client: Pong response with both timestamps for RTT calculation.
/// </summary>
public sealed class PongPayload
{
    /// <summary>Echoed from the Ping. Client uses this to calculate RTT.</summary>
    public required long ClientTimestamp { get; init; }
    /// <summary>Server's clock at time of response. Useful for clock sync if needed later.</summary>
    public required long ServerTimestamp { get; init; }
}

/// <summary>
/// Server → Client: Error notification (e.g., invalid action, server full).
/// </summary>
public sealed class ErrorPayload
{
    /// <summary>Machine-readable error code for programmatic handling.</summary>
    public required string Code { get; init; }
    /// <summary>Human-readable error description for display.</summary>
    public required string Message { get; init; }
}
