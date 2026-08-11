// =============================================================================
// GameJsonContext.cs — AOT-Compatible JSON Serialization for Game Messages
// =============================================================================
//
// WHY THIS EXISTS:
// Native AOT compilation eliminates the .NET runtime's ability to perform
// reflection at runtime. Standard System.Text.Json relies on reflection to
// discover properties, construct serializers, and handle polymorphism.
//
// The [JsonSerializable] attributes trigger the Roslyn source generator
// (System.Text.Json.SourceGeneration) to emit highly-optimized, zero-reflection
// serialization code at compile time. Each type listed here gets a dedicated
// serializer baked directly into the binary.
//
// WHY EVERY TYPE IS LISTED:
// The source generator only emits code for types explicitly declared here.
// If a type is missing, serialization will silently produce null/empty JSON
// or throw at runtime. When adding new message payloads, they MUST be added
// to this context — this is the most common AOT-related mistake.
//
// WHY CamelCase:
// The frontend (TypeScript) uses camelCase property names. Rather than decorating
// every C# property with [JsonPropertyName], we set the naming policy globally
// on the context. This keeps the C# code using PascalCase (idiomatic) while the
// wire format uses camelCase (idiomatic for JSON/JS).
//
// WHY WhenWritingNull:
// Game messages use a discriminated union pattern where only one payload field
// is populated per message. Omitting null fields significantly reduces message
// size (important at 20 messages/sec × N players).
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carcosa.Server.Network;

/// <summary>
/// Source-generated JSON serialization context for all WebSocket game message types.
/// This is the ONLY serializer used for game communication — it produces zero-allocation,
/// AOT-compatible serialization code at compile time via Roslyn source generation.
/// 
/// IMPORTANT FOR AOT: Any new message payload type MUST be registered here with a
/// [JsonSerializable] attribute, otherwise it will not serialize correctly in
/// the published AOT binary (even if it works in debug/JIT mode).
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
