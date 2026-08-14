// =============================================================================
// OverworldBootstrap.cs — Bundled overworld for mesh peers (no matchmaking)
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carcosa.Server.Gameplay;

/// <summary>
/// Generates and caches Assets/overworld-v{N}.json when missing so P2P map API works offline.
/// Layout mirrors the Carcosa fishing-village / Lake Hali greybox (200×200).
/// </summary>
public static class OverworldBootstrap
{
    public const int Width = 200;
    public const int Height = 200;

    public static string EnsureAssetJson(int majorVersion)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", $"overworld-v{majorVersion}.json");
        if (File.Exists(path))
            return File.ReadAllText(path);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var map = Generate(seed: 20240814);
        var json = JsonSerializer.Serialize(map, OverworldBootstrapJsonContext.Default.BootstrapOverworldMap);
        File.WriteAllText(path, json);

        // Also try write to repo Assets for packaging
        try
        {
            var repoAssets = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Assets"));
            Directory.CreateDirectory(repoAssets);
            File.WriteAllText(Path.Combine(repoAssets, $"overworld-v{majorVersion}.json"), json);
        }
        catch { /* ignore */ }

        Console.WriteLine($"[Overworld] Generated bundled map at {path}");
        return json;
    }

    public static BootstrapOverworldMap Generate(int seed)
    {
        var rng = new Random(seed);
        var tiles = new byte[Width * Height];
        Array.Fill(tiles, (byte)0); // Grass

        // Lake Hali (west-center)
        PaintEllipse(tiles, 55, 90, 22, 16, 1); // DeepWater
        PaintEllipseRing(tiles, 55, 90, 24, 18, 2); // ShallowWater rim

        // Mountains north
        for (int y = 0; y < 30; y++)
            for (int x = 0; x < Width; x++)
                if (y < 25 + (int)(Math.Sin(x * 0.1) * 3))
                    tiles[y * Width + x] = 4; // Mountain

        // Passes
        foreach (var px in new[] { 50, 130, 170 })
            for (int y = 0; y < 28; y++)
                for (int dx = -1; dx <= 1; dx++)
                    tiles[y * Width + Math.Clamp(px + dx, 0, Width - 1)] = 6; // Path

        // Dark forest east
        for (int y = 40; y < 150; y++)
            for (int x = 130; x < 195; x++)
                if (rng.NextDouble() < 0.85)
                    tiles[y * Width + x] = 3; // Forest

        // Village cobble south
        for (int y = 165; y < 185; y++)
            for (int x = 85; x < 115; x++)
                tiles[y * Width + x] = 10; // Cobblestone

        // Paths
        PaintPath(tiles, 100, 180, 55, 110);
        PaintPath(tiles, 100, 180, 130, 28);
        PaintPath(tiles, 100, 110, 165, 52);

        // Pallid shore
        for (int y = Height - 8; y < Height; y++)
            for (int x = 0; x < Width; x++)
                tiles[y * Width + x] = y >= Height - 4 ? (byte)1 : (byte)7;

        // Ruins NE
        PaintEllipse(tiles, 165, 52, 8, 6, 10);

        return new BootstrapOverworldMap
        {
            Width = Width,
            Height = Height,
            Seed = seed,
            TilesBase64 = Convert.ToBase64String(tiles),
            SpawnPoint = new BootstrapPoint { X = 100, Y = 180 },
            Landmarks =
            [
                new() { Name = "The Fishing Village", X = 100, Y = 175, Type = "village" },
                new() { Name = "Lake Hali", X = 55, Y = 90, Type = "lake" },
                new() { Name = "Ruins of the King in Yellow", X = 165, Y = 52, Type = "ruins" },
                new() { Name = "The Dark Forest", X = 160, Y = 95, Type = "forest" },
                new() { Name = "Court of the Dragon (planned)", X = 40, Y = 50, Type = "ash" },
            ],
            DungeonEntrances =
            [
                new() { Name = "The Warehouse", X = 105, Y = 182, Scenario = "warehouse", DungeonWidth = 80, DungeonHeight = 60 },
                new() { Name = "Temple of Hali", X = 165, Y = 55, Scenario = "temple", DungeonWidth = 100, DungeonHeight = 100 },
                new() { Name = "Mountain Cave", X = 130, Y = 28, Scenario = "mountain_cave", DungeonWidth = 60, DungeonHeight = 50 },
            ],
            WorldObjects = [],
        };
    }

    private static void PaintEllipse(byte[] tiles, int cx, int cy, int rx, int ry, byte tile)
    {
        for (int y = cy - ry; y <= cy + ry; y++)
        for (int x = cx - rx; x <= cx + rx; x++)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
            double nx = (x - cx) / (double)rx;
            double ny = (y - cy) / (double)ry;
            if (nx * nx + ny * ny <= 1)
                tiles[y * Width + x] = tile;
        }
    }

    private static void PaintEllipseRing(byte[] tiles, int cx, int cy, int rx, int ry, byte tile)
    {
        for (int y = cy - ry; y <= cy + ry; y++)
        for (int x = cx - rx; x <= cx + rx; x++)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
            double nx = (x - cx) / (double)rx;
            double ny = (y - cy) / (double)ry;
            var d = nx * nx + ny * ny;
            if (d <= 1 && d > 0.7)
                tiles[y * Width + x] = tile;
        }
    }

    private static void PaintPath(byte[] tiles, int x0, int y0, int x1, int y1)
    {
        int x = x0, y = y0;
        while (x != x1 || y != y1)
        {
            for (int dy = 0; dy <= 1; dy++)
            for (int dx = 0; dx <= 1; dx++)
            {
                int tx = Math.Clamp(x + dx, 0, Width - 1);
                int ty = Math.Clamp(y + dy, 0, Height - 1);
                var t = tiles[ty * Width + tx];
                if (t is 0 or 3 or 14)
                    tiles[ty * Width + tx] = 6;
            }
            if (x < x1) x++; else if (x > x1) x--;
            else if (y < y1) y++; else if (y > y1) y--;
        }
    }
}

public sealed class BootstrapOverworldMap
{
    public int Width { get; set; }
    public int Height { get; set; }
    public string TilesBase64 { get; set; } = "";
    public int Seed { get; set; }
    public List<BootstrapLandmark> Landmarks { get; set; } = new();
    public List<BootstrapEntrance> DungeonEntrances { get; set; } = new();
    public List<object> WorldObjects { get; set; } = new();
    public BootstrapPoint SpawnPoint { get; set; } = new();
}

public sealed class BootstrapLandmark
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public string Type { get; set; } = "";
}

public sealed class BootstrapEntrance
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public string Scenario { get; set; } = "";
    public int DungeonWidth { get; set; }
    public int DungeonHeight { get; set; }
}

public sealed class BootstrapPoint
{
    public int X { get; set; }
    public int Y { get; set; }
}

[JsonSerializable(typeof(BootstrapOverworldMap))]
[JsonSerializable(typeof(BootstrapLandmark))]
[JsonSerializable(typeof(BootstrapEntrance))]
[JsonSerializable(typeof(BootstrapPoint))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class OverworldBootstrapJsonContext : JsonSerializerContext;
