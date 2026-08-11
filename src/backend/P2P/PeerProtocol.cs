// =============================================================================
// PeerProtocol.cs — P2P Protocol Constants & Version Compatibility
// =============================================================================
//
// OVERVIEW:
// Defines the versioning scheme for the Carcosa P2P mesh protocol. Every peer
// must agree on the protocol to communicate. This file is the single source of
// truth for version numbers and compatibility rules.
//
// VERSIONING STRATEGY:
// We use a two-level versioning approach:
//
//   1. PROTOCOL VERSION (integer) — The wire format version. Incremented only
//      when the message structure changes incompatibly (fields renamed, types
//      changed, messages removed). Two peers must have the same protocol version
//      to communicate at all.
//
//   2. GAME VERSION (semver: major.minor.patch) — The game content version.
//      - Major: Breaking gameplay changes (new physics, tile format changes).
//        Peers with different major versions CANNOT connect (different simulations).
//      - Minor: New content (enemies, items, dungeon types). Compatible within
//        same major — older peers ignore unknown fields gracefully.
//      - Patch: Bug fixes. Always compatible.
//
// COMPATIBILITY RULE:
//   ProtocolVersion must match exactly.
//   GameVersion.Major must match.
//   GameVersion.Minor and Patch can differ (forward/backward compatible).
//
// EXAMPLE:
//   Peer A (protocol=1, game=1.3.2) ↔ Peer B (protocol=1, game=1.5.0) → OK
//   Peer A (protocol=1, game=1.3.2) ↔ Peer C (protocol=1, game=2.0.0) → REJECTED
//   Peer A (protocol=1, game=1.3.2) ↔ Peer D (protocol=2, game=1.3.2) → REJECTED
//
// WHY NOT JUST USE SEMVER ALONE:
// The protocol version is separate because the wire format can stay stable across
// many game releases. A new enemy type doesn't change how messages are framed.
// Conversely, a protocol optimization (e.g., switching from JSON to binary) would
// increment the protocol version without changing the game version.
// =============================================================================

namespace Carcosa.Server.P2P;

/// <summary>
/// Central definition of protocol and game versioning constants.
/// Used during peer handshake to determine compatibility.
/// </summary>
public static class PeerProtocol
{
    // =========================================================================
    // PROTOCOL VERSION
    // =========================================================================

    /// <summary>
    /// The P2P wire protocol version. Increment this ONLY when the message
    /// envelope format changes in an incompatible way.
    /// 
    /// History:
    ///   1 — Initial P2P mesh protocol (JSON over WebSocket)
    /// </summary>
    public const int ProtocolVersion = 1;

    // =========================================================================
    // GAME VERSION
    // =========================================================================

    /// <summary>
    /// Major game version. Peers with different majors cannot connect.
    /// Increment when: simulation physics change, tile format changes,
    /// entity system restructured, or any change that makes game states
    /// diverge between versions.
    /// </summary>
    public const int GameVersionMajor = 1;

    /// <summary>
    /// Minor game version. Peers with different minors CAN connect.
    /// Increment when: new content added (enemies, items, scenarios),
    /// new message types added (older peers ignore unknown types).
    /// </summary>
    public const int GameVersionMinor = 0;

    /// <summary>
    /// Patch version. Always compatible. Bug fixes only.
    /// </summary>
    public const int GameVersionPatch = 0;

    /// <summary>
    /// Full game version string for display purposes.
    /// </summary>
    public static string GameVersionString =>
        $"{GameVersionMajor}.{GameVersionMinor}.{GameVersionPatch}";

    // =========================================================================
    // COMPATIBILITY CHECKING
    // =========================================================================

    /// <summary>
    /// Determine if a remote peer's version is compatible with ours.
    /// Both protocol version and game major version must match.
    /// </summary>
    /// <param name="remoteProtocol">The remote peer's protocol version.</param>
    /// <param name="remoteGameMajor">The remote peer's game major version.</param>
    /// <returns>True if versions are compatible and connection should proceed.</returns>
    public static bool IsCompatible(int remoteProtocol, int remoteGameMajor)
    {
        return remoteProtocol == ProtocolVersion && remoteGameMajor == GameVersionMajor;
    }

    /// <summary>
    /// Get a human-readable reason why a version is incompatible.
    /// Returns null if versions ARE compatible.
    /// </summary>
    public static string? GetIncompatibilityReason(int remoteProtocol, int remoteGameMajor)
    {
        if (remoteProtocol != ProtocolVersion)
        {
            return $"Protocol version mismatch (local: {ProtocolVersion}, remote: {remoteProtocol}). " +
                   "Both peers must use the same protocol version.";
        }

        if (remoteGameMajor != GameVersionMajor)
        {
            return $"Game major version mismatch (local: {GameVersionMajor}, remote: {remoteGameMajor}). " +
                   "Major version must match for peers to connect.";
        }

        return null; // Compatible
    }

    // =========================================================================
    // PROTOCOL CONSTANTS
    // =========================================================================

    /// <summary>
    /// Maximum number of peers in a single world shard.
    /// When this limit is reached, new peers are redirected to a new shard.
    /// </summary>
    public const int MaxPeersPerWorld = 100;

    /// <summary>
    /// How often peers exchange their known peer lists (Peer Exchange / PEX).
    /// </summary>
    public const int PeerExchangeIntervalSeconds = 30;

    /// <summary>
    /// How long to wait for a handshake response before disconnecting.
    /// </summary>
    public const int HandshakeTimeoutMs = 5000;

    /// <summary>
    /// How often to send a keepalive ping to connected peers.
    /// </summary>
    public const int KeepaliveIntervalSeconds = 10;

    /// <summary>
    /// After this many seconds without a keepalive response, consider peer dead.
    /// </summary>
    public const int PeerTimeoutSeconds = 30;

    /// <summary>
    /// Maximum player movement speed (tiles per second).
    /// Used by anti-cheat validation to detect speed hacks.
    /// Set generously above the actual speed (4.5) to account for network
    /// jitter, clock drift, and batched position updates.
    /// </summary>
    public const float MaxPlayerSpeed = 12.0f;

    /// <summary>
    /// Maximum allowed position jump in a single update (tiles).
    /// Larger jumps indicate teleport hacking.
    /// </summary>
    public const float MaxPositionJump = 8.0f;
}
