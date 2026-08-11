// =============================================================================
// PlayerStore.cs — Player Registration and Cryptol Balance Management
// =============================================================================
//
// Stores player data in a local JSON file (players.json).
// Each player has a generated UUID as their persistent ID and a Cryptol balance.
// In the future, player IDs will be linked to Steam accounts.
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carcosa.Matchmaking.Services;

/// <summary>
/// Player information stored by the matchmaking service.
/// </summary>
public sealed class PlayerInfo
{
    public required string Id { get; init; }
    public int Balance { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Manages player registration and Cryptol balances.
/// Persists to a JSON file for simplicity (will be replaced by a database later).
/// </summary>
public sealed class PlayerStore
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private Dictionary<string, PlayerInfo> _players;

    public PlayerStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(AppContext.BaseDirectory, "players.json");
        _players = Load();
    }

    /// <summary>
    /// Register a new player and return their generated info.
    /// </summary>
    public PlayerInfo RegisterPlayer()
    {
        lock (_lock)
        {
            var id = Guid.NewGuid().ToString("N")[..12]; // 12-char hex ID
            var player = new PlayerInfo
            {
                Id = id,
                Balance = 0,
                RegisteredAt = DateTime.UtcNow
            };
            _players[id] = player;
            Save();
            Console.WriteLine($"[Players] Registered new player: {id}");
            return player;
        }
    }

    /// <summary>
    /// Get player info by ID. Returns null if not found.
    /// </summary>
    public PlayerInfo? GetPlayer(string id)
    {
        lock (_lock)
        {
            return _players.GetValueOrDefault(id);
        }
    }

    /// <summary>
    /// Get all registered players (for dashboard display).
    /// </summary>
    public List<PlayerInfo> GetAllPlayers()
    {
        lock (_lock)
        {
            return _players.Values.ToList();
        }
    }

    /// <summary>
    /// Update a player's Cryptol balance (add or subtract).
    /// Returns updated player info, or null if player not found.
    /// </summary>
    public PlayerInfo? UpdateCryptol(string id, int amount)
    {
        lock (_lock)
        {
            if (!_players.TryGetValue(id, out var player)) return null;
            player.Balance += amount;
            if (player.Balance < 0) player.Balance = 0; // Can't go negative
            Save();
            return player;
        }
    }

    private Dictionary<string, PlayerInfo> Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize(json, PlayerStoreJsonContext.Default.DictionaryStringPlayerInfo)
                    ?? new Dictionary<string, PlayerInfo>();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Players] Failed to load: {ex.Message}");
        }
        return new Dictionary<string, PlayerInfo>();
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_players, PlayerStoreJsonContext.Default.DictionaryStringPlayerInfo);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Players] Failed to save: {ex.Message}");
        }
    }
}

[JsonSerializable(typeof(Dictionary<string, PlayerInfo>))]
[JsonSerializable(typeof(PlayerInfo))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class PlayerStoreJsonContext : JsonSerializerContext { }
