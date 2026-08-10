using System.Text.Json.Serialization;

namespace Carcosa.Server.Network;

// --- Base message envelope ---

/// <summary>
/// All WebSocket messages use this envelope with a discriminator field.
/// The "type" field determines which payload is present.
/// </summary>
public sealed class GameMessage
{
    public required string Type { get; init; }

    // Connection messages
    public PlayerJoinedPayload? PlayerJoined { get; set; }
    public PlayerLeftPayload? PlayerLeft { get; set; }

    // Input messages (client → server)
    public PlayerInputPayload? PlayerInput { get; set; }

    // State messages (server → client)
    public GameStatePayload? GameState { get; set; }
    public MapDataPayload? MapData { get; set; }
    public GameEventPayload? GameEvent { get; set; }

    // Chat messages (bidirectional)
    public ChatMessagePayload? Chat { get; set; }

    // Session messages
    public SessionInfoPayload? SessionInfo { get; set; }
    public SessionActionPayload? SessionAction { get; set; }

    // Ping/Pong for connection health
    public PingPayload? Ping { get; set; }
    public PongPayload? Pong { get; set; }

    // Error messages (server → client)
    public ErrorPayload? Error { get; set; }
}

// --- Message types as constants ---

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

// --- Payload types ---

public sealed class PlayerJoinedPayload
{
    public required string PlayerId { get; init; }
    public required string PlayerName { get; init; }
    public string? SelectedClass { get; set; }
}

public sealed class PlayerLeftPayload
{
    public required string PlayerId { get; init; }
    public string? Reason { get; set; }
}

public sealed class PlayerInputPayload
{
    public required int SequenceNumber { get; init; }
    public float MoveX { get; set; }
    public float MoveY { get; set; }
    public bool PrimaryFire { get; set; }
    public bool SecondaryAbility { get; set; }
    public bool Interact { get; set; }
    public float AimAngle { get; set; }
    public long Timestamp { get; set; }
}

public sealed class GameStatePayload
{
    public required int Tick { get; init; }
    public required EntityState[] Entities { get; init; }
    public int? LastProcessedInput { get; set; }
}

public sealed class EntityState
{
    public required string Id { get; init; }
    public required string EntityType { get; init; } // "player", "enemy", "projectile"
    public float X { get; set; }
    public float Y { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public string? SubType { get; set; } // class name or enemy type
    public bool IsAlive { get; set; } = true;
}

public sealed class ChatMessagePayload
{
    public required string SenderId { get; init; }
    public required string SenderName { get; init; }
    public required string Message { get; init; }
    public long Timestamp { get; set; }
}

public sealed class MapDataPayload
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Seed { get; init; }
    public required string TilesBase64 { get; init; } // Base64 encoded byte array of tile data
}

public sealed class SessionInfoPayload
{
    public required string SessionId { get; init; }
    public required string HostId { get; init; }
    public required string State { get; init; } // "lobby", "playing", "game_over"
    public required PlayerInfo[] Players { get; init; }
    public int MaxPlayers { get; set; } = 8;
    public int CurrentWave { get; set; }
}

public sealed class PlayerInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? SelectedClass { get; set; }
    public bool IsReady { get; set; }
    public bool IsHost { get; set; }
}

public sealed class PingPayload
{
    public required long ClientTimestamp { get; init; }
}

public sealed class SessionActionPayload
{
    public required string Action { get; init; } // "select_class", "set_ready", "start_game", "return_to_lobby"
    public string? Value { get; set; }
}

public sealed class GameEventPayload
{
    public required string Event { get; init; } // "damage", "heal", "death", "revive", "wave_start", "game_over", "victory"
    public string? TargetId { get; set; }
    public string? SourceId { get; set; }
    public int Amount { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public int Wave { get; set; }
    public string? Message { get; set; }
}

public sealed class PongPayload
{
    public required long ClientTimestamp { get; init; }
    public required long ServerTimestamp { get; init; }
}

public sealed class ErrorPayload
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}
