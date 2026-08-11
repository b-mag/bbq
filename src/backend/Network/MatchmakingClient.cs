// =============================================================================
// MatchmakingClient.cs — Client for the Centralized Matchmaking Service
// =============================================================================
//
// PURPOSE:
// Connects the game server to the matchmaking service for online features:
//   1. Detects if the matchmaking service is reachable (online mode)
//   2. Publishes session heartbeats every N seconds so other players can find us
//   3. Queries available sessions for the "Join a Game" feature
//
// OFFLINE GRACEFUL DEGRADATION:
// If the matchmaking service is unreachable, the game works in standalone mode.
// The IsOnline property is false, the frontend hides the "Join a Game" button,
// and no heartbeats are sent. The game is fully playable without matchmaking.
//
// HEARTBEAT LIFECYCLE:
// Started after the game server is running. Publishes every 10s (configurable).
// Stops when the server shuts down or the session ends.
// =============================================================================

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carcosa.Server.Network;

/// <summary>
/// Configuration for the matchmaking integration, loaded from appsettings.json.
/// </summary>
public sealed class MatchmakingConfig
{
    public string Url { get; set; } = "http://localhost:5100";
    public bool Enabled { get; set; } = true;
    public int HeartbeatIntervalSeconds { get; set; } = 10;
}

/// <summary>
/// Session info returned by the matchmaking service.
/// </summary>
public sealed class AvailableSession
{
    public string SessionId { get; set; } = "";
    public string HostAddress { get; set; } = "";
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; }
    public string State { get; set; } = "";
    public string Scenario { get; set; } = "";
    public int CurrentWave { get; set; }
}

/// <summary>
/// Client that communicates with the centralized matchmaking service.
/// Handles online detection, heartbeat publishing, and session discovery.
/// </summary>
public sealed class MatchmakingClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly MatchmakingConfig _config;
    private readonly string _serverName;
    private readonly int _port;
    private CancellationTokenSource? _heartbeatCts;
    private Task? _heartbeatTask;

    /// <summary>True if the matchmaking service was reachable on last check.</summary>
    public bool IsOnline { get; private set; }

    /// <summary>Timestamp of last successful matchmaking contact.</summary>
    public DateTime LastContact { get; private set; }

    public MatchmakingClient(MatchmakingConfig config, string serverName, int port)
    {
        _config = config;
        _serverName = serverName;
        _port = port;
        _http = new HttpClient
        {
            BaseAddress = new Uri(config.Url),
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    /// <summary>
    /// Check if the matchmaking service is reachable. Updates IsOnline.
    /// Call this on startup to determine if online features should be shown.
    /// </summary>
    public async Task<bool> CheckOnlineAsync()
    {
        if (!_config.Enabled)
        {
            IsOnline = false;
            return false;
        }

        try
        {
            var response = await _http.GetAsync("/api/health");
            IsOnline = response.IsSuccessStatusCode;
            if (IsOnline) LastContact = DateTime.UtcNow;
            Console.WriteLine($"[Matchmaking] Service {(IsOnline ? "ONLINE" : "OFFLINE")} at {_config.Url}");
        }
        catch
        {
            IsOnline = false;
            Console.WriteLine($"[Matchmaking] Service UNREACHABLE at {_config.Url} (standalone mode)");
        }

        return IsOnline;
    }

    /// <summary>
    /// Start publishing session heartbeats to the matchmaking service.
    /// Call this after the game server is ready to accept connections.
    /// </summary>
    public void StartHeartbeat(Func<SessionHeartbeatData> getSessionData)
    {
        if (!IsOnline || _heartbeatCts != null) return;

        _heartbeatCts = new CancellationTokenSource();
        var token = _heartbeatCts.Token;

        _heartbeatTask = Task.Run(async () =>
        {
            Console.WriteLine($"[Matchmaking] Heartbeat started (every {_config.HeartbeatIntervalSeconds}s)");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var data = getSessionData();
                    await _http.PostAsJsonAsync("/api/sessions/heartbeat", data, MatchmakingClientJson.Default.SessionHeartbeatData, token);
                    LastContact = DateTime.UtcNow;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Silently handle — heartbeat failures are non-critical
                    Console.WriteLine($"[Matchmaking] Heartbeat failed: {ex.Message}");
                }

                try { await Task.Delay(_config.HeartbeatIntervalSeconds * 1000, token); }
                catch (OperationCanceledException) { break; }
            }
            Console.WriteLine("[Matchmaking] Heartbeat stopped");
        });
    }

    /// <summary>
    /// Stop publishing heartbeats (called on shutdown or game end).
    /// </summary>
    public void StopHeartbeat()
    {
        _heartbeatCts?.Cancel();
        _heartbeatCts?.Dispose();
        _heartbeatCts = null;
    }

    /// <summary>
    /// Get available sessions from the matchmaking service.
    /// Returns sessions in lobby state (preferred) and playing state.
    /// </summary>
    public async Task<List<AvailableSession>> GetAvailableSessionsAsync()
    {
        if (!IsOnline) return new List<AvailableSession>();

        try
        {
            var response = await _http.GetAsync("/api/sessions");
            if (response.IsSuccessStatusCode)
            {
                var sessions = await response.Content.ReadFromJsonAsync(
                    MatchmakingClientJson.Default.ListAvailableSession);
                return sessions ?? new List<AvailableSession>();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Matchmaking] Failed to fetch sessions: {ex.Message}");
        }

        return new List<AvailableSession>();
    }

    public void Dispose()
    {
        StopHeartbeat();
        _http.Dispose();
    }
}

/// <summary>
/// Data published as a heartbeat to the matchmaking service.
/// Tells the matchmaking service about our current session state.
/// </summary>
public sealed class SessionHeartbeatData
{
    public string SessionId { get; set; } = "";
    public string HostAddress { get; set; } = "";
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; } = 8;
    public string State { get; set; } = "lobby";
    public string Scenario { get; set; } = "warehouse";
    public int CurrentWave { get; set; }
}

/// <summary>
/// AOT-compatible JSON context for matchmaking client types.
/// </summary>
[JsonSerializable(typeof(SessionHeartbeatData))]
[JsonSerializable(typeof(List<AvailableSession>))]
[JsonSerializable(typeof(AvailableSession))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class MatchmakingClientJson : JsonSerializerContext { }
