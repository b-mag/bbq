// =============================================================================
// OverworldTileType.cs — Tile Types for the Persistent Overworld Map
// =============================================================================
//
// These tile types define the overworld's terrain. They are distinct from the
// dungeon tile types (in Game/MapGenerator.cs) because the overworld has different
// biomes: forests, mountains, lakes, ruins, etc. The dungeon tile types remain
// unchanged for instanced dungeon generation.
//
// Values are stored as bytes in the tile array (base64-encoded in JSON).
// The frontend must have a matching enum to render correct colors/sprites.
// =============================================================================

namespace Carcosa.Matchmaking.Overworld;

/// <summary>
/// Tile types for the persistent overworld map.
/// Values are stored as bytes in the base64-encoded tile array.
/// </summary>
public enum OverworldTileType : byte
{
    /// <summary>Open grassy terrain (walkable). Default outdoor tile.</summary>
    Grass = 0,
    /// <summary>Deep water — impassable. Lake Hali, ocean.</summary>
    DeepWater = 1,
    /// <summary>Shallow water/marsh — walkable but slow. Lake edges.</summary>
    ShallowWater = 2,
    /// <summary>Dense forest — impassable. Must use paths through forest.</summary>
    Forest = 3,
    /// <summary>Mountain/cliff — impassable. Northern barrier.</summary>
    Mountain = 4,
    /// <summary>Ancient ruined structures — impassable walls.</summary>
    Ruins = 5,
    /// <summary>Dirt/stone path — walkable. Connects regions.</summary>
    Path = 6,
    /// <summary>Sandy beach — walkable. Southern coast, lake shores.</summary>
    Sand = 7,
    /// <summary>Bridge over water — walkable. Crosses rivers/lake narrows.</summary>
    Bridge = 8,
    /// <summary>Dungeon entrance marker — walkable. Interaction trigger zone.</summary>
    DungeonEntrance = 9,
    /// <summary>Cobblestone (village streets) — walkable.</summary>
    Cobblestone = 10,
    /// <summary>Building wall — impassable. Village structures.</summary>
    Wall = 11,
    /// <summary>Building floor/interior — walkable. Inside village buildings.</summary>
    Floor = 12,
    /// <summary>Door — walkable. Building entrance.</summary>
    Door = 13,
    /// <summary>Dark grass — walkable. Transition near forests/ruins.</summary>
    DarkGrass = 14,
    /// <summary>Mist — walkable but obscured. Near Lake Hali.</summary>
    Mist = 15,
}
