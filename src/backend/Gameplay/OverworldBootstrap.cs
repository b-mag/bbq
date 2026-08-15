// =============================================================================
// OverworldBootstrap.cs — In-EXE overworld for mesh peers (no matchmaking)
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carcosa.Server.Gameplay;

/// <summary>
/// Deterministic overworld baked into the native binary via Generate().
/// All peers on the same major version share the same map (fixed seed).
/// Disk write under Assets/ is optional cache only — never required to serve the map.
/// </summary>
public static class OverworldBootstrap
{
    public const int Width = OverworldWorldGen.Width;
    public const int Height = OverworldWorldGen.Height;
    public const int DefaultSeed = OverworldWorldGen.DefaultSeed;

    /// <summary>
    /// Map a saved overworld point onto the live world.
    /// WorldWidth 200 is the legacy greybox. 0 means "unset" — do not scale
    /// (that used to throw players off the 640 map into empty void).
    /// </summary>
    public static void ClampResume(int savedWorldWidth, ref float x, ref float y)
    {
        if (savedWorldWidth == 200)
        {
            x *= Width / 200f;
            y *= Height / 200f;
        }

        if (x < 0.5f || y < 0.5f || x >= Width - 0.5f || y >= Height - 0.5f)
        {
            x = 320.5f;
            y = 544.5f;
        }
    }

    private static readonly object CacheLock = new();
    private static string? _cachedJson;
    private static int _cachedMajor = -1;

    /// <summary>
    /// Return the overworld JSON for the given major version.
    /// Generated once in-process and cached; never throws for missing disk files.
    /// </summary>
    public static string GetOrCreateJson(int majorVersion)
    {
        lock (CacheLock)
        {
            if (_cachedJson != null && _cachedMajor == majorVersion)
                return _cachedJson;

            var map = Generate(DefaultSeed);
            var json = JsonSerializer.Serialize(map, OverworldBootstrapJsonContext.Default.BootstrapOverworldMap);
            _cachedJson = json;
            _cachedMajor = majorVersion;

            TryWriteDiskCache(majorVersion, json);
            Console.WriteLine($"[Overworld] In-EXE map ready ({Width}x{Height}, seed {DefaultSeed}, v{majorVersion})");
            return json;
        }
    }

    /// <summary>Warm the in-memory cache at process startup.</summary>
    public static void Warm(int majorVersion) => _ = GetOrCreateJson(majorVersion);

    /// <summary>
    /// Legacy name used by older call sites. Prefer <see cref="GetOrCreateJson"/>.
    /// </summary>
    public static string EnsureAssetJson(int majorVersion) => GetOrCreateJson(majorVersion);

    private static void TryWriteDiskCache(int majorVersion, string json)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", $"overworld-v{majorVersion}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Disk is optional — map is already in memory.
        }
    }

    public static BootstrapOverworldMap Generate(int seed) => OverworldWorldGen.Generate(seed);
}

public sealed class BootstrapOverworldMap
{
    public int Width { get; set; }
    public int Height { get; set; }
    public string TilesBase64 { get; set; } = "";
    public int Seed { get; set; }
    public List<BootstrapLandmark> Landmarks { get; set; } = new();
    public List<BootstrapEntrance> DungeonEntrances { get; set; } = new();
    public List<BootstrapWorldObject> WorldObjects { get; set; } = new();
    public BootstrapPoint SpawnPoint { get; set; } = new();
    public List<BootstrapPoint> LakeIsland { get; set; } = new();
    public List<BootstrapPoint> DrainCauseway { get; set; } = new();
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

public sealed class BootstrapWorldObject
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public bool Collision { get; set; }
    public float CollisionRadius { get; set; } = 0.4f;
}

public sealed class BootstrapPoint
{
    public int X { get; set; }
    public int Y { get; set; }
}

[JsonSerializable(typeof(BootstrapOverworldMap))]
[JsonSerializable(typeof(BootstrapLandmark))]
[JsonSerializable(typeof(BootstrapEntrance))]
[JsonSerializable(typeof(BootstrapWorldObject))]
[JsonSerializable(typeof(List<BootstrapWorldObject>))]
[JsonSerializable(typeof(BootstrapPoint))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class OverworldBootstrapJsonContext : JsonSerializerContext;
