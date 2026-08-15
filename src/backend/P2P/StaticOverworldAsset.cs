using Carcosa.Server.Gameplay;

namespace Carcosa.Server.P2P;

/// <summary>
/// Provides a versioned overworld map for peer-hosted games.
/// The map is generated in-process (compiled into the EXE) so solo/offline
/// play never depends on a sidecar Assets file.
/// </summary>
public static class StaticOverworldAsset
{
    public static string GetPreferredAssetPath(int majorVersion)
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", $"overworld-v{majorVersion}.json");
    }

    /// <summary>
    /// Load overworld JSON. Always succeeds via in-EXE bootstrap.
    /// Optional on-disk Assets/ file is used only as a best-effort cache by OverworldBootstrap.
    /// </summary>
    public static string LoadJson(int majorVersion) => OverworldBootstrap.GetOrCreateJson(majorVersion);
}
