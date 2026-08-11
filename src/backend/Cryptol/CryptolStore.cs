// =============================================================================
// CryptolStore.cs — Local Currency Persistence
// =============================================================================
//
// WHY LOCAL JSON:
// Cryptol is the in-game currency. For now it's stored in a simple JSON file
// next to the server executable. This is intentionally simple because:
//   1. No external database dependency (single-exe distribution)
//   2. Easy to inspect/debug (human-readable JSON)
//   3. Will be replaced by the matchmaking service in a future iteration
//
// WHY NOT SQLite/Redis:
// Adding a database dependency would complicate the AOT build (SQLite native
// binaries, Redis client libraries). A JSON file is sufficient for the current
// peer-hosted model where one server handles one game at a time.
//
// THREAD SAFETY:
// The store uses a lock for reads/writes since it can be called from the game
// loop thread (on game end) and potentially from HTTP endpoints (for balance queries).
//
// FILE FORMAT:
// {
//   "players": {
//     "playerId1": { "balance": 1000 },
//     "playerId2": { "balance": 500 }
//   }
// }
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carcosa.Server.Cryptol;

/// <summary>
/// AOT-compatible source-generated JSON context for Cryptol data types.
/// Required because the CryptolStore reads/writes JSON files and must work
/// under Native AOT where reflection-based serialization is not available.
/// </summary>
[JsonSerializable(typeof(CryptolData))]
[JsonSerializable(typeof(Dictionary<string, PlayerCryptolInfo>))]
[JsonSerializable(typeof(PlayerCryptolInfo))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class CryptolJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Persists player Cryptol balances to a local JSON file.
/// Simple key-value store: playerId → balance.
/// Will be replaced by the centralized matchmaking service in a future iteration.
/// </summary>
public sealed class CryptolStore
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private CryptolData _data;

    public CryptolStore(string? filePath = null)
    {
        // Default: store in same directory as the executable
        _filePath = filePath ?? Path.Combine(AppContext.BaseDirectory, "cryptol.json");
        _data = Load();
    }

    /// <summary>
    /// Get the current Cryptol balance for a player. Returns 0 if player not found.
    /// </summary>
    public int GetBalance(string playerId)
    {
        lock (_lock)
        {
            return _data.Players.TryGetValue(playerId, out var info) ? info.Balance : 0;
        }
    }

    /// <summary>
    /// Award Cryptol to a player. Creates the player entry if it doesn't exist.
    /// Returns the new balance.
    /// </summary>
    public int AwardCryptol(string playerId, int amount)
    {
        lock (_lock)
        {
            if (!_data.Players.TryGetValue(playerId, out var info))
            {
                info = new PlayerCryptolInfo { Balance = 0 };
                _data.Players[playerId] = info;
            }

            info.Balance += amount;
            Save();
            return info.Balance;
        }
    }

    /// <summary>
    /// Award Cryptol to multiple players at once (e.g., end-of-game rewards).
    /// More efficient than calling AwardCryptol individually (single file write).
    /// </summary>
    public void AwardCryptolBatch(IEnumerable<string> playerIds, int amount)
    {
        lock (_lock)
        {
            foreach (var playerId in playerIds)
            {
                if (!_data.Players.TryGetValue(playerId, out var info))
                {
                    info = new PlayerCryptolInfo { Balance = 0 };
                    _data.Players[playerId] = info;
                }
                info.Balance += amount;
            }
            Save();
        }
    }

    private CryptolData Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize(json, CryptolJsonContext.Default.CryptolData) ?? new CryptolData();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Cryptol] Failed to load {_filePath}: {ex.Message}");
        }
        return new CryptolData();
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_data, CryptolJsonContext.Default.CryptolData);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Cryptol] Failed to save {_filePath}: {ex.Message}");
        }
    }
}

/// <summary>
/// Root data structure for the cryptol.json file.
/// </summary>
public sealed class CryptolData
{
    public Dictionary<string, PlayerCryptolInfo> Players { get; set; } = new();
}

/// <summary>
/// Per-player Cryptol information.
/// </summary>
public sealed class PlayerCryptolInfo
{
    public int Balance { get; set; }
}
