// =============================================================================
// SessionRegistry.cs — Active Game Session Tracking
// =============================================================================
//
// Tracks which game servers are currently active and accepting connections.
// Sessions are registered via heartbeat messages (either Kafka or REST).
// Sessions are considered stale and removed if no heartbeat arrives within 30 seconds.
//
// This enables:
//   - Players browsing available games to join
//   - Invaders finding an active session to invade
//   - The frontend showing a "Join Game" option without knowing IPs
// =============================================================================

namespace Carcosa.Matchmaking.Services;

/// <summary>
/// A heartbeat message from an active game server.
/// Published every 10 seconds by running game servers.
/// </summary>
public sealed class SessionHeartbeat
{
    /// <summary>Unique session identifier.</summary>
    public required string SessionId { get; init; }
    /// <summary>Host address (IP:port) that players can connect to.</summary>
    public required string HostAddress { get; init; }
    /// <summary>Current number of connected players.</summary>
    public int PlayerCount { get; set; }
    /// <summary>Maximum players allowed.</summary>
    public int MaxPlayers { get; set; } = 8;
    /// <summary>Current game state: "lobby", "playing", "game_over".</summary>
    public string State { get; set; } = "lobby";
    /// <summary>Selected scenario: "warehouse" or "temple".</summary>
    public string Scenario { get; set; } = "warehouse";
    /// <summary>Current wave number (0 if in lobby).</summary>
    public int CurrentWave { get; set; }
    /// <summary>Server timestamp when this heartbeat was sent.</summary>
    public long Timestamp { get; set; }
}

/// <summary>
/// In-memory registry of active game sessions.
/// Sessions expire after 30 seconds without a heartbeat.
/// Thread-safe via lock (low contention — few sessions expected).
/// </summary>
public sealed class SessionRegistry
{
    private const int StaleTimeoutSeconds = 30;
    private readonly Dictionary<string, SessionHeartbeat> _sessions = new();
    private readonly object _lock = new();

    /// <summary>
    /// Update or register a session from a heartbeat message.
    /// </summary>
    public void UpdateSession(SessionHeartbeat heartbeat)
    {
        lock (_lock)
        {
            heartbeat.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _sessions[heartbeat.SessionId] = heartbeat;
        }
    }

    /// <summary>
    /// Get all active (non-stale) sessions.
    /// Removes stale entries opportunistically.
    /// </summary>
    public List<SessionHeartbeat> GetActiveSessions()
    {
        lock (_lock)
        {
            PruneStale();
            return _sessions.Values.ToList();
        }
    }

    /// <summary>
    /// Get the best session for an invader to join.
    /// Prefers sessions that are "playing" with fewer players (more room, more targets).
    /// Returns null if no suitable session exists.
    /// </summary>
    public SessionHeartbeat? GetBestSession()
    {
        lock (_lock)
        {
            PruneStale();
            return _sessions.Values
                .Where(s => s.State == "playing" && s.PlayerCount < s.MaxPlayers)
                .OrderBy(s => s.PlayerCount) // Prefer less-full games
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// Remove a session (e.g., when game ends).
    /// </summary>
    public void RemoveSession(string sessionId)
    {
        lock (_lock)
        {
            _sessions.Remove(sessionId);
        }
    }

    private void PruneStale()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var staleThreshold = now - (StaleTimeoutSeconds * 1000);
        var staleKeys = _sessions
            .Where(kv => kv.Value.Timestamp < staleThreshold)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in staleKeys)
        {
            _sessions.Remove(key);
            Console.WriteLine($"[Sessions] Pruned stale session: {key}");
        }
    }
}
