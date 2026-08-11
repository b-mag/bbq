// =============================================================================
// OverworldMessages.cs — WebSocket Protocol for the Persistent Overworld
// =============================================================================
//
// Defines the message types used between clients and the overworld server.
// Similar pattern to the dungeon Messages.cs (envelope with type discriminator)
// but tailored for overworld-specific interactions: movement in a shared space,
// party management, chat channels, and dungeon entrance triggers.
// =============================================================================

using System.Text.Json.Serialization;

namespace Carcosa.Matchmaking.Overworld;

/// <summary>
/// Universal message envelope for overworld WebSocket communication.
/// </summary>
public sealed class OverworldMessage
{
    public required string Type { get; init; }

    // --- Connection lifecycle ---
    public OwPlayerJoinedPayload? PlayerJoined { get; set; }
    public OwPlayerLeftPayload? PlayerLeft { get; set; }

    // --- Movement ---
    public OwPlayerInputPayload? PlayerInput { get; set; }
    public OwWorldStatePayload? WorldState { get; set; }

    // --- Map data ---
    public OwMapDataPayload? MapData { get; set; }

    // --- Chat ---
    public OwChatMessagePayload? ChatMessage { get; set; }

    // --- Party ---
    public OwPartyInvitePayload? PartyInvite { get; set; }
    public OwPartyResponsePayload? PartyResponse { get; set; }
    public OwPartyUpdatePayload? PartyUpdate { get; set; }

    // --- Dungeon ---
    public OwDungeonPreparePayload? DungeonPrepare { get; set; }
    public OwDungeonConnectPayload? DungeonConnect { get; set; }
    public OwDungeonCompletePayload? DungeonComplete { get; set; }

    // --- Connection health ---
    public OwPingPayload? Ping { get; set; }
    public OwPongPayload? Pong { get; set; }

    // --- Error ---
    public OwErrorPayload? Error { get; set; }
}

/// <summary>Message type constants for the overworld protocol.</summary>
public static class OwMessageTypes
{
    public const string PlayerJoined = "player_joined";
    public const string PlayerLeft = "player_left";
    public const string PlayerInput = "player_input";
    public const string WorldState = "world_state";
    public const string MapData = "map_data";
    public const string ChatMessage = "chat_message";
    public const string PartyInvite = "party_invite";
    public const string PartyResponse = "party_response";
    public const string PartyUpdate = "party_update";
    public const string DungeonPrepare = "dungeon_prepare";
    public const string DungeonConnect = "dungeon_connect";
    public const string DungeonComplete = "dungeon_complete";
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string Error = "error";
}

// =============================================================================
// Payload Types
// =============================================================================

public sealed class OwPlayerJoinedPayload
{
    public required string PlayerId { get; init; }
    public required string PlayerName { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
}

public sealed class OwPlayerLeftPayload
{
    public required string PlayerId { get; init; }
    public string? Reason { get; set; }
}

/// <summary>
/// Client -> Server: Player movement input in the overworld.
/// Simplified compared to dungeon input (no combat, no abilities).
/// </summary>
public sealed class OwPlayerInputPayload
{
    public int SequenceNumber { get; set; }
    public float MoveX { get; set; }
    public float MoveY { get; set; }
    /// <summary>True if interact button pressed (for dungeon entrances, NPCs).</summary>
    public bool Interact { get; set; }
    public long Timestamp { get; set; }
}

/// <summary>
/// Server -> Client: All player positions in the overworld (delta updates).
/// </summary>
public sealed class OwWorldStatePayload
{
    public required int Tick { get; init; }
    public required OwPlayerState[] Players { get; init; }
    public int? LastProcessedInput { get; set; }
}

/// <summary>
/// A single player's state in the overworld.
/// </summary>
public sealed class OwPlayerState
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    /// <summary>Player's current state: "exploring", "in_party", "in_dungeon".</summary>
    public string Status { get; set; } = "exploring";
    /// <summary>Party ID if in a party, null otherwise.</summary>
    public string? PartyId { get; set; }
    /// <summary>Whether this player is the party leader.</summary>
    public bool IsPartyLeader { get; set; }
}

/// <summary>
/// Server -> Client: Full overworld map data (sent on connection).
/// </summary>
public sealed class OwMapDataPayload
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int Seed { get; set; }
    public required string TilesBase64 { get; init; }
    public required OwLandmarkData[] Landmarks { get; init; }
    public required OwDungeonEntranceData[] DungeonEntrances { get; init; }
    public required OwWorldObjectData[] WorldObjects { get; init; }
    public float SpawnX { get; set; }
    public float SpawnY { get; set; }
}

public sealed class OwLandmarkData
{
    public required string Name { get; init; }
    public int X { get; set; }
    public int Y { get; set; }
    public required string Type { get; init; }
}

public sealed class OwDungeonEntranceData
{
    public required string Name { get; init; }
    public int X { get; set; }
    public int Y { get; set; }
    public required string Scenario { get; init; }
}

public sealed class OwWorldObjectData
{
    public required string Type { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
    public bool Collision { get; set; }
    public float CollisionRadius { get; set; }
}

/// <summary>
/// Chat message with channel support (global, nearby, party).
/// </summary>
public sealed class OwChatMessagePayload
{
    /// <summary>Channel: "global", "nearby", "party".</summary>
    public required string Channel { get; init; }
    public required string SenderId { get; init; }
    public required string SenderName { get; init; }
    public required string Text { get; init; }
    public long Timestamp { get; set; }
}

/// <summary>
/// Server -> Client: Party invite notification.
/// </summary>
public sealed class OwPartyInvitePayload
{
    public required string PartyId { get; init; }
    public required string InviterId { get; init; }
    public required string InviterName { get; init; }
}

/// <summary>
/// Client -> Server: Response to a party invite.
/// </summary>
public sealed class OwPartyResponsePayload
{
    public required string PartyId { get; init; }
    public bool Accepted { get; set; }
}

/// <summary>
/// Server -> Client: Party state update (members joined/left, leader changed).
/// </summary>
public sealed class OwPartyUpdatePayload
{
    public required string PartyId { get; init; }
    public required string LeaderId { get; init; }
    public required OwPartyMember[] Members { get; init; }
    /// <summary>Event: "formed", "joined", "left", "disbanded", "leader_changed".</summary>
    public string? Event { get; set; }
}

public sealed class OwPartyMember
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsLeader { get; set; }
}

/// <summary>
/// Server -> Party Leader: Prepare to host a dungeon instance.
/// </summary>
public sealed class OwDungeonPreparePayload
{
    public required int Seed { get; init; }
    public required string Scenario { get; init; }
    public int DungeonWidth { get; set; }
    public int DungeonHeight { get; set; }
    public required string[] PartyMemberIds { get; init; }
}

/// <summary>
/// Server -> Party Members: Connect to the dungeon host.
/// </summary>
public sealed class OwDungeonConnectPayload
{
    public required string HostAddress { get; init; }
    public required int Seed { get; init; }
    public required string Scenario { get; init; }
}

/// <summary>
/// Client -> Server: Notify that the dungeon run is complete.
/// </summary>
public sealed class OwDungeonCompletePayload
{
    public bool Victory { get; set; }
    public int WavesCompleted { get; set; }
}

public sealed class OwPingPayload
{
    public long ClientTimestamp { get; set; }
}

public sealed class OwPongPayload
{
    public long ClientTimestamp { get; set; }
    public long ServerTimestamp { get; set; }
}

public sealed class OwErrorPayload
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}
