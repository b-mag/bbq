// =============================================================================
// MapGenerator.cs — BSP Procedural Map Generation
// =============================================================================
//
// WHY BSP (Binary Space Partitioning):
// BSP recursively divides the map into regions, places rooms inside leaf regions,
// then connects adjacent rooms with corridors. This guarantees:
//   - All rooms are reachable (connected by construction)
//   - No overlapping rooms (each room is in its own partition)
//   - Natural layout variety (random split positions + room sizes)
//   - Good gameplay flow (corridors create chokepoints, rooms create arenas)
//
// WHY SEEDED RANDOM:
// Using a seed makes maps reproducible. All players receive the seed and can
// verify the map matches. Useful for debugging ("bug happens on seed 12345")
// and potentially for competitive scenarios (same map layout for fairness).
//
// MAP THEME:
// The current generator creates a 1920s coastal village:
//   - Rooms = buildings (interior Floor tiles with Wall perimeters)
//   - Corridors = streets (Cobblestone tiles, 2 tiles wide)
//   - Doors = connections between buildings and streets
//   - Bottom edge = shoreline (Sand → Water gradient)
// =============================================================================

namespace Carcosa.Server.Game;

/// <summary>
/// Tile types in the game map. Stored as bytes in the TileMap.Tiles array.
/// Values must match the frontend's TileType enum (in lib/map.ts).
/// </summary>
public enum TileType : byte
{
    /// <summary>Interior floor of buildings (walkable).</summary>
    Floor = 0,
    /// <summary>Impassable walls (building perimeters and map edges).</summary>
    Wall = 1,
    /// <summary>Walkable door connecting building interior to street.</summary>
    Door = 2,
    /// <summary>Impassable shoreline water at map bottom.</summary>
    Water = 3,
    /// <summary>Street/outdoor walkable surface between buildings.</summary>
    Cobblestone = 4,
    /// <summary>Beach area near water (walkable).</summary>
    Sand = 5,
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

    /// <summary>
    /// Generate a Temple-style map: large open arena with scattered pillars.
    /// Designed for Vampire Survivors-style endless survival gameplay.
    /// Map is 100x100 tiles — much larger than Warehouse — mostly open floor
    /// with stone pillars for partial cover and arena edges.
    /// </summary>
    public static TileMap GenerateTemple(int width, int height, int seed)
    {
        var rng = new Random(seed);
        var tiles = new byte[width * height];

        // Fill with walls (border)
        Array.Fill(tiles, (byte)TileType.Wall);

        // Carve out a large central arena (90% of map is open floor)
        var margin = 3;
        for (int y = margin; y < height - margin; y++)
        {
            for (int x = margin; x < width - margin; x++)
            {
                tiles[y * width + x] = (byte)TileType.Floor;
            }
        }

        // Add scattered stone pillars (2x2 walls) for partial cover
        var pillarCount = (width * height) / 200; // ~50 pillars on a 100x100 map
        var rooms = new List<Room>();
        for (int i = 0; i < pillarCount; i++)
        {
            var px = rng.Next(margin + 3, width - margin - 4);
            var py = rng.Next(margin + 3, height - margin - 4);

            // 2x2 pillar
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                    tiles[(py + dy) * width + (px + dx)] = (byte)TileType.Wall;
        }

        // Add some cobblestone paths crossing the arena for visual variety
        for (int i = 0; i < 4; i++)
        {
            var isHorizontal = rng.Next(2) == 0;
            var pos = rng.Next(margin + 5, (isHorizontal ? height : width) - margin - 5);

            for (int j = margin; j < (isHorizontal ? width : height) - margin; j++)
            {
                var idx = isHorizontal ? pos * width + j : j * width + pos;
                if (tiles[idx] == (byte)TileType.Floor)
                    tiles[idx] = (byte)TileType.Cobblestone;
                // Also the adjacent tile for 2-wide paths
                var idx2 = isHorizontal ? (pos + 1) * width + j : j * width + (pos + 1);
                if (idx2 < tiles.Length && tiles[idx2] == (byte)TileType.Floor)
                    tiles[idx2] = (byte)TileType.Cobblestone;
            }
        }

        // Spawn points: scattered around the arena edges (enemies come from the perimeter)
        var spawnPoints = new List<SpawnPoint>();
        for (int i = 0; i < 12; i++)
        {
            var edge = rng.Next(4); // 0=top, 1=bottom, 2=left, 3=right
            int sx, sy;
            switch (edge)
            {
                case 0: sx = rng.Next(margin + 2, width - margin - 2); sy = margin + 2; break;
                case 1: sx = rng.Next(margin + 2, width - margin - 2); sy = height - margin - 3; break;
                case 2: sx = margin + 2; sy = rng.Next(margin + 2, height - margin - 2); break;
                default: sx = width - margin - 3; sy = rng.Next(margin + 2, height - margin - 2); break;
            }
            spawnPoints.Add(new SpawnPoint(sx, sy, SpawnPointType.Street));
        }

        return new TileMap
        {
            Width = width,
            Height = height,
            Tiles = tiles,
            Seed = seed,
            Rooms = rooms.ToArray(),
            SpawnPoints = spawnPoints.ToArray()
        };
    }

    /// <summary>
    /// Generate a Mountain Cave: start filled with Wall, carve Floor caverns via
    /// drunkard-walk plus cellular automata, then flood-fill from the entrance
    /// and cut corridors so every room is reachable. Default usage is ~60x50.
    /// Seeded Random for determinism.
    /// </summary>
    public static TileMap GenerateCave(int width, int height, int seed)
    {
        if (width < 16) width = 16;
        if (height < 16) height = 16;

        var rng = new Random(seed);
        var tiles = new byte[width * height];
        Array.Fill(tiles, (byte)TileType.Wall);

        var entranceX = width / 2;
        var entranceY = height - 3;

        var walkerCount = Math.Max(8, (width * height) / 350);
        var steps = Math.Max(width * height / 10, 200);

        DrunkardWalk(tiles, width, height, rng, entranceX, entranceY, steps + steps / 2, radius: 1);
        for (int i = 1; i < walkerCount; i++)
        {
            var sx = rng.Next(2, width - 2);
            var sy = rng.Next(2, height - 2);
            var radius = rng.Next(2) == 0 ? 1 : 2;
            DrunkardWalk(tiles, width, height, rng, sx, sy, steps, radius);
        }

        SmoothCaveCellularAutomata(tiles, width, height, iterations: 4);
        EnforceCaveBorder(tiles, width, height);

        CarveEntranceCorridor(tiles, width, height, entranceX, entranceY);
        var (spawnX, spawnY) = FindCaveEntranceSpawn(tiles, width, height, entranceX, entranceY);

        EnsureCaveConnectivity(tiles, width, height, spawnX, spawnY);
        CarveEntranceCorridor(tiles, width, height, entranceX, entranceY);
        (spawnX, spawnY) = FindCaveEntranceSpawn(tiles, width, height, entranceX, entranceY);

        var rooms = IdentifyCaveRooms(tiles, width, height, minTiles: 12);
        if (rooms.Count == 0)
        {
            var rw = Math.Min(16, width - 6);
            var rh = Math.Min(12, height - 6);
            var rx = Math.Max(2, entranceX - rw / 2);
            var ry = Math.Max(2, height / 2 - rh / 2);
            var fallback = new Room(rx, ry, rw, rh);
            CarveCaveRect(tiles, width, fallback);
            rooms.Add(fallback);
            EnsureCaveConnectivity(tiles, width, height, spawnX, spawnY);
        }

        var spawnPoints = new List<SpawnPoint>
        {
            new SpawnPoint(spawnX, spawnY, SpawnPointType.Player)
        };
        foreach (var room in rooms)
        {
            if (tiles[room.Center.Y * width + room.Center.X] == (byte)TileType.Floor)
                spawnPoints.Add(new SpawnPoint(room.Center.X, room.Center.Y, SpawnPointType.Room));
        }

        return new TileMap
        {
            Width = width,
            Height = height,
            Tiles = tiles,
            Seed = seed,
            Rooms = rooms.ToArray(),
            SpawnPoints = spawnPoints.ToArray()
        };
    }

    private static void DrunkardWalk(byte[] tiles, int width, int height, Random rng,
        int startX, int startY, int steps, int radius)
    {
        int x = Math.Clamp(startX, 1, width - 2);
        int y = Math.Clamp(startY, 1, height - 2);
        int[] dirX = [0, 0, -1, 1];
        int[] dirY = [-1, 1, 0, 0];

        for (int s = 0; s < steps; s++)
        {
            for (int oy = -radius; oy <= radius; oy++)
            {
                for (int ox = -radius; ox <= radius; ox++)
                {
                    var cx = x + ox;
                    var cy = y + oy;
                    if (cx >= 1 && cx < width - 1 && cy >= 1 && cy < height - 1)
                        tiles[cy * width + cx] = (byte)TileType.Floor;
                }
            }

            var dir = rng.Next(4);
            if (rng.Next(5) == 0)
            {
                var cx = width / 2;
                var cy = height / 2;
                dir = Math.Abs(cx - x) > Math.Abs(cy - y)
                    ? (x < cx ? 3 : 2)
                    : (y < cy ? 1 : 0);
            }

            var nx = x + dirX[dir];
            var ny = y + dirY[dir];
            if (nx >= 1 && nx < width - 1 && ny >= 1 && ny < height - 1)
            {
                x = nx;
                y = ny;
            }
        }
    }

    private static void SmoothCaveCellularAutomata(byte[] tiles, int width, int height, int iterations)
    {
        var buffer = new byte[tiles.Length];
        for (int i = 0; i < iterations; i++)
        {
            Array.Copy(tiles, buffer, tiles.Length);
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    var walls = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            if (buffer[(y + dy) * width + (x + dx)] == (byte)TileType.Wall)
                                walls++;
                        }
                    }
                    tiles[y * width + x] = walls >= 5
                        ? (byte)TileType.Wall
                        : (byte)TileType.Floor;
                }
            }
        }
    }

    private static void EnforceCaveBorder(byte[] tiles, int width, int height)
    {
        for (int x = 0; x < width; x++)
        {
            tiles[x] = (byte)TileType.Wall;
            tiles[(height - 1) * width + x] = (byte)TileType.Wall;
        }
        for (int y = 0; y < height; y++)
        {
            tiles[y * width] = (byte)TileType.Wall;
            tiles[y * width + (width - 1)] = (byte)TileType.Wall;
        }
    }

    private static void CarveEntranceCorridor(byte[] tiles, int width, int height, int entranceX, int entranceY)
    {
        entranceX = Math.Clamp(entranceX, 2, width - 3);
        entranceY = Math.Clamp(entranceY, 2, height - 2);

        for (int y = height - 2; y >= Math.Max(2, entranceY - 8); y--)
        {
            tiles[y * width + entranceX] = (byte)TileType.Floor;
            tiles[y * width + entranceX - 1] = (byte)TileType.Floor;
            tiles[y * width + entranceX + 1] = (byte)TileType.Floor;
        }
    }

    private static (int X, int Y) FindCaveEntranceSpawn(byte[] tiles, int width, int height, int hintX, int hintY)
    {
        var mid = Math.Clamp(hintX, 2, width - 3);
        var startY = Math.Clamp(hintY, 2, height - 3);

        for (int y = startY; y >= height / 2; y--)
        {
            for (int dx = 0; dx <= width / 3; dx++)
            {
                foreach (var x in new[] { mid + dx, mid - dx })
                {
                    if (x < 2 || x >= width - 2) continue;
                    if (tiles[y * width + x] == (byte)TileType.Floor
                        && CountFloorNeighbors(tiles, width, height, x, y) >= 3)
                        return (x, y);
                }
            }
        }

        for (int y = height - 3; y >= 2; y--)
        {
            for (int x = 2; x < width - 2; x++)
            {
                if (tiles[y * width + x] == (byte)TileType.Floor)
                    return (x, y);
            }
        }

        return (Math.Clamp(mid, 2, width - 3), Math.Clamp(height / 2, 2, height - 3));
    }

    private static int CountFloorNeighbors(byte[] tiles, int width, int height, int x, int y)
    {
        var n = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                if (tiles[ny * width + nx] == (byte)TileType.Floor)
                    n++;
            }
        }
        return n;
    }

    private static List<Room> IdentifyCaveRooms(byte[] tiles, int width, int height, int minTiles)
    {
        var rooms = new List<Room>();
        var seen = new bool[width * height];

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                var idx = y * width + x;
                if (seen[idx] || tiles[idx] != (byte)TileType.Floor)
                    continue;

                var region = FloodFillFloor(tiles, width, height, x, y, seen);
                if (region.Count < minTiles)
                {
                    foreach (var (rx, ry) in region)
                        tiles[ry * width + rx] = (byte)TileType.Wall;
                    continue;
                }

                int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
                foreach (var (rx, ry) in region)
                {
                    if (rx < minX) minX = rx;
                    if (ry < minY) minY = ry;
                    if (rx > maxX) maxX = rx;
                    if (ry > maxY) maxY = ry;
                }

                rooms.Add(new Room(minX, minY, Math.Max(1, maxX - minX + 1), Math.Max(1, maxY - minY + 1)));
            }
        }

        return rooms;
    }

    private static List<(int X, int Y)> FloodFillFloor(byte[] tiles, int width, int height, int startX, int startY, bool[]? seen = null)
    {
        seen ??= new bool[width * height];
        var region = new List<(int X, int Y)>();
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((startX, startY));
        seen[startY * width + startX] = true;

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            if (tiles[y * width + x] != (byte)TileType.Floor)
                continue;

            region.Add((x, y));
            TryEnqueue(x + 1, y);
            TryEnqueue(x - 1, y);
            TryEnqueue(x, y + 1);
            TryEnqueue(x, y - 1);
        }

        return region;

        void TryEnqueue(int nx, int ny)
        {
            if (nx < 1 || ny < 1 || nx >= width - 1 || ny >= height - 1)
                return;
            var i = ny * width + nx;
            if (seen[i] || tiles[i] != (byte)TileType.Floor)
                return;
            seen[i] = true;
            queue.Enqueue((nx, ny));
        }
    }

    private static void EnsureCaveConnectivity(byte[] tiles, int width, int height, int spawnX, int spawnY)
    {
        if (spawnX < 1 || spawnY < 1 || spawnX >= width - 1 || spawnY >= height - 1)
            return;
        if (tiles[spawnY * width + spawnX] != (byte)TileType.Floor)
            tiles[spawnY * width + spawnX] = (byte)TileType.Floor;

        var reachable = new HashSet<(int X, int Y)>(FloodFillFloor(tiles, width, height, spawnX, spawnY));
        if (reachable.Count == 0)
            return;

        var seen = new bool[width * height];
        foreach (var (x, y) in reachable)
            seen[y * width + x] = true;

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                if (tiles[y * width + x] != (byte)TileType.Floor || seen[y * width + x])
                    continue;

                var region = FloodFillFloor(tiles, width, height, x, y, seen);
                if (region.Count < 8)
                {
                    foreach (var (rx, ry) in region)
                        tiles[ry * width + rx] = (byte)TileType.Wall;
                    continue;
                }

                var bestD = int.MaxValue;
                (int X, int Y) from = (x, y);
                (int X, int Y) to = reachable.First();
                foreach (var cell in region)
                {
                    foreach (var dest in reachable)
                    {
                        var d = Math.Abs(cell.X - dest.X) + Math.Abs(cell.Y - dest.Y);
                        if (d < bestD)
                        {
                            bestD = d;
                            from = cell;
                            to = dest;
                        }
                    }
                }

                CarveCaveCorridor(tiles, width, height, from.X, from.Y, to.X, to.Y);
                foreach (var cell in region)
                    reachable.Add(cell);
            }
        }
    }

    private static void CarveCaveCorridor(byte[] tiles, int width, int height, int x1, int y1, int x2, int y2)
    {
        var x = x1;
        var y = y1;
        while (x != x2)
        {
            x += Math.Sign(x2 - x);
            SetCaveFloor(tiles, width, height, x, y);
            SetCaveFloor(tiles, width, height, x, y - 1);
        }
        while (y != y2)
        {
            y += Math.Sign(y2 - y);
            SetCaveFloor(tiles, width, height, x, y);
            SetCaveFloor(tiles, width, height, x - 1, y);
        }
    }

    private static void SetCaveFloor(byte[] tiles, int width, int height, int x, int y)
    {
        if (x < 1 || y < 1 || x >= width - 1 || y >= height - 1)
            return;
        tiles[y * width + x] = (byte)TileType.Floor;
    }

    private static void CarveCaveRect(byte[] tiles, int width, Room room)
    {
        for (int y = room.Y + 1; y < room.Y + room.Height - 1; y++)
        {
            for (int x = room.X + 1; x < room.X + room.Width - 1; x++)
            {
                if (x >= 1 && y >= 1 && x < width - 1)
                    tiles[y * width + x] = (byte)TileType.Floor;
            }
        }
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
    Street,
    Player
}
