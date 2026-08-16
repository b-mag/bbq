// =============================================================================
// OverworldWorldGen.cs — Large Carcosa overworld (LTTP-scale+)
// =============================================================================
// 640×640 tiles ≈ 10× the old 200×200 area. Regions are painted with noisy
// edges so biomes meet like 90s SNES maps instead of axis-aligned rectangles.
// =============================================================================

namespace Carcosa.Server.Gameplay;

internal static class OverworldWorldGen
{
    public const int Width = 640;
    public const int Height = 640;
    public const int DefaultSeed = 20240815;

    private const byte Grass = 0, DeepWater = 1, ShallowWater = 2, Forest = 3, Mountain = 4,
        Ruins = 5, Path = 6, Sand = 7, Bridge = 8, DungeonEntrance = 9, Cobblestone = 10,
        Wall = 11, Floor = 12, Door = 13, DarkGrass = 14, Mist = 15, Desert = 16, Swamp = 17,
        MountainPath = 18, Snow = 19, Ash = 20, Palace = 21, Flesh = 22, Ladder = 23;

    public static BootstrapOverworldMap Generate(int seed)
    {
        var rng = new Random(seed);
        var w = Width;
        var h = Height;
        var tiles = new byte[w * h];
        Array.Fill(tiles, Grass);

        PaintMountains(tiles, w, h, rng);
        PaintLadders(tiles, w, h);
        PaintDesert(tiles, w, h, rng);
        PaintAshWastes(tiles, w, h, rng);
        PaintLakeAndRiver(tiles, w, h, rng);
        var (island, causeway) = PaintLakeIslandAndCauseway(tiles, w, h);
        PaintSwamp(tiles, w, h, rng);
        PaintForest(tiles, w, h, rng);
        PaintPalacesAndRuins(tiles, w, h, rng);
        PaintCoast(tiles, w, h, rng);
        PaintVillage(tiles, w, h, rng);
        PaintPathsAndBridge(tiles, w, h);
        PaintMistAndTransitions(tiles, w, h, rng);

        var spawnX = X(0.50);
        var spawnY = Y(0.85);

        return new BootstrapOverworldMap
        {
            Width = w,
            Height = h,
            Seed = seed,
            TilesBase64 = Convert.ToBase64String(tiles),
            SpawnPoint = new BootstrapPoint { X = spawnX, Y = spawnY },
            Landmarks = Landmarks(),
            DungeonEntrances = Entrances(),
            WorldObjects = Objects(tiles, w, h, rng),
            LakeIsland = island,
            DrainCauseway = causeway,
        };
    }

    private static int X(double n) => Math.Clamp((int)Math.Round(n * (Width - 1)), 0, Width - 1);
    private static int Y(double n) => Math.Clamp((int)Math.Round(n * (Height - 1)), 0, Height - 1);

    private static int Noise(int x, int y, int mag)
    {
        var n = Math.Sin(x * 0.07 + y * 0.05) * mag + Math.Sin(x * 0.17 - y * 0.11) * (mag * 0.55);
        return (int)Math.Round(n);
    }

    private static void Set(byte[] t, int w, int h, int x, int y, byte v)
    {
        if ((uint)x < (uint)w && (uint)y < (uint)h) t[y * w + x] = v;
    }

    private static byte Get(byte[] t, int w, int h, int x, int y)
        => (uint)x < (uint)w && (uint)y < (uint)h ? t[y * w + x] : DeepWater;

    private static void PaintMountains(byte[] t, int w, int h, Random rng)
    {
        for (int y = 0; y < Y(0.22); y++)
        {
            for (int x = 0; x < w; x++)
            {
                var edge = Y(0.14) + Noise(x, y, 10);
                if (y < edge)
                    Set(t, w, h, x, y, y < Y(0.055) + Noise(x, 0, 4) ? Snow : Mountain);
            }
        }

        foreach (var px in new[] { 0.22, 0.50, 0.78 })
        {
            var cx = X(px);
            for (int y = 0; y < Y(0.18); y++)
            {
                var wobble = (int)(Math.Sin(y * 0.2) * 2);
                for (int dx = -2; dx <= 2; dx++)
                    Set(t, w, h, cx + dx + wobble, y, MountainPath);
            }
        }
    }

    /// <summary>
    /// LTTP-style cliff ladders: vertical strips on south-facing mountain walls.
    /// No stamina — they are just a way onto a higher terrace.
    /// </summary>
    private static void PaintLadders(byte[] t, int w, int h)
    {
        foreach (var px in new[] { 0.18, 0.36, 0.64, 0.88 })
        {
            var cx = X(px);
            for (int y = Y(0.04); y < Y(0.22); y++)
            {
                if (Get(t, w, h, cx, y) is not (Mountain or Snow or MountainPath)) continue;
                var south = Get(t, w, h, cx, y + 1);
                if (south is Mountain or Snow) { Set(t, w, h, cx, y, Ladder); continue; }
                if (south is Grass or Path or DarkGrass or Sand or MountainPath or Ash)
                    Set(t, w, h, cx, y, Ladder);
            }
        }

        for (int y = 4; y < Y(0.24); y++)
        for (int x = 10; x < w - 10; x++)
        {
            if (Get(t, w, h, x, y) is not Mountain) continue;
            var south = Get(t, w, h, x, y + 1);
            if (south is not (Grass or Path or DarkGrass or Sand or MountainPath or Ash)) continue;
            if ((x * 17 + y * 11) % 23 != 0) continue;
            for (int k = 0; k < 10; k++)
            {
                var ty = y - k;
                if (Get(t, w, h, x, ty) is Mountain or Snow)
                    Set(t, w, h, x, ty, Ladder);
            }
        }
    }

    private static (List<BootstrapPoint> island, List<BootstrapPoint> causeway) PaintLakeIslandAndCauseway(byte[] t, int w, int h)
    {
        var island = new List<BootstrapPoint>();
        var causeway = new List<BootstrapPoint>();
        var cx = X(0.32);
        var cy = Y(0.42);

        for (int y = cy - 4; y <= cy + 4; y++)
        for (int x = cx - 5; x <= cx + 5; x++)
        {
            var nx = (x - cx) / 5.0;
            var ny = (y - cy) / 4.0;
            if (nx * nx + ny * ny >= 0.85) continue;
            Set(t, w, h, x, y, (x + y) % 3 == 0 ? Cobblestone : Sand);
            island.Add(new BootstrapPoint { X = x, Y = y });
        }

        var yShore = Y(0.52);
        var yIsland = cy + 4;
        for (int y = yIsland; y <= yShore; y++)
        for (int dx = -1; dx <= 1; dx++)
        {
            var x = cx + dx;
            if (Get(t, w, h, x, y) is DeepWater or ShallowWater)
                Set(t, w, h, x, y, Sand);
            causeway.Add(new BootstrapPoint { X = x, Y = y });
        }

        return (island, causeway);
    }

    private static void PaintDesert(byte[] t, int w, int h, Random rng)
    {
        var cx = X(0.18); var cy = Y(0.40);
        var rx = X(0.20); var ry = Y(0.14);
        for (int y = cy - ry; y <= cy + ry; y++)
        for (int x = cx - rx; x <= cx + rx; x++)
        {
            var nx = (x - cx) / (double)rx;
            var ny = (y - cy) / (double)ry;
            if (nx * nx + ny * ny + Math.Sin(x * 0.08) * 0.12 < 1)
                Set(t, w, h, x, y, Desert);
        }
    }

    private static void PaintAshWastes(byte[] t, int w, int h, Random rng)
    {
        var cx = X(0.14); var cy = Y(0.22);
        var rx = X(0.12); var ry = Y(0.08);
        for (int y = cy - ry; y <= cy + ry; y++)
        for (int x = cx - rx; x <= cx + rx; x++)
        {
            var nx = (x - cx) / (double)rx;
            var ny = (y - cy) / (double)ry;
            if (nx * nx + ny * ny < 1.0 + Math.Sin(x * 0.2) * 0.15)
                Set(t, w, h, x, y, Ash);
        }
    }

    private static void PaintLakeAndRiver(byte[] t, int w, int h, Random rng)
    {
        var cx = X(0.32); var cy = Y(0.42);
        var rx = X(0.14); var ry = Y(0.10);
        for (int y = cy - ry - 8; y <= cy + ry + 8; y++)
        for (int x = cx - rx - 8; x <= cx + rx + 8; x++)
        {
            var nx = (x - cx) / (double)rx;
            var ny = (y - cy) / (double)ry;
            var d = Math.Sqrt(nx * nx + ny * ny) + Math.Sin(x * 0.15) * 0.08;
            if (d < 0.72) Set(t, w, h, x, y, DeepWater);
            else if (d < 0.92) Set(t, w, h, x, y, ShallowWater);
            else if (d < 1.12) Set(t, w, h, x, y, Sand);
        }

        // River: mountain pass → lake → south-east to the sea
        PaintWinding(t, w, h, X(0.50), Y(0.12), cx, cy - 6, DeepWater, 2);
        PaintWinding(t, w, h, cx + 8, cy + 8, X(0.62), Y(0.92), DeepWater, 2);
        PaintWinding(t, w, h, X(0.62), Y(0.92), X(0.70), Y(0.98), DeepWater, 3);
        // Shallow banks
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (t[y * w + x] != DeepWater) continue;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var nx = x + dx; var ny = y + dy;
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                if (t[ny * w + nx] is Grass or Desert or DarkGrass or Forest)
                    t[ny * w + nx] = ShallowWater;
            }
        }
    }

    private static void PaintWinding(byte[] t, int w, int h, int x0, int y0, int x1, int y1, byte tile, int radius)
    {
        var steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) * 2 + 1;
        for (int i = 0; i <= steps; i++)
        {
            var u = i / (double)steps;
            var x = x0 + (x1 - x0) * u + Math.Sin(u * 9) * 4;
            var y = y0 + (y1 - y0) * u + Math.Cos(u * 7) * 3;
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                if (dx * dx + dy * dy <= radius * radius + 1)
                    Set(t, w, h, (int)x + dx, (int)y + dy, tile);
        }
    }

    private static void PaintSwamp(byte[] t, int w, int h, Random rng)
    {
        var cx = X(0.28); var cy = Y(0.58);
        var rx = X(0.16); var ry = Y(0.10);
        for (int y = cy - ry; y <= cy + ry; y++)
        for (int x = cx - rx; x <= cx + rx; x++)
        {
            var nx = (x - cx) / (double)rx;
            var ny = (y - cy) / (double)ry;
            if (nx * nx + ny * ny + Math.Sin(y * 0.12) * 0.1 < 1)
            {
                var existing = Get(t, w, h, x, y);
                if (existing is Grass or DarkGrass)
                    Set(t, w, h, x, y, rng.NextDouble() < 0.12 ? ShallowWater : Swamp);
            }
        }
    }

    private static void PaintForest(byte[] t, int w, int h, Random rng)
    {
        for (int y = Y(0.32); y < Y(0.72); y++)
        {
            var edge = X(0.62) + Noise(y, 3, 14);
            for (int x = edge; x < w - 6; x++)
            {
                if (Get(t, w, h, x, y) is Mountain or Snow or DeepWater or MountainPath) continue;
                Set(t, w, h, x, y, Forest);
            }
        }
        PaintWinding(t, w, h, X(0.64), Y(0.36), X(0.90), Y(0.68), Path, 1);
        PaintWinding(t, w, h, X(0.70), Y(0.40), X(0.70), Y(0.66), Path, 1);
    }

    private static void PaintPalacesAndRuins(byte[] t, int w, int h, Random rng)
    {
        var pcx = X(0.82); var pcy = Y(0.24);
        for (int y = pcy - 28; y <= pcy + 28; y++)
        for (int x = pcx - 36; x <= pcx + 36; x++)
        {
            var nx = (x - pcx) / 36.0;
            var ny = (y - pcy) / 28.0;
            if (nx * nx + ny * ny < 0.9)
                Set(t, w, h, x, y, Palace);
        }

        var rcx = X(0.78); var rcy = Y(0.34);
        for (int y = rcy - 22; y <= rcy + 22; y++)
        for (int x = rcx - 30; x <= rcx + 30; x++)
        {
            var nx = (x - rcx) / 30.0;
            var ny = (y - rcy) / 22.0;
            if (nx * nx + ny * ny < 0.75 && Get(t, w, h, x, y) is Palace or Grass or Forest)
                Set(t, w, h, x, y, Cobblestone);
        }

        for (int i = 0; i < 70; i++)
        {
            var rx = rcx + rng.Next(-24, 24);
            var ry = rcy + rng.Next(-16, 16);
            if (Get(t, w, h, rx, ry) != Cobblestone) continue;
            var len = rng.Next(2, 5);
            var horiz = rng.Next(2) == 0;
            for (int j = 0; j < len; j++)
                Set(t, w, h, horiz ? rx + j : rx, horiz ? ry : ry + j, Ruins);
        }
    }

    private static void PaintCoast(byte[] t, int w, int h, Random rng)
    {
        for (int y = Y(0.90); y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var existing = Get(t, w, h, x, y);
            if (existing is Wall or Floor or Door or Flesh or Cobblestone) continue;
            var depth = h - 1 - y;
            var wave = (int)(Math.Sin(x * 0.12) * 3);
            if (depth < 8 + wave) Set(t, w, h, x, y, DeepWater);
            else if (depth < 16 + wave) Set(t, w, h, x, y, Sand);
        }
    }

    private static void PaintVillage(byte[] t, int w, int h, Random rng)
    {
        var vx = X(0.50); var vy = Y(0.84);
        for (int y = vy - 28; y <= vy + 18; y++)
        for (int x = vx - 40; x <= vx + 40; x++)
        {
            var existing = Get(t, w, h, x, y);
            if (existing is DeepWater or Mountain) continue;
            var relX = x - (vx - 40);
            var relY = y - (vy - 28);
            if (relY % 10 < 2 || relX % 12 < 2)
                Set(t, w, h, x, y, Cobblestone);
            else if (existing is Grass)
                Set(t, w, h, x, y, rng.NextDouble() < 0.45 ? Flesh : Grass);
        }

        // West hamlet
        var hx = X(0.18); var hy = Y(0.86);
        for (int y = hy - 12; y <= hy + 8; y++)
        for (int x = hx - 14; x <= hx + 14; x++)
            if (Get(t, w, h, x, y) is Grass or Sand)
                Set(t, w, h, x, y, (x + y) % 7 < 2 ? Cobblestone : Flesh);
    }

    private static void PaintPathsAndBridge(byte[] t, int w, int h)
    {
        PaintStraight(t, w, h, X(0.50), Y(0.84), X(0.50), Y(0.14), Path);
        PaintStraight(t, w, h, X(0.12), Y(0.46), X(0.78), Y(0.46), Path);
        PaintStraight(t, w, h, X(0.50), Y(0.84), X(0.32), Y(0.46), Path);
        PaintStraight(t, w, h, X(0.52), Y(0.34), X(0.76), Y(0.30), Path);

        // Bridge where the north-south road crosses the river
        for (int y = Y(0.50); y <= Y(0.54); y++)
        for (int x = X(0.49); x <= X(0.52); x++)
            if (Get(t, w, h, x, y) is DeepWater or ShallowWater or Path)
                Set(t, w, h, x, y, Bridge);
    }

    private static void PaintStraight(byte[] t, int w, int h, int x1, int y1, int x2, int y2, byte tile)
    {
        var minX = Math.Min(x1, x2); var maxX = Math.Max(x1, x2);
        var minY = Math.Min(y1, y2); var maxY = Math.Max(y1, y2);
        for (int x = minX; x <= maxX; x++)
        for (int pw = 0; pw < 2; pw++)
        {
            var py = y1 + pw;
            var existing = Get(t, w, h, x, py);
            if (existing is Grass or DarkGrass or Forest or Sand or Desert or Swamp or Flesh or Ash or Snow)
                Set(t, w, h, x, py, tile);
        }
        for (int y = minY; y <= maxY; y++)
        for (int pw = 0; pw < 2; pw++)
        {
            var px = x2 + pw;
            var existing = Get(t, w, h, px, y);
            if (existing is Grass or DarkGrass or Forest or Sand or Desert or Swamp or Flesh or Ash or Snow)
                Set(t, w, h, px, y, tile);
        }
    }

    private static void PaintMistAndTransitions(byte[] t, int w, int h, Random rng)
    {
        var copy = (byte[])t.Clone();
        for (int y = 1; y < h - 1; y++)
        for (int x = 1; x < w - 1; x++)
        {
            if (copy[y * w + x] != Grass) continue;
            var near = false;
            for (int dy = -1; dy <= 1 && !near; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var n = copy[(y + dy) * w + (x + dx)];
                if (n is Forest or Ruins or Swamp or Flesh) { near = true; break; }
            }
            if (near) t[y * w + x] = DarkGrass;
        }

        var lx = X(0.32); var ly = Y(0.42);
        for (int y = ly - 50; y <= ly + 50; y++)
        for (int x = lx - 60; x <= lx + 60; x++)
        {
            if (Get(t, w, h, x, y) != Grass) continue;
            var dx = (x - lx) / 50.0;
            var dy = (y - ly) / 40.0;
            var d = Math.Sqrt(dx * dx + dy * dy);
            if (d > 0.7 && d < 1.15 && rng.NextDouble() < 0.28)
                Set(t, w, h, x, y, Mist);
        }
    }

    private static List<BootstrapLandmark> Landmarks() =>
    [
        new() { Name = "The Fishing Village", X = X(0.50), Y = Y(0.84), Type = "village" },
        new() { Name = "Lake Hali", X = X(0.32), Y = Y(0.42), Type = "lake" },
        new() { Name = "The Pallid Shore", X = X(0.32), Y = Y(0.36), Type = "shore" },
        new() { Name = "The Waste", X = X(0.18), Y = Y(0.40), Type = "desert" },
        new() { Name = "Marshes of Yhtill", X = X(0.28), Y = Y(0.58), Type = "swamp" },
        new() { Name = "The Dark Forest", X = X(0.78), Y = Y(0.52), Type = "forest" },
        new() { Name = "Ruins of the King in Yellow", X = X(0.78), Y = Y(0.34), Type = "ruins" },
        new() { Name = "The Yellow Palaces", X = X(0.82), Y = Y(0.24), Type = "palace" },
        new() { Name = "Court of the Dragon", X = X(0.14), Y = Y(0.22), Type = "ash" },
        new() { Name = "Aldebaran Crossing", X = X(0.50), Y = Y(0.46), Type = "crossroads" },
        new() { Name = "The Hyades Gate", X = X(0.22), Y = Y(0.16), Type = "gate" },
        new() { Name = "Black Stars", X = X(0.50), Y = Y(0.04), Type = "peaks" },
        new() { Name = "Dim Carcosa Approaches", X = X(0.40), Y = Y(0.18), Type = "canyon" },
        new() { Name = "The Intact House", X = X(0.32), Y = Y(0.42), Type = "shop" },
        new() { Name = "West Hamlet", X = X(0.18), Y = Y(0.86), Type = "village" },
        new() { Name = "The Twin Suns Road", X = X(0.50), Y = Y(0.30), Type = "road" },
        new() { Name = "The Dream Hull", X = X(0.56), Y = Y(0.945), Type = "wreck" },
    ];

    private static List<BootstrapEntrance> Entrances() =>
    [
        new() { Name = "The Drowned Dock", X = X(0.52), Y = Y(0.90), Scenario = "drowned_dock", DungeonWidth = 80, DungeonHeight = 60 },
        new() { Name = "Temple of Hali", X = X(0.78), Y = Y(0.34), Scenario = "temple", DungeonWidth = 100, DungeonHeight = 100 },
        new() { Name = "Mountain Cave", X = X(0.50), Y = Y(0.16), Scenario = "mountain_cave", DungeonWidth = 60, DungeonHeight = 50 },
        new() { Name = "Sunken Cyclopean Quay", X = X(0.70), Y = Y(0.94), Scenario = "warehouse", DungeonWidth = 90, DungeonHeight = 70 },
        new() { Name = "Palace Crypt", X = X(0.82), Y = Y(0.24), Scenario = "temple", DungeonWidth = 80, DungeonHeight = 80 },
    ];

    private static List<BootstrapWorldObject> Objects(byte[] tiles, int w, int h, Random rng)
    {
        var list = new List<BootstrapWorldObject>();
        void Add(string type, float x, float y, bool col, float rad) =>
            list.Add(new BootstrapWorldObject { Type = type, X = x, Y = y, Collision = col, CollisionRadius = rad });

        for (int i = 0; i < 420; i++)
        {
            var x = rng.Next(8, w - 8);
            var y = rng.Next(Y(0.18), h - 20);
            var tile = Get(tiles, w, h, x, y);
            if (tile is Grass or DarkGrass or Swamp)
                Add("tree", x + 0.5f, y + 0.5f, true, 0.4f);
        }

        for (int i = 0; i < 40; i++)
        {
            var x = X(0.78) + rng.Next(-22, 22);
            var y = Y(0.34) + rng.Next(-14, 14);
            if (Get(tiles, w, h, x, y) == Cobblestone)
                Add("ruined_pillar", x + 0.5f, y + 0.5f, true, 0.3f);
        }

        var vx = X(0.50); var vy = Y(0.84);
        var houseTypes = new[] { "organic_house", "giger_house", "mud_hut" };
        var houseSpots = new (int dx, int dy)[]
        {
            (-18, -16), (-6, -16), (8, -16), (20, -16),
            (-18, -4), (-6, -4), (8, -4), (20, -4),
            (-12, 8), (4, 8), (16, 8),
        };
        foreach (var (dx, dy) in houseSpots)
            Add(houseTypes[Math.Abs(dx + dy) % houseTypes.Length], vx + dx + 0.5f, vy + dy + 0.5f, true, 1.5f);

        Add("giger_house", X(0.16) + 0.5f, Y(0.85) + 0.5f, true, 1.5f);
        Add("mud_hut", X(0.20) + 0.5f, Y(0.87) + 0.5f, true, 1.4f);
        Add("organic_house", X(0.14) + 0.5f, Y(0.88) + 0.5f, true, 1.5f);

        Add("dark_tower", X(0.82) + 0.5f, Y(0.22) + 0.5f, true, 1.2f);
        Add("dark_tower", X(0.86) + 0.5f, Y(0.26) + 0.5f, true, 1.2f);
        Add("bone_spire", X(0.14) + 0.5f, Y(0.20) + 0.5f, true, 0.7f);
        Add("bone_spire", X(0.18) + 0.5f, Y(0.24) + 0.5f, true, 0.7f);
        Add("meditation_altar", X(0.50) + 0.5f, Y(0.82) + 0.5f, true, 1.4f);
        Add("lake_shop", X(0.32) + 0.5f, Y(0.42) + 0.5f, true, 1.5f);

        for (int i = 0; i < 8; i++)
            Add("wreck_boat", X(0.46) + i * 3 + 0.5f, Y(0.93) + (i % 2), false, 0);
        for (int i = 0; i < 6; i++)
            Add("dock_post", X(0.48) + i * 4 + 0.5f, Y(0.91) + 0.5f, true, 0.3f);
        Add("fishing_boat", X(0.47) + 0.5f, Y(0.94) + 0.5f, false, 0);
        Add("village_net", X(0.54) + 0.5f, Y(0.90) + 0.5f, false, 0);

        Add("signpost", X(0.50) + 0.5f, Y(0.70) + 0.5f, true, 0.2f);
        Add("signpost", X(0.50) + 0.5f, Y(0.46) + 0.5f, true, 0.2f);
        Add("signpost", X(0.64) + 0.5f, Y(0.46) + 0.5f, true, 0.2f);

        Add("npc_cassilda", X(0.51) + 0.5f, Y(0.86) + 0.5f, false, 0);
        Add("npc_widow", X(0.18) + 0.5f, Y(0.87) + 0.5f, false, 0);
        Add("npc_ferryman", X(0.32) + 0.5f, Y(0.36) + 0.5f, false, 0);
        Add("npc_ashwalker", X(0.18) + 0.5f, Y(0.40) + 0.5f, false, 0);
        Add("npc_marsh", X(0.28) + 0.5f, Y(0.58) + 0.5f, false, 0);
        Add("npc_ranger", X(0.78) + 0.5f, Y(0.52) + 0.5f, false, 0);
        Add("npc_priest", X(0.82) + 0.5f, Y(0.26) + 0.5f, false, 0);
        Add("npc_hermit", X(0.50) + 0.5f, Y(0.10) + 0.5f, false, 0);
        Add("npc_ember", X(0.14) + 0.5f, Y(0.22) + 0.5f, false, 0);

        Add("dream_ship", X(0.56) + 0.5f, Y(0.945) + 0.5f, true, 3.2f);
        Add("npc_merek", X(0.54) + 0.5f, Y(0.94) + 0.5f, false, 0);
        Add("npc_agwan", X(0.505) + 0.5f, Y(0.888) + 0.5f, false, 0);
        Add("npc_agwan", X(0.535) + 0.5f, Y(0.888) + 0.5f, false, 0);

        Add("npc_fisher", X(0.48) + 0.5f, Y(0.90) + 0.5f, false, 0);
        Add("npc_fisher", X(0.53) + 0.5f, Y(0.91) + 0.5f, false, 0);
        Add("npc_villager", X(0.50) + 0.5f, Y(0.83) + 0.5f, false, 0);
        Add("npc_villager", X(0.46) + 0.5f, Y(0.85) + 0.5f, false, 0);
        Add("npc_satyr", X(0.54) + 0.5f, Y(0.84) + 0.5f, false, 0);
        Add("npc_maskbearer", X(0.49) + 0.5f, Y(0.88) + 0.5f, false, 0);
        Add("npc_monk", X(0.82) + 0.5f, Y(0.25) + 0.5f, false, 0);
        Add("npc_monk", X(0.78) + 0.5f, Y(0.35) + 0.5f, false, 0);

        return list;
    }
}
