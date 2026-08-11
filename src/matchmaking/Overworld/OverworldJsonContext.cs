// =============================================================================
// OverworldJsonContext.cs — AOT-Compatible JSON for Overworld WebSocket Protocol
// =============================================================================

using System.Text.Json.Serialization;

namespace Carcosa.Matchmaking.Overworld;

[JsonSerializable(typeof(OverworldMessage))]
[JsonSerializable(typeof(OwPlayerJoinedPayload))]
[JsonSerializable(typeof(OwPlayerLeftPayload))]
[JsonSerializable(typeof(OwPlayerInputPayload))]
[JsonSerializable(typeof(OwWorldStatePayload))]
[JsonSerializable(typeof(OwPlayerState))]
[JsonSerializable(typeof(OwPlayerState[]))]
[JsonSerializable(typeof(OwMapDataPayload))]
[JsonSerializable(typeof(OwLandmarkData))]
[JsonSerializable(typeof(OwLandmarkData[]))]
[JsonSerializable(typeof(OwDungeonEntranceData))]
[JsonSerializable(typeof(OwDungeonEntranceData[]))]
[JsonSerializable(typeof(OwWorldObjectData))]
[JsonSerializable(typeof(OwWorldObjectData[]))]
[JsonSerializable(typeof(OwChatMessagePayload))]
[JsonSerializable(typeof(OwPartyInvitePayload))]
[JsonSerializable(typeof(OwPartyResponsePayload))]
[JsonSerializable(typeof(OwPartyUpdatePayload))]
[JsonSerializable(typeof(OwPartyMember))]
[JsonSerializable(typeof(OwPartyMember[]))]
[JsonSerializable(typeof(OwDungeonPreparePayload))]
[JsonSerializable(typeof(OwDungeonConnectPayload))]
[JsonSerializable(typeof(OwDungeonCompletePayload))]
[JsonSerializable(typeof(OwPingPayload))]
[JsonSerializable(typeof(OwPongPayload))]
[JsonSerializable(typeof(OwErrorPayload))]
[JsonSerializable(typeof(string[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class OverworldJsonContext : JsonSerializerContext { }
