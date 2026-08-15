// =============================================================================
// PeerIdentity.cs — Unique Peer Identification & Address Information
// =============================================================================
//
// OVERVIEW:
// Each Carcosa.Server instance in the mesh has a unique identity that persists
// across sessions. The identity includes:
//   - A persistent peer ID (generated once, stored locally)
//   - The public-facing network address (IP:port)
//   - Version information for compatibility checking
//   - A display name (the player's chosen name)
//
// PERSISTENCE:
// The peer ID is generated on first launch and saved to a local file
// (peer-identity.json). This ensures a peer is consistently identifiable
// across restarts, which is important for:
//   - Peer Exchange (other peers remember us)
//   - Anti-cheat reputation (ban lists persist)
//   - Cached peer lists (reconnecting to the same peer by ID)
//
// NETWORK ADDRESS:
// The public address is discovered at runtime via:
//   1. STUN query (determines public IP behind NAT)
//   2. UPnP port mapping (opens router port)
//   3. Manual specification (--public-address CLI flag)
//   4. Localhost fallback (LAN-only mode)
//
// WHY A SEPARATE FILE:
// Identity is orthogonal to networking — it defines WHO we are, not HOW we
// connect. This separation allows identity to be established before any
// network connections are made.
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carcosa.Server.P2P;

/// <summary>
/// Represents the unique, persistent identity of this peer in the mesh network.
/// Generated once on first launch, persisted to disk, and included in every
/// handshake with other peers.
/// </summary>
public sealed class PeerIdentity
{
    /// <summary>
    /// Unique identifier for this peer. 16-character hex string generated from a GUID.
    /// Persists across restarts (saved to peer-identity.json).
    /// </summary>
    public required string PeerId { get; init; }

    /// <summary>
    /// Human-readable display name for this peer (the player's name).
    /// Can change between sessions — not used for identification, only display.
    /// </summary>
    public string DisplayName { get; set; } = "Unknown";

    /// <summary>
    /// The world shard this peer is currently part of.
    /// Used by other peers and the tracker to route connections.
    /// </summary>
    public string WorldId { get; set; } = "";

    /// <summary>
    /// Public TCP address (IP:listenPort) for tracker / WebSocket fallback.
    /// </summary>
    public string PublicAddress { get; set; } = "";

    /// <summary>
    /// STUN-mapped UDP address (IP:mappedPort) advertised in Glyphs.
    /// Ephemeral — not persisted. Empty until NAT discovery runs.
    /// </summary>
    [JsonIgnore]
    public string StunMappedAddress { get; set; } = "";

    /// <summary>
    /// The local listening port for peer connections.
    /// </summary>
    public int ListenPort { get; set; } = 5000;

    /// <summary>
    /// Protocol version this peer supports (from PeerProtocol.ProtocolVersion).
    /// </summary>
    public int ProtocolVersion { get; init; } = PeerProtocol.ProtocolVersion;

    /// <summary>
    /// Game major version (from PeerProtocol.GameVersionMajor).
    /// </summary>
    public int GameVersionMajor { get; init; } = PeerProtocol.GameVersionMajor;

    /// <summary>
    /// Game minor version (from PeerProtocol.GameVersionMinor).
    /// </summary>
    public int GameVersionMinor { get; init; } = PeerProtocol.GameVersionMinor;

    /// <summary>
    /// Game patch version (from PeerProtocol.GameVersionPatch).
    /// </summary>
    public int GameVersionPatch { get; init; } = PeerProtocol.GameVersionPatch;

    /// <summary>
    /// Full version string for display (e.g., "1.0.0").
    /// </summary>
    public string GameVersionString => $"{GameVersionMajor}.{GameVersionMinor}.{GameVersionPatch}";

    /// <summary>
    /// Timestamp when this identity was first created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Manages loading, creating, and persisting the local peer identity.
/// The identity file lives alongside the game executable.
/// </summary>
public static class PeerIdentityStore
{
    private const string IdentityFileName = "peer-identity.json";

    /// <summary>
    /// Load the peer identity from disk, or generate a new one if none exists.
    /// Uses a port-specific filename to support multiple instances on the same machine.
    /// </summary>
    /// <param name="displayName">The player's display name for this session.</param>
    /// <param name="listenPort">The port this peer will listen on.</param>
    /// <returns>The peer's persistent identity.</returns>
    public static PeerIdentity LoadOrCreate(string displayName, int listenPort)
    {
        // Port-specific identity file prevents collisions when running multiple instances
        var fileName = listenPort == 5000 ? IdentityFileName : $"peer-identity-{listenPort}.json";
        var filePath = Path.Combine(AppContext.BaseDirectory, fileName);

        PeerIdentity? identity = null;

        // Try to load existing identity
        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                identity = JsonSerializer.Deserialize(json, PeerIdentityJsonContext.Default.PeerIdentity);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[P2P:Identity] Failed to load identity: {ex.Message}. Generating new.");
            }
        }

        // Generate new identity if none exists or load failed
        if (identity == null)
        {
            identity = new PeerIdentity
            {
                PeerId = Guid.NewGuid().ToString("N")[..16], // 16-char hex ID
                CreatedAt = DateTime.UtcNow,
            };
            Console.WriteLine($"[P2P:Identity] Generated new peer ID: {identity.PeerId}");
        }

        // Update mutable fields for this session
        identity.DisplayName = displayName;
        identity.ListenPort = listenPort;

        // Persist to disk
        Save(identity, filePath);

        return identity;
    }

    /// <summary>
    /// Save the peer identity to disk.
    /// </summary>
    private static void Save(PeerIdentity identity, string filePath)
    {
        try
        {
            var json = JsonSerializer.Serialize(identity, PeerIdentityJsonContext.Default.PeerIdentity);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[P2P:Identity] Failed to save identity: {ex.Message}");
        }
    }
}

/// <summary>
/// AOT-compatible JSON serialization context for peer identity persistence.
/// </summary>
[JsonSerializable(typeof(PeerIdentity))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class PeerIdentityJsonContext : JsonSerializerContext { }
