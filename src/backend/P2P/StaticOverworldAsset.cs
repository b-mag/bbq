namespace Carcosa.Server.P2P;

/// <summary>
/// Provides a versioned static overworld asset for peer-hosted games.
/// All peers in the same major game version share the same base map.
/// </summary>
public static class StaticOverworldAsset
{
    public static string GetPreferredAssetPath(int majorVersion)
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "Assets", $"overworld-v{majorVersion}.json");
        if (File.Exists(candidate)) return candidate;

        // Fallback for source-root/dev execution when running directly from the repo.
        var repoRelative = Path.Combine(baseDir, "..", "..", "..", "..", "Assets", $"overworld-v{majorVersion}.json");
        return Path.GetFullPath(repoRelative);
    }

    public static string LoadJson(int majorVersion)
    {
        var path = GetPreferredAssetPath(majorVersion);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Static overworld asset not found for game major version {majorVersion}: {path}");
        }

        return File.ReadAllText(path);
    }
}
