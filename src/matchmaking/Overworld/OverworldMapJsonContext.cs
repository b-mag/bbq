// =============================================================================
// OverworldMapJsonContext.cs — AOT-Compatible JSON Serialization for Overworld
// =============================================================================

using System.Text.Json.Serialization;

namespace Carcosa.Matchmaking.Overworld;

[JsonSerializable(typeof(OverworldMap))]
[JsonSerializable(typeof(Landmark))]
[JsonSerializable(typeof(Landmark[]))]
[JsonSerializable(typeof(List<Landmark>))]
[JsonSerializable(typeof(DungeonEntrance))]
[JsonSerializable(typeof(DungeonEntrance[]))]
[JsonSerializable(typeof(List<DungeonEntrance>))]
[JsonSerializable(typeof(WorldObject))]
[JsonSerializable(typeof(WorldObject[]))]
[JsonSerializable(typeof(List<WorldObject>))]
[JsonSerializable(typeof(SpawnPoint))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class OverworldMapJsonContext : JsonSerializerContext { }
