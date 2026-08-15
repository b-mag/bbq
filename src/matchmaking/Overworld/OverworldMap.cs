// =============================================================================
// OverworldMap.cs — Persistent Overworld Map Data Model
// =============================================================================
//
// The overworld map is a 200x200 tile grid stored as a JSON file.
// It contains:
//   - Tile data (base64-encoded byte array)
//   - Named landmarks (for lore/navigation)
//   - Dungeon entrances (interactable portals to instanced content)
//   - World objects (trees, houses, pillars — see Task 8)
//   - Spawn point (where new players appear)
//
// The map is generated once by OverworldGenerator on first server boot,
// then persisted to disk. Subsequent boots load from disk.
// Editable: modify the JSON to move landmarks, add entrances, etc.
// =============================================================================

using System.Text.Json.Serialization;

namespace Carcosa.Matchmaking.Overworld;

/// <summary>
/// The complete overworld map data structure. Serialized to/from JSON.
/// </summary>
public sealed class OverworldMap
{
    /// <summary>Map width in tiles.</summary>
    public int Width { get; set; }
    /// <summary>Map height in tiles.</summary>
    public int Height { get; set; }
    /// <summary>Base64-encoded byte array of tile data (row-major, Width*Height bytes).</summary>
    public string TilesBase64 { get; set; } = "";
    /// <summary>Seed used to generate this map (for reproducibility tracking).</summary>
    public int Seed { get; set; }
    /// <summary>Named landmarks for navigation and lore.</summary>
    public List<Landmark> Landmarks { get; set; } = new();
    /// <summary>Dungeon entrance points where parties can enter instanced content.</summary>
    public List<DungeonEntrance> DungeonEntrances { get; set; } = new();
    /// <summary>Static world objects (trees, buildings, pillars, etc.).</summary>
    public List<WorldObject> WorldObjects { get; set; } = new();
    /// <summary>Default spawn point for new players.</summary>
    public SpawnPoint SpawnPoint { get; set; } = new() { X = 100, Y = 180 };

    /// <summary>
    /// Decode the base64 tile data into a byte array.
    /// </summary>
    public byte[] DecodeTiles() => Convert.FromBase64String(TilesBase64);

    /// <summary>
    /// Encode a byte array into the base64 tile data.
    /// </summary>
    public void EncodeTiles(byte[] tiles) => TilesBase64 = Convert.ToBase64String(tiles);

    /// <summary>
    /// Get the tile type at a given coordinate.
    /// </summary>
    public OverworldTileType GetTile(byte[] tiles, int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return OverworldTileType.DeepWater; // Out of bounds = impassable
        return (OverworldTileType)tiles[y * Width + x];
    }

    /// <summary>
    /// Check if a tile coordinate is walkable.
    /// </summary>
    public static bool IsWalkable(OverworldTileType tile)
    {
        return tile switch
        {
            OverworldTileType.Grass => true,
            OverworldTileType.ShallowWater => true,
            OverworldTileType.Path => true,
            OverworldTileType.Sand => true,
            OverworldTileType.Bridge => true,
            OverworldTileType.DungeonEntrance => true,
            OverworldTileType.Cobblestone => true,
            OverworldTileType.Floor => true,
            OverworldTileType.Door => true,
            OverworldTileType.DarkGrass => true,
            OverworldTileType.Mist => true,
            OverworldTileType.Desert => true,
            OverworldTileType.Swamp => true,
            OverworldTileType.MountainPath => true,
            OverworldTileType.Snow => true,
            OverworldTileType.Ash => true,
            OverworldTileType.Palace => true,
            OverworldTileType.Flesh => true,
            OverworldTileType.Ladder => true,
            _ => false,
        };
    }
}

/// <summary>
/// A named landmark on the overworld for navigation and lore.
/// </summary>
public sealed class Landmark
{
    /// <summary>Display name (e.g., "Lake Hali", "The King's Palace").</summary>
    public string Name { get; set; } = "";
    /// <summary>Tile X coordinate.</summary>
    public int X { get; set; }
    /// <summary>Tile Y coordinate.</summary>
    public int Y { get; set; }
    /// <summary>Landmark type for rendering/icon selection.</summary>
    public string Type { get; set; } = "generic";
}

/// <summary>
/// A dungeon entrance on the overworld. When a party interacts here,
/// they enter an instanced dungeon.
/// </summary>
public sealed class DungeonEntrance
{
    /// <summary>Display name (e.g., "The Drowned Dock", "Temple of Hali").</summary>
    public string Name { get; set; } = "";
    /// <summary>Tile X coordinate of the entrance.</summary>
    public int X { get; set; }
    /// <summary>Tile Y coordinate of the entrance.</summary>
    public int Y { get; set; }
    /// <summary>Scenario type to generate (warehouse, temple, cave, etc.).</summary>
    public string Scenario { get; set; } = "warehouse";
    /// <summary>Dungeon map width (tiles).</summary>
    public int DungeonWidth { get; set; } = 80;
    /// <summary>Dungeon map height (tiles).</summary>
    public int DungeonHeight { get; set; } = 60;
}

/// <summary>
/// A static world object placed on the overworld (tree, house, pillar, etc.).
/// </summary>
public sealed class WorldObject
{
    /// <summary>Object type identifier (matches sprite manifest key).</summary>
    public string Type { get; set; } = "";
    /// <summary>World X position (tile coordinates, can be fractional).</summary>
    public float X { get; set; }
    /// <summary>World Y position (tile coordinates, can be fractional).</summary>
    public float Y { get; set; }
    /// <summary>Whether this object blocks movement.</summary>
    public bool Collision { get; set; }
    /// <summary>Collision radius in tiles (if Collision is true).</summary>
    public float CollisionRadius { get; set; } = 0.4f;
}

/// <summary>
/// A spawn point coordinate.
/// </summary>
public sealed class SpawnPoint
{
    public int X { get; set; }
    public int Y { get; set; }
}
