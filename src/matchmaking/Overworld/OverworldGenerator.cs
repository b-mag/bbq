// =============================================================================
// OverworldGenerator.cs — Procedural Overworld Map Generation
// =============================================================================
//
// Generates a 200x200 Carcosa-themed overworld using zone-based biome painting.
// Inspired by Zelda: A Link to the Past's overworld layout with distinct regions:
//
//   - SOUTH: Fishing Village (player spawn, sand coast, cobblestone streets)
//   - CENTRAL-WEST: Lake Hali (deep water lake with misty shores)
//   - NORTH: Mountain Range (impassable barrier with a few passes)
//   - EAST: Dark Forest (dense trees with winding paths)
//   - NORTHEAST: Ancient Ruins (The King in Yellow's domain)
//   - CENTRAL: Connecting paths, scattered landmarks
//
// The generator uses simple geometric zone definitions and distance-based
// blending rather than Perlin noise (simpler, more controllable, and the
// zones need to be well-defined for gameplay purposes).
// =============================================================================

namespace Carcosa.Matchmaking.Overworld;

public static class OverworldGenerator
{
    // Greybox core remains 200×200; LTTP-scale expansion tracked in OVERWORLD_VISION.md
    public const int DefaultWidth = 200;
    public const int DefaultHeight = 200;

    /// <summary>
    /// Generate the complete overworld map.
    /// </summary>
    public static OverworldMap Generate(int width = DefaultWidth, int height = DefaultHeight, int? seed = null)
    {
        var actualSeed = seed ?? Random.Shared.Next();
        var rng = new Random(actualSeed);
        var tiles = new byte[width * height];

        // Phase 1: Fill base terrain (grass everywhere)
        Array.Fill(tiles, (byte)OverworldTileType.Grass);

        // Phase 2: Paint biome zones
        PaintMountains(tiles, width, height, rng);
        PaintLakeHali(tiles, width, height, rng);
        PaintDarkForest(tiles, width, height, rng);
        PaintAncientRuins(tiles, width, height, rng);
        PaintFishingVillage(tiles, width, height, rng);
        PaintSouthernCoast(tiles, width, height, rng);

        // Phase 3: Connect regions with paths
        PaintPaths(tiles, width, height, rng);

        // Phase 4: Add mist near Lake Hali
        PaintMist(tiles, width, height, rng);

        // Phase 5: Add dark grass transitions
        PaintDarkGrassTransitions(tiles, width, height);

        // Build the map
        var map = new OverworldMap
        {
            Width = width,
            Height = height,
            Seed = actualSeed,
            SpawnPoint = new SpawnPoint { X = 100, Y = 180 },
        };
        map.EncodeTiles(tiles);

        // Add landmarks
        map.Landmarks = GenerateLandmarks();

        // Add dungeon entrances
        map.DungeonEntrances = GenerateDungeonEntrances();

        // Add world objects (trees, houses, pillars)
        map.WorldObjects = GenerateWorldObjects(tiles, width, height, rng);

        return map;
    }

    // =========================================================================
    // BIOME PAINTING
    // =========================================================================

    /// <summary>
    /// Paint the northern mountain range. Creates an impassable barrier
    /// across the top ~30 rows with a few passes (gaps) for paths through.
    /// </summary>
    private static void PaintMountains(byte[] tiles, int w, int h, Random rng)
    {
        // Mountains span y=0 to y~30, with irregular southern edge
        for (int y = 0; y < 35; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Irregular southern edge using simple sine waves
                var edgeY = 25 + (int)(Math.Sin(x * 0.05) * 4) + (int)(Math.Sin(x * 0.13) * 3);

                if (y < edgeY)
                {
                    tiles[y * w + x] = (byte)OverworldTileType.Mountain;
                }
            }
        }

        // Carve mountain passes (3 tiles wide)
        var passes = new[] { 50, 130, 170 }; // X positions of passes
        foreach (var passX in passes)
        {
            for (int y = 0; y < 35; y++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var px = passX + dx;
                    if (px >= 0 && px < w)
                    {
                        tiles[y * w + px] = (byte)OverworldTileType.Path;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Paint Lake Hali in the central-west area. An oval deep water lake
    /// with shallow water edges and sandy shores.
    /// "Along the shore the cloud waves break / The twin suns sink behind the lake"
    /// </summary>
    private static void PaintLakeHali(byte[] tiles, int w, int h, Random rng)
    {
        // Lake center at approximately (55, 90), oval shape ~40x30 tiles
        var centerX = 55;
        var centerY = 90;
        var radiusX = 22;
        var radiusY = 16;

        for (int y = centerY - radiusY - 5; y <= centerY + radiusY + 5; y++)
        {
            for (int x = centerX - radiusX - 5; x <= centerX + radiusX + 5; x++)
            {
                if (x < 0 || x >= w || y < 0 || y >= h) continue;

                // Normalized distance from center (elliptical)
                var dx = (float)(x - centerX) / radiusX;
                var dy = (float)(y - centerY) / radiusY;
                var dist = Math.Sqrt(dx * dx + dy * dy);

                // Add some irregularity
                var noise = Math.Sin(x * 0.3 + y * 0.2) * 0.15 + Math.Sin(x * 0.1 - y * 0.15) * 0.1;
                dist += noise;

                if (dist < 0.7)
                {
                    tiles[y * w + x] = (byte)OverworldTileType.DeepWater;
                }
                else if (dist < 0.9)
                {
                    tiles[y * w + x] = (byte)OverworldTileType.ShallowWater;
                }
                else if (dist < 1.1)
                {
                    tiles[y * w + x] = (byte)OverworldTileType.Sand;
                }
            }
        }
    }

    /// <summary>
    /// Paint the Dark Forest in the eastern portion of the map.
    /// Dense forest tiles with winding paths carved through.
    /// "Strange is the night where black stars rise"
    /// </summary>
    private static void PaintDarkForest(byte[] tiles, int w, int h, Random rng)
    {
        // Forest region: x=130..195, y=40..150
        for (int y = 40; y < 150; y++)
        {
            for (int x = 130; x < 195; x++)
            {
                if (x >= w || y >= h) continue;

                // Skip if already painted (mountains, etc.)
                if (tiles[y * w + x] == (byte)OverworldTileType.Mountain) continue;

                // Irregular western edge
                var edgeX = 130 + (int)(Math.Sin(y * 0.08) * 8) + (int)(Math.Cos(y * 0.15) * 4);
                if (x < edgeX) continue;

                tiles[y * w + x] = (byte)OverworldTileType.Forest;
            }
        }

        // Carve winding paths through the forest
        CarveForestPath(tiles, w, h, 150, 45, 160, 145, rng); // N-S path
        CarveForestPath(tiles, w, h, 130, 90, 190, 95, rng);  // W-E path
        CarveForestPath(tiles, w, h, 140, 120, 185, 130, rng); // Secondary E path
    }

    /// <summary>
    /// Carve a winding path through forest from (x1,y1) to (x2,y2).
    /// </summary>
    private static void CarveForestPath(byte[] tiles, int w, int h, int x1, int y1, int x2, int y2, Random rng)
    {
        var cx = (float)x1;
        var cy = (float)y1;
        var steps = Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1)) * 2;

        for (int i = 0; i <= steps; i++)
        {
            var t = (float)i / steps;
            var targetX = x1 + (x2 - x1) * t;
            var targetY = y1 + (y2 - y1) * t;

            // Add some wandering
            cx += (targetX - cx) * 0.1f + (rng.NextSingle() - 0.5f) * 1.5f;
            cy += (targetY - cy) * 0.1f + (rng.NextSingle() - 0.5f) * 1.5f;

            // Paint path (2 tiles wide)
            for (int dy = -1; dy <= 0; dy++)
            {
                for (int dx = -1; dx <= 0; dx++)
                {
                    var px = (int)cx + dx;
                    var py = (int)cy + dy;
                    if (px >= 0 && px < w && py >= 0 && py < h)
                    {
                        tiles[py * w + px] = (byte)OverworldTileType.Path;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Paint the Ancient Ruins in the northeast area.
    /// Crumbling walls, broken pillars, the domain of the King in Yellow.
    /// "Songs that the Hyades shall sing / Where flap the tatters of the King"
    /// </summary>
    private static void PaintAncientRuins(byte[] tiles, int w, int h, Random rng)
    {
        // Ruins region: x=140..190, y=35..70 (just south of mountains, east side)
        var ruinCenterX = 165;
        var ruinCenterY = 52;

        // Paint floor area first
        for (int y = 38; y < 70; y++)
        {
            for (int x = 140; x < 190; x++)
            {
                if (x >= w || y >= h) continue;
                if (tiles[y * w + x] == (byte)OverworldTileType.Mountain) continue;
                if (tiles[y * w + x] == (byte)OverworldTileType.Path) continue;

                var dx = (float)(x - ruinCenterX) / 25;
                var dy = (float)(y - ruinCenterY) / 16;
                var dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist < 0.8)
                {
                    tiles[y * w + x] = (byte)OverworldTileType.Cobblestone;
                }
            }
        }

        // Scatter ruin wall fragments
        for (int i = 0; i < 40; i++)
        {
            var rx = ruinCenterX + rng.Next(-20, 20);
            var ry = ruinCenterY + rng.Next(-12, 12);
            if (rx < 0 || rx >= w || ry < 0 || ry >= h) continue;
            if (tiles[ry * w + rx] != (byte)OverworldTileType.Cobblestone) continue;

            // Place a small wall fragment (1-3 tiles)
            var length = rng.Next(1, 4);
            var horizontal = rng.Next(2) == 0;
            for (int j = 0; j < length; j++)
            {
                var wx = horizontal ? rx + j : rx;
                var wy = horizontal ? ry : ry + j;
                if (wx >= 0 && wx < w && wy >= 0 && wy < h)
                {
                    tiles[wy * w + wx] = (byte)OverworldTileType.Ruins;
                }
            }
        }
    }

    /// <summary>
    /// Paint the Fishing Village in the south-central area.
    /// Small buildings with cobblestone streets — the player starting area.
    /// </summary>
    private static void PaintFishingVillage(byte[] tiles, int w, int h, Random rng)
    {
        // Village center at approximately (100, 175)
        var villageCenterX = 100;
        var villageCenterY = 175;

        // Paint cobblestone streets in a grid pattern
        for (int y = villageCenterY - 12; y <= villageCenterY + 8; y++)
        {
            for (int x = villageCenterX - 15; x <= villageCenterX + 15; x++)
            {
                if (x < 0 || x >= w || y < 0 || y >= h) continue;

                // Main streets: horizontal every 8 tiles, vertical every 10 tiles
                var relX = x - (villageCenterX - 15);
                var relY = y - (villageCenterY - 12);

                if (relY % 8 < 2 || relX % 10 < 2)
                {
                    tiles[y * w + x] = (byte)OverworldTileType.Cobblestone;
                }
            }
        }

        // Place small buildings (5x4 rectangles) in the village grid
        var buildingPositions = new (int x, int y)[]
        {
            (villageCenterX - 12, villageCenterY - 10),
            (villageCenterX - 12, villageCenterY - 2),
            (villageCenterX - 2, villageCenterY - 10),
            (villageCenterX - 2, villageCenterY - 2),
            (villageCenterX + 8, villageCenterY - 10),
            (villageCenterX + 8, villageCenterY - 2),
            (villageCenterX - 7, villageCenterY + 4),
            (villageCenterX + 3, villageCenterY + 4),
        };

        foreach (var (bx, by) in buildingPositions)
        {
            PlaceBuilding(tiles, w, h, bx, by, 5, 4);
        }
    }

    /// <summary>
    /// Place a single building (wall perimeter with floor interior and a door).
    /// </summary>
    private static void PlaceBuilding(byte[] tiles, int w, int h, int x, int y, int bw, int bh)
    {
        for (int dy = 0; dy < bh; dy++)
        {
            for (int dx = 0; dx < bw; dx++)
            {
                var tx = x + dx;
                var ty = y + dy;
                if (tx < 0 || tx >= w || ty < 0 || ty >= h) continue;

                if (dx == 0 || dx == bw - 1 || dy == 0 || dy == bh - 1)
                {
                    tiles[ty * w + tx] = (byte)OverworldTileType.Wall;
                }
                else
                {
                    tiles[ty * w + tx] = (byte)OverworldTileType.Floor;
                }
            }
        }

        // Add a door on the south wall (center)
        var doorX = x + bw / 2;
        var doorY = y + bh - 1;
        if (doorX >= 0 && doorX < w && doorY >= 0 && doorY < h)
        {
            tiles[doorY * w + doorX] = (byte)OverworldTileType.Door;
        }
    }

    /// <summary>
    /// Paint the southern coastline — sand and water at the map's bottom edge.
    /// </summary>
    private static void PaintSouthernCoast(byte[] tiles, int w, int h, Random rng)
    {
        for (int y = h - 8; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Don't overwrite village buildings
                var existing = (OverworldTileType)tiles[y * w + x];
                if (existing == OverworldTileType.Wall || existing == OverworldTileType.Floor ||
                    existing == OverworldTileType.Door) continue;

                var depthFromBottom = h - 1 - y;
                var waveOffset = (int)(Math.Sin(x * 0.2 + rng.NextDouble() * 0.3) * 1.5);

                if (depthFromBottom < 3 + waveOffset)
                {
                    tiles[y * w + x] = (byte)OverworldTileType.DeepWater;
                }
                else if (depthFromBottom < 5 + waveOffset)
                {
                    tiles[y * w + x] = (byte)OverworldTileType.Sand;
                }
            }
        }
    }

    /// <summary>
    /// Paint connecting paths between major regions.
    /// </summary>
    private static void PaintPaths(byte[] tiles, int w, int h, Random rng)
    {
        // Main north-south road (from fishing village to mountain pass)
        PaintStraightPath(tiles, w, h, 100, 165, 100, 30, 2);

        // East-west road (connects lake area to forest)
        PaintStraightPath(tiles, w, h, 30, 110, 135, 110, 2);

        // Path from village to lake
        PaintStraightPath(tiles, w, h, 75, 170, 55, 110, 2);

        // Path from main road to ruins
        PaintStraightPath(tiles, w, h, 110, 55, 145, 52, 2);

        // Path from forest entrance to ruins
        PaintStraightPath(tiles, w, h, 140, 55, 140, 90, 2);

        // Path along southern part connecting west to east
        PaintStraightPath(tiles, w, h, 20, 150, 130, 150, 2);
    }

    /// <summary>
    /// Paint a straight path between two points (with L-shaped routing).
    /// </summary>
    private static void PaintStraightPath(byte[] tiles, int w, int h, int x1, int y1, int x2, int y2, int pathWidth)
    {
        // Route horizontally first, then vertically
        var minX = Math.Min(x1, x2);
        var maxX = Math.Max(x1, x2);
        var minY = Math.Min(y1, y2);
        var maxY = Math.Max(y1, y2);

        // Horizontal segment at y1
        for (int x = minX; x <= maxX; x++)
        {
            for (int pw = 0; pw < pathWidth; pw++)
            {
                var py = y1 + pw;
                if (x >= 0 && x < w && py >= 0 && py < h)
                {
                    var existing = (OverworldTileType)tiles[py * w + x];
                    if (existing == OverworldTileType.Grass || existing == OverworldTileType.DarkGrass ||
                        existing == OverworldTileType.Forest || existing == OverworldTileType.Sand)
                    {
                        tiles[py * w + x] = (byte)OverworldTileType.Path;
                    }
                }
            }
        }

        // Vertical segment at x2
        for (int y = minY; y <= maxY; y++)
        {
            for (int pw = 0; pw < pathWidth; pw++)
            {
                var px = x2 + pw;
                if (px >= 0 && px < w && y >= 0 && y < h)
                {
                    var existing = (OverworldTileType)tiles[y * w + px];
                    if (existing == OverworldTileType.Grass || existing == OverworldTileType.DarkGrass ||
                        existing == OverworldTileType.Forest || existing == OverworldTileType.Sand)
                    {
                        tiles[y * w + px] = (byte)OverworldTileType.Path;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Paint mist tiles around Lake Hali for atmosphere.
    /// </summary>
    private static void PaintMist(byte[] tiles, int w, int h, Random rng)
    {
        var centerX = 55;
        var centerY = 90;

        for (int y = centerY - 25; y <= centerY + 25; y++)
        {
            for (int x = centerX - 30; x <= centerX + 30; x++)
            {
                if (x < 0 || x >= w || y < 0 || y >= h) continue;

                var existing = (OverworldTileType)tiles[y * w + x];
                if (existing != OverworldTileType.Grass) continue;

                var dx = (float)(x - centerX) / 28;
                var dy = (float)(y - centerY) / 22;
                var dist = Math.Sqrt(dx * dx + dy * dy);

                // Spotty mist in a ring around the lake
                if (dist > 0.7 && dist < 1.0 && rng.NextDouble() < 0.3)
                {
                    tiles[y * w + x] = (byte)OverworldTileType.Mist;
                }
            }
        }
    }

    /// <summary>
    /// Paint dark grass as transition zones between grass and forest/ruins.
    /// </summary>
    private static void PaintDarkGrassTransitions(byte[] tiles, int w, int h)
    {
        var copy = (byte[])tiles.Clone();

        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                if ((OverworldTileType)copy[y * w + x] != OverworldTileType.Grass) continue;

                // Check if adjacent to forest or ruins
                var hasForestNeighbor = false;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var neighbor = (OverworldTileType)copy[(y + dy) * w + (x + dx)];
                        if (neighbor == OverworldTileType.Forest || neighbor == OverworldTileType.Ruins)
                        {
                            hasForestNeighbor = true;
                            break;
                        }
                    }
                    if (hasForestNeighbor) break;
                }

                if (hasForestNeighbor)
                {
                    tiles[y * w + x] = (byte)OverworldTileType.DarkGrass;
                }
            }
        }
    }

    // =========================================================================
    // LANDMARKS & ENTRANCES
    // =========================================================================

    private static List<Landmark> GenerateLandmarks()
    {
        return new List<Landmark>
        {
            new() { Name = "Lake Hali", X = 55, Y = 90, Type = "lake" },
            new() { Name = "The Fishing Village", X = 100, Y = 175, Type = "village" },
            new() { Name = "Mountain Pass (West)", X = 50, Y = 15, Type = "pass" },
            new() { Name = "Mountain Pass (Central)", X = 130, Y = 15, Type = "pass" },
            new() { Name = "Mountain Pass (East)", X = 170, Y = 15, Type = "pass" },
            new() { Name = "The Dark Forest", X = 160, Y = 95, Type = "forest" },
            new() { Name = "Ruins of the King in Yellow", X = 165, Y = 52, Type = "ruins" },
            new() { Name = "The Pallid Shore", X = 55, Y = 75, Type = "shore" },
            new() { Name = "Aldebaran Crossing", X = 100, Y = 110, Type = "crossroads" },
            new() { Name = "The Hyades Gate", X = 50, Y = 50, Type = "gate" },
            new() { Name = "Court of the Dragon", X = 40, Y = 50, Type = "ash" },
            new() { Name = "Dim Carcosa Approaches", X = 80, Y = 40, Type = "canyon" },
        };
    }

    private static List<DungeonEntrance> GenerateDungeonEntrances()
    {
        return new List<DungeonEntrance>
        {
            new()
            {
                Name = "The Warehouse",
                X = 105,
                Y = 182,
                Scenario = "warehouse",
                DungeonWidth = 80,
                DungeonHeight = 60
            },
            new()
            {
                Name = "Temple of Hali",
                X = 165,
                Y = 55,
                Scenario = "temple",
                DungeonWidth = 100,
                DungeonHeight = 100
            },
            new()
            {
                Name = "Mountain Cave",
                X = 130,
                Y = 28,
                Scenario = "mountain_cave",
                DungeonWidth = 60,
                DungeonHeight = 50
            },
        };
    }

    // =========================================================================
    // WORLD OBJECTS
    // =========================================================================

    private static List<WorldObject> GenerateWorldObjects(byte[] tiles, int w, int h, Random rng)
    {
        var objects = new List<WorldObject>();

        // Scatter trees along forest edges and in grassy areas
        for (int i = 0; i < 150; i++)
        {
            var x = rng.Next(5, w - 5);
            var y = rng.Next(35, h - 10);
            var tile = (OverworldTileType)tiles[y * w + x];

            if (tile == OverworldTileType.Grass || tile == OverworldTileType.DarkGrass)
            {
                objects.Add(new WorldObject
                {
                    Type = "tree",
                    X = x,
                    Y = y,
                    Collision = true,
                    CollisionRadius = 0.4f
                });
            }
        }

        // Ruined pillars in the ruins area
        for (int i = 0; i < 20; i++)
        {
            var x = 165 + rng.Next(-18, 18);
            var y = 52 + rng.Next(-10, 10);
            if (x < 0 || x >= w || y < 0 || y >= h) continue;
            var tile = (OverworldTileType)tiles[y * w + x];

            if (tile == OverworldTileType.Cobblestone)
            {
                objects.Add(new WorldObject
                {
                    Type = "ruined_pillar",
                    X = x,
                    Y = y,
                    Collision = true,
                    CollisionRadius = 0.3f
                });
            }
        }

        // Fishing boats near the village coast
        for (int i = 0; i < 5; i++)
        {
            var x = 90 + rng.Next(0, 20);
            var y = h - 9;
            if (x >= 0 && x < w && y >= 0 && y < h)
            {
                objects.Add(new WorldObject
                {
                    Type = "fishing_boat",
                    X = x,
                    Y = y,
                    Collision = false,
                    CollisionRadius = 0
                });
            }
        }

        // Signposts near paths
        objects.Add(new WorldObject { Type = "signpost", X = 98, Y = 150, Collision = true, CollisionRadius = 0.2f });
        objects.Add(new WorldObject { Type = "signpost", X = 102, Y = 108, Collision = true, CollisionRadius = 0.2f });
        objects.Add(new WorldObject { Type = "signpost", X = 135, Y = 88, Collision = true, CollisionRadius = 0.2f });

        return objects;
    }
}
