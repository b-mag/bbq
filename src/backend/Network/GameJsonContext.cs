using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carcosa.Server.Network;

/// <summary>
/// AOT-compatible source-generated JSON serialization context for all game message types.
/// This eliminates runtime reflection for JSON serialization.
/// </summary>
[JsonSerializable(typeof(GameMessage))]
[JsonSerializable(typeof(PlayerJoinedPayload))]
[JsonSerializable(typeof(PlayerLeftPayload))]
[JsonSerializable(typeof(PlayerInputPayload))]
[JsonSerializable(typeof(GameStatePayload))]
[JsonSerializable(typeof(EntityState))]
[JsonSerializable(typeof(EntityState[]))]
[JsonSerializable(typeof(ChatMessagePayload))]
[JsonSerializable(typeof(MapDataPayload))]
[JsonSerializable(typeof(SessionInfoPayload))]
[JsonSerializable(typeof(SessionActionPayload))]
[JsonSerializable(typeof(GameEventPayload))]
[JsonSerializable(typeof(PlayerInfo))]
[JsonSerializable(typeof(PlayerInfo[]))]
[JsonSerializable(typeof(PingPayload))]
[JsonSerializable(typeof(PongPayload))]
[JsonSerializable(typeof(ErrorPayload))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
internal partial class GameJsonContext : JsonSerializerContext
{
}
