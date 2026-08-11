// =============================================================================
// OverworldConnectionManager.cs — WebSocket Hub for the Shared Overworld
// =============================================================================
//
// Similar architecture to the dungeon ConnectionManager but for the persistent
// overworld. Key differences:
//   - No max player cap (overworld supports many concurrent players)
//   - Players can be in different states (exploring, in_party, in_dungeon)
//   - Broadcast uses spatial awareness (nearby messages only to nearby players)
// =============================================================================

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Carcosa.Matchmaking.Overworld;

/// <summary>
/// Manages all WebSocket connections to the overworld server.
/// Thread-safe for concurrent access from the game loop and request threads.
/// </summary>
public sealed class OverworldConnectionManager
{
    private readonly ConcurrentDictionary<string, OverworldClientConnection> _connections = new();

    public int ConnectionCount => _connections.Count;

    public event Action<string, string>? OnPlayerConnected;    // playerId, playerName
    public event Action<string>? OnPlayerDisconnected;          // playerId
    public event Action<string, OverworldMessage>? OnMessageReceived; // playerId, message

    public bool TryAddConnection(string playerId, string playerName, WebSocket webSocket)
    {
        var connection = new OverworldClientConnection(playerId, playerName, webSocket);
        if (_connections.TryAdd(playerId, connection))
        {
            OnPlayerConnected?.Invoke(playerId, playerName);
            return true;
        }
        return false;
    }

    public void RemoveConnection(string playerId)
    {
        if (_connections.TryRemove(playerId, out _))
        {
            OnPlayerDisconnected?.Invoke(playerId);
        }
    }

    /// <summary>
    /// Handle incoming messages from a client until disconnect.
    /// </summary>
    public async Task HandleConnectionAsync(string playerId, CancellationToken ct)
    {
        if (!_connections.TryGetValue(playerId, out var connection))
            return;

        var buffer = new byte[4096];

        try
        {
            while (connection.WebSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await connection.WebSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await connection.WebSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure, null, ct);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var message = JsonSerializer.Deserialize(json,
                        OverworldJsonContext.Default.OverworldMessage);

                    if (message != null)
                    {
                        OnMessageReceived?.Invoke(playerId, message);
                    }
                }
            }
        }
        catch (WebSocketException) { }
        catch (OperationCanceledException) { }
        finally
        {
            RemoveConnection(playerId);
        }
    }

    /// <summary>Send a message to a specific player.</summary>
    public async Task SendToAsync(string playerId, OverworldMessage message, CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(playerId, out var connection)) return;
        if (connection.WebSocket.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(message, OverworldJsonContext.Default.OverworldMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        try
        {
            await connection.SendSemaphore.WaitAsync(ct);
            try
            {
                await connection.WebSocket.SendAsync(
                    new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            }
            finally
            {
                connection.SendSemaphore.Release();
            }
        }
        catch (WebSocketException)
        {
            RemoveConnection(playerId);
        }
    }

    /// <summary>Broadcast a message to all connected players.</summary>
    public async Task BroadcastAsync(OverworldMessage message, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message, OverworldJsonContext.Default.OverworldMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        var tasks = new List<Task>();
        foreach (var (_, connection) in _connections)
        {
            if (connection.WebSocket.State == WebSocketState.Open)
            {
                tasks.Add(SendBytesAsync(connection, bytes, ct));
            }
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>Broadcast to all except one player (typically the sender).</summary>
    public async Task BroadcastExceptAsync(string excludeId, OverworldMessage message, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message, OverworldJsonContext.Default.OverworldMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        var tasks = new List<Task>();
        foreach (var (playerId, connection) in _connections)
        {
            if (playerId != excludeId && connection.WebSocket.State == WebSocketState.Open)
            {
                tasks.Add(SendBytesAsync(connection, bytes, ct));
            }
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>Send a message to a set of specific players (e.g., party members).</summary>
    public async Task SendToMultipleAsync(IEnumerable<string> playerIds, OverworldMessage message, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message, OverworldJsonContext.Default.OverworldMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        var tasks = new List<Task>();
        foreach (var playerId in playerIds)
        {
            if (_connections.TryGetValue(playerId, out var connection) &&
                connection.WebSocket.State == WebSocketState.Open)
            {
                tasks.Add(SendBytesAsync(connection, bytes, ct));
            }
        }
        await Task.WhenAll(tasks);
    }

    public IEnumerable<string> GetConnectedPlayerIds() => _connections.Keys;

    public OverworldClientConnection? GetConnection(string playerId)
    {
        _connections.TryGetValue(playerId, out var conn);
        return conn;
    }

    private static async Task SendBytesAsync(OverworldClientConnection connection, byte[] bytes, CancellationToken ct)
    {
        try
        {
            await connection.SendSemaphore.WaitAsync(ct);
            try
            {
                await connection.WebSocket.SendAsync(
                    new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            }
            finally
            {
                connection.SendSemaphore.Release();
            }
        }
        catch (WebSocketException) { }
    }
}

/// <summary>
/// Represents a single connected overworld client.
/// </summary>
public sealed class OverworldClientConnection
{
    public string PlayerId { get; }
    public string PlayerName { get; }
    public WebSocket WebSocket { get; }
    public SemaphoreSlim SendSemaphore { get; } = new(1, 1);
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;

    public OverworldClientConnection(string playerId, string playerName, WebSocket webSocket)
    {
        PlayerId = playerId;
        PlayerName = playerName;
        WebSocket = webSocket;
    }
}
