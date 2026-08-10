namespace Carcosa.Server.Game;

/// <summary>
/// Tile types in the game map.
/// </summary>
public enum TileType : byte
{
    Floor = 0,       // Interior floor of buildings
    Wall = 1,        // Impassable walls
    Door = 2,        // Walkable door connecting building to street
    Water = 3,       // Impassable shoreline water
    Cobblestone = 4, // Street/outdoor walkable surface
    Sand = 5,        // Beach area near water (walkable)
}

/// <summary>
/// BSP-based procedural map generator that creates a 1920s coastal village layout.
/// Generates rooms (buildings), corridors (streets), walls, doors, and shoreline.
/// Uses seeded random for reproducibility.
/// </summary>
public static class MapGenerator
{
    private const int MinRoomSize = 5;
    private const int MaxRoomSize = 12;
    private const int MinPartitionSize = 8;
    private const int StreetWidth = 2;
    private const int ShorelineDepth = 4;
    private const int SandDepth = 2;

    /// <summary>
    /// Generate a complete map with the given dimensions and seed.
    /// </summary>
    public static TileMap Generate(int width, int height, int seed)
    {
        var rng = new Random(seed);
        var tiles = new byte[width * height];

        // Start with all walls
        Array.Fill(tiles, (byte)TileType.Wall);

        // Create the BSP tree
        var root = new BspNode(1, 1, width - 2, height - ShorelineDepth - 2);
        SplitNode(root, rng);

        // Create rooms in leaf nodes
        var rooms = new List<Room>();
        CreateRooms(root, rng, rooms);

        // Carve rooms into the map
        foreach (var room in rooms)
        {
            CarveRoom(tiles, width, room);
        }

        // Connect rooms with corridors (streets)
        ConnectRooms(root, tiles, width, rng);

        // Add the shoreline at the bottom of the map
        AddShoreline(tiles, width, height, rng);

        // Add some additional street connections for a more village-like feel
        AddExtraStreets(tiles, width, height, rooms, rng);

        // Identify spawn points (open areas for enemy wave spawns)
        var spawnPoints = IdentifySpawnPoints(tiles, width, height, rooms, rng);

        return new TileMap
        {
            Width = width,
            Height = height,
            Tiles = tiles,
            Seed = seed,
            Rooms = rooms.ToArray(),
            SpawnPoints = spawnPoints
        };
    }

    private static void SplitNode(BspNode node, Random rng)
    {
        // Don't split if too small
        if (node.Width < MinPartitionSize * 2 && node.Height < MinPartitionSize * 2)
            return;

        // Choose split direction based on aspect ratio
        bool splitHorizontally;
        if (node.Width > node.Height * 1.25f)
            splitHorizontally = false; // Split vertically (make it less wide)
        else if (node.Height > node.Width * 1.25f)
            splitHorizontally = true; // Split horizontally (make it less tall)
        else
            splitHorizontally = rng.Next(2) == 0;

        if (splitHorizontally)
        {
            if (node.Height < MinPartitionSize * 2) return;
            var split = rng.Next(MinPartitionSize, node.Height - MinPartitionSize);
            node.Left = new BspNode(node.X, node.Y, node.Width, split);
            node.Right = new BspNode(node.X, node.Y + split, node.Width, node.Height - split);
        }
        else
        {
            if (node.Width < MinPartitionSize * 2) return;
            var split = rng.Next(MinPartitionSize, node.Width - MinPartitionSize);
            node.Left = new BspNode(node.X, node.Y, split, node.Height);
            node.Right = new BspNode(node.X + split, node.Y, node.Width - split, node.Height);
        }

        SplitNode(node.Left, rng);
        SplitNode(node.Right, rng);
    }

    private static void CreateRooms(BspNode node, Random rng, List<Room> rooms)
    {
        if (node.Left == null && node.Right == null)
        {
            // Leaf node — create a room inside this partition
            var roomWidth = rng.Next(MinRoomSize, Math.Min(MaxRoomSize, node.Width - 2));
            var roomHeight = rng.Next(MinRoomSize, Math.Min(MaxRoomSize, node.Height - 2));

            var roomX = node.X + rng.Next(1, node.Width - roomWidth - 1);
            var roomY = node.Y + rng.Next(1, node.Height - roomHeight - 1);

            var room = new Room(roomX, roomY, roomWidth, roomHeight);
            node.Room = room;
            rooms.Add(room);
        }
        else
        {
            if (node.Left != null) CreateRooms(node.Left, rng, rooms);
            if (node.Right != null) CreateRooms(node.Right, rng, rooms);

            // Set this node's room to be the first child room (for corridor connection)
            node.Room = node.Left?.Room ?? node.Right?.Room;
        }
    }

    private static void CarveRoom(byte[] tiles, int mapWidth, Room room)
    {
        // Carve interior floor
        for (int y = room.Y + 1; y < room.Y + room.Height - 1; y++)
        {
            for (int x = room.X + 1; x < room.X + room.Width - 1; x++)
            {
                tiles[y * mapWidth + x] = (byte)TileType.Floor;
            }
        }

        // The perimeter stays as Wall (already set)
    }

    private static void ConnectRooms(BspNode node, byte[] tiles, int mapWidth, Random rng)
    {
        if (node.Left == null || node.Right == null) return;

        ConnectRooms(node.Left, tiles, mapWidth, rng);
        ConnectRooms(node.Right, tiles, mapWidth, rng);

        // Connect the two child nodes with a corridor
        var roomA = GetRoom(node.Left);
        var roomB = GetRoom(node.Right);
        if (roomA == null || roomB == null) return;

        var centerA = roomA.Center;
        var centerB = roomB.Center;

        // Create an L-shaped corridor
        if (rng.Next(2) == 0)
        {
            CarveCorridorH(tiles, mapWidth, centerA.X, centerB.X, centerA.Y);
            CarveCorridorV(tiles, mapWidth, centerA.Y, centerB.Y, centerB.X);
        }
        else
        {
            CarveCorridorV(tiles, mapWidth, centerA.Y, centerB.Y, centerA.X);
            CarveCorridorH(tiles, mapWidth, centerA.X, centerB.X, centerB.Y);
        }

        // Add doors where corridors meet room walls
        AddDoors(tiles, mapWidth, roomA, rng);
        AddDoors(tiles, mapWidth, roomB, rng);
    }

    private static Room? GetRoom(BspNode node)
    {
        if (node.Room != null) return node.Room;
        if (node.Left != null)
        {
            var r = GetRoom(node.Left);
            if (r != null) return r;
        }
        if (node.Right != null)
        {
            return GetRoom(node.Right);
        }
        return null;
    }

    private static void CarveCorridorH(byte[] tiles, int mapWidth, int x1, int x2, int y)
    {
        var minX = Math.Min(x1, x2);
        var maxX = Math.Max(x1, x2);
        for (int x = minX; x <= maxX; x++)
        {
            for (int dy = 0; dy < StreetWidth; dy++)
            {
                var idx = (y + dy) * mapWidth + x;
                if (idx >= 0 && idx < tiles.Length)
                {
                    if (tiles[idx] == (byte)TileType.Wall)
                        tiles[idx] = (byte)TileType.Cobblestone;
                }
            }
        }
    }

    private static void CarveCorridorV(byte[] tiles, int mapWidth, int y1, int y2, int x)
    {
        var minY = Math.Min(y1, y2);
        var maxY = Math.Max(y1, y2);
        for (int y = minY; y <= maxY; y++)
        {
            for (int dx = 0; dx < StreetWidth; dx++)
            {
                var idx = y * mapWidth + (x + dx);
                if (idx >= 0 && idx < tiles.Length)
                {
                    if (tiles[idx] == (byte)TileType.Wall)
                        tiles[idx] = (byte)TileType.Cobblestone;
                }
            }
        }
    }

    private static void AddDoors(byte[] tiles, int mapWidth, Room room, Random rng)
    {
        // Check each wall segment of the room for adjacent corridor tiles
        // Top wall
        for (int x = room.X + 1; x < room.X + room.Width - 1; x++)
        {
            var wallIdx = room.Y * mapWidth + x;
            var outsideIdx = (room.Y - 1) * mapWidth + x;
            if (outsideIdx >= 0 && tiles[outsideIdx] == (byte)TileType.Cobblestone
                && tiles[wallIdx] == (byte)TileType.Wall)
            {
                tiles[wallIdx] = (byte)TileType.Door;
            }
        }
        // Bottom wall
        for (int x = room.X + 1; x < room.X + room.Width - 1; x++)
        {
            var wallIdx = (room.Y + room.Height - 1) * mapWidth + x;
            var outsideIdx = (room.Y + room.Height) * mapWidth + x;
            if (outsideIdx < tiles.Length && tiles[outsideIdx] == (byte)TileType.Cobblestone
                && tiles[wallIdx] == (byte)TileType.Wall)
            {
                tiles[wallIdx] = (byte)TileType.Door;
            }
        }
        // Left wall
        for (int y = room.Y + 1; y < room.Y + room.Height - 1; y++)
        {
            var wallIdx = y * mapWidth + room.X;
            var outsideIdx = y * mapWidth + (room.X - 1);
            if (outsideIdx >= 0 && tiles[outsideIdx] == (byte)TileType.Cobblestone
                && tiles[wallIdx] == (byte)TileType.Wall)
            {
                tiles[wallIdx] = (byte)TileType.Door;
            }
        }
        // Right wall
        for (int y = room.Y + 1; y < room.Y + room.Height - 1; y++)
        {
            var wallIdx = y * mapWidth + (room.X + room.Width - 1);
            var outsideIdx = y * mapWidth + (room.X + room.Width);
            if (outsideIdx < tiles.Length && tiles[outsideIdx] == (byte)TileType.Cobblestone
                && tiles[wallIdx] == (byte)TileType.Wall)
            {
                tiles[wallIdx] = (byte)TileType.Door;
            }
        }
    }

    private static void AddShoreline(byte[] tiles, int mapWidth, int mapHeight, Random rng)
    {
        // The bottom portion of the map is shoreline/water
        var waterStart = mapHeight - ShorelineDepth;
        var sandStart = waterStart - SandDepth;

        for (int y = sandStart; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                if (y >= waterStart)
                {
                    // Irregular water edge
                    var waveOffset = (int)(Math.Sin(x * 0.3 + rng.NextDouble() * 0.5) * 1.5);
                    if (y >= waterStart + 1 + waveOffset)
                    {
                        tiles[y * mapWidth + x] = (byte)TileType.Water;
                    }
                    else
                    {
                        tiles[y * mapWidth + x] = (byte)TileType.Sand;
                    }
                }
                else
                {
                    // Sand area between buildings and water
                    if (tiles[y * mapWidth + x] == (byte)TileType.Wall)
                    {
                        tiles[y * mapWidth + x] = (byte)TileType.Sand;
                    }
                }
            }
        }
    }

    private static void AddExtraStreets(byte[] tiles, int mapWidth, int mapHeight, List<Room> rooms, Random rng)
    {
        // Add some extra connecting streets to make the village feel more connected
        var numExtraStreets = rooms.Count / 3;
        for (int i = 0; i < numExtraStreets; i++)
        {
            var roomA = rooms[rng.Next(rooms.Count)];
            var roomB = rooms[rng.Next(rooms.Count)];
            if (roomA == roomB) continue;

            var startX = roomA.Center.X;
            var startY = roomA.Center.Y;
            var endX = roomB.Center.X;
            var endY = roomB.Center.Y;

            // Only add if rooms aren't already very close
            var dist = Math.Abs(startX - endX) + Math.Abs(startY - endY);
            if (dist < 15) continue;

            CarveCorridorH(tiles, mapWidth, startX, endX, startY);
            CarveCorridorV(tiles, mapWidth, startY, endY, endX);

            AddDoors(tiles, mapWidth, roomA, rng);
            AddDoors(tiles, mapWidth, roomB, rng);
        }
    }

    private static SpawnPoint[] IdentifySpawnPoints(byte[] tiles, int mapWidth, int mapHeight, List<Room> rooms, Random rng)
    {
        var spawnPoints = new List<SpawnPoint>();

        // Use room centers as potential spawn points (for enemies to spawn inside buildings)
        foreach (var room in rooms)
        {
            if (room.Width >= 6 && room.Height >= 6)
            {
                spawnPoints.Add(new SpawnPoint(room.Center.X, room.Center.Y, SpawnPointType.Room));
            }
        }

        // Add some outdoor spawn points on cobblestone streets
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var x = rng.Next(5, mapWidth - 5);
            var y = rng.Next(5, mapHeight - ShorelineDepth - 5);
            if (tiles[y * mapWidth + x] == (byte)TileType.Cobblestone)
            {
                // Verify it's a decent open area
                var openCount = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (tiles[(y + dy) * mapWidth + (x + dx)] != (byte)TileType.Wall)
                            openCount++;

                if (openCount >= 7)
                {
                    spawnPoints.Add(new SpawnPoint(x, y, SpawnPointType.Street));
                }
            }
        }

        return spawnPoints.ToArray();
    }
}

// --- Supporting types ---

/// <summary>
/// BSP tree node for map partitioning.
/// </summary>
internal sealed class BspNode
{
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public BspNode? Left { get; set; }
    public BspNode? Right { get; set; }
    public Room? Room { get; set; }

    public BspNode(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}

/// <summary>
/// A room (building) in the village.
/// </summary>
public sealed class Room
{
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public (int X, int Y) Center => (X + Width / 2, Y + Height / 2);

    public Room(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}

/// <summary>
/// A designated spawn point for enemies or players.
/// </summary>
public sealed class SpawnPoint
{
    public int X { get; }
    public int Y { get; }
    public SpawnPointType Type { get; }

    public SpawnPoint(int x, int y, SpawnPointType type)
    {
        X = x;
        Y = y;
        Type = type;
    }
}

public enum SpawnPointType
{
    Room,
    Street
}
