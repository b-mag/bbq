// =============================================================================
// PeerJsonContext.cs — AOT-Compatible JSON Serialization for P2P Messages
// =============================================================================
//
// WHY SOURCE-GENERATED:
// .NET Native AOT cannot use reflection-based JSON serialization. This context
// pre-generates all serializers at compile time. Every type exchanged between
// peers must be registered here.
//
// ADDING NEW MESSAGE TYPES:
// When adding a new payload type:
//   1. Define the class in PeerMessagePayloads.cs
//   2. Add a nullable property to PeerMessage
//   3. Add the type to this context (JsonSerializable attribute)
//   4. Add a constant to PeerMessageTypes
// =============================================================================

using System.Text.Json.Serialization;

namespace Carcosa.Server.P2P;

[JsonSerializable(typeof(PeerMessage))]
[JsonSerializable(typeof(PeerHandshakePayload))]
[JsonSerializable(typeof(PeerHandshakeResponsePayload))]
[JsonSerializable(typeof(PeerStateUpdatePayload))]
[JsonSerializable(typeof(PeerExchangePayload))]
[JsonSerializable(typeof(PeerEndpoint))]
[JsonSerializable(typeof(PeerEndpoint[]))]
[JsonSerializable(typeof(PeerChatRelayPayload))]
[JsonSerializable(typeof(PeerPartyUpdatePayload))]
[JsonSerializable(typeof(PeerAdminBroadcastPayload))]
[JsonSerializable(typeof(PeerViolationPayload))]
[JsonSerializable(typeof(PeerVoteKickPayload))]
[JsonSerializable(typeof(PeerKeepalivePayload))]
[JsonSerializable(typeof(PeerKeepaliveAckPayload))]
[JsonSerializable(typeof(PeerCombatActionPayload))]
[JsonSerializable(typeof(PeerEnemySyncPayload))]
[JsonSerializable(typeof(PeerEnemySyncEntry))]
[JsonSerializable(typeof(PeerEnemySyncEntry[]))]
[JsonSerializable(typeof(PeerProjectileSyncEntry))]
[JsonSerializable(typeof(PeerProjectileSyncEntry[]))]
[JsonSerializable(typeof(PeerDamageEventPayload))]
[JsonSerializable(typeof(PeerEliteDefeatedPayload))]
[JsonSerializable(typeof(PeerLootDropSyncPayload))]
[JsonSerializable(typeof(PeerLootDropEntry))]
[JsonSerializable(typeof(PeerLootDropEntry[]))]
[JsonSerializable(typeof(PeerLootPickupPayload))]
[JsonSerializable(typeof(PeerLootFairGamePayload))]
[JsonSerializable(typeof(PeerMetricsUpdatePayload))]
[JsonSerializable(typeof(string[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class PeerJsonContext : JsonSerializerContext { }
