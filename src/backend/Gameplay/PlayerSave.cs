// =============================================================================
// PlayerSave.cs — Encrypted Player Persistence
// =============================================================================
//
// OVERVIEW:
// Saves and loads player progress to an encrypted local file. The save contains
// all persistent player state: level, XP, abilities, equipment, inventory,
// currency, and last position.
//
// ENCRYPTION:
//   - AES-256-CBC encryption with PKCS7 padding
//   - Key derived via PBKDF2 (100,000 iterations) from peer ID + hardcoded salt
//   - HMAC-SHA256 appended for tamper detection
//   - IV is random per save (prepended to ciphertext)
//
// FILE FORMAT:
//   [1 byte: version] [32 bytes: HMAC] [16 bytes: IV] [N bytes: AES ciphertext]
//   The plaintext inside the ciphertext is UTF-8 JSON.
//
// SECURITY MODEL:
// This prevents CASUAL save editing (opening the file in a text editor and
// changing values). It does NOT prevent determined hackers who can reverse the
// binary to find the salt. For a P2P game without a central server, this is the
// best we can do without hardware-backed encryption.
//
// TAMPER DETECTION:
// HMAC covers the entire encrypted payload (IV + ciphertext). If any byte is
// modified, the HMAC won't match → save is rejected and reset to defaults.
//
// FUTURE EXPANSION:
// The version byte allows schema migration. When we add new fields, increment
// the version and add migration logic in LoadAsync. The save format is designed
// to be portable — a future "export" feature could re-encrypt with a different
// key (e.g., a user-chosen password) for transfer between machines.
//
// AUTO-SAVE:
// Saves every 60 seconds and on graceful shutdown. Crash recovery loses at most
// 60 seconds of progress (acceptable for a demo).
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carcosa.Server.Gameplay;

/// <summary>
/// The complete player save data. Serialized to JSON, then encrypted.
/// Version field enables future schema migrations.
/// </summary>
public sealed class PlayerSaveData
{
    /// <summary>Save format version. Increment when adding breaking changes.</summary>
    public int Version { get; set; } = 2;

    // --- Identity / first-run ---
    public string DisplayName { get; set; } = "";
    public bool HasCompletedFirstRun { get; set; }
    /// <summary>Cosmetic body id: a, b, or c.</summary>
    public string Figure { get; set; } = "b";

    // --- Settings ---
    public bool OfflineMode { get; set; }
    public float MasterVolume { get; set; } = 1f;
    public bool ShowGlyphOverlay { get; set; } = true;
    public bool ShowFps { get; set; }
    /// <summary>Local test tools: map reveal, click-to-travel. Never synced to peers.</summary>
    public bool DevMode { get; set; }
    /// <summary>Packed fog-of-war bitfield (4x4 chunks). Local knowledge only — not P2P.</summary>
    public string ExploredFogBase64 { get; set; } = "";

    // --- Progression ---
    public int Level { get; set; } = 1;
    public int XP { get; set; }
    public int PaleMarks { get; set; }

    // --- Abilities ---
    public string PrimaryAbility { get; set; } = "ember_spray";
    public string SecondaryAbility { get; set; } = "iron_veil";
    public List<string> UnlockedAbilityIds { get; set; } = new()
    {
        "ember_spray", "pale_blade", "void_bolt", "bone_cleaver", "hex_dart",
        "warding_light", "iron_veil", "shadow_step", "grim_howl", "cinder_ward", "soul_projection",
    };
    public List<string> UnlockedItemIds { get; set; } = new();

    // --- Equipment (slot → item ID, null = empty) ---
    public string? WeaponSlot { get; set; }
    public string? ArmorSlot { get; set; }
    public string? TrinketSlot { get; set; }
    public string? BootsSlot { get; set; }

    // --- Backpack (list of item ID + quantity, null = empty slot) ---
    public List<SaveInventorySlot?> Backpack { get; set; } = new();

    // --- Position (last known overworld position for resume) ---
    public float LastX { get; set; } = 320.5f;
    public float LastY { get; set; } = 544.5f;
    /// <summary>Safe overworld position to restore after dungeon logout.</summary>
    public float LastSafeOverworldX { get; set; } = 320.5f;
    public float LastSafeOverworldY { get; set; } = 544.5f;
    /// <summary>Map width this position was saved against. 0/200 = legacy greybox.</summary>
    public int WorldWidth { get; set; }
    public bool WasInDungeon { get; set; }

    // --- Timestamps ---
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSavedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A single backpack slot in the save file.</summary>
public sealed class SaveInventorySlot
{
    public required string ItemId { get; set; }
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// Manages saving and loading encrypted player data.
/// Uses AES-256-CBC with PBKDF2 key derivation and HMAC-SHA256 integrity.
/// </summary>
public sealed class SaveManager
{
    // =========================================================================
    // CONSTANTS
    // =========================================================================

    /// <summary>Current save format version.</summary>
    private const byte CurrentVersion = 2;

    /// <summary>
    /// Hardcoded salt for PBKDF2 key derivation. Combined with peer ID to
    /// produce a unique encryption key per player. Not secret (in the binary)
    /// but adds complexity for casual attackers.
    /// </summary>
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("carcosa_dim_shore_2024_pale_marks");

    /// <summary>PBKDF2 iteration count. 100k provides reasonable brute-force resistance.</summary>
    private const int KeyIterations = 100_000;

    /// <summary>AES key size in bytes (256-bit).</summary>
    private const int KeySizeBytes = 32;

    /// <summary>AES IV size in bytes (128-bit for CBC mode).</summary>
    private const int IvSizeBytes = 16;

    /// <summary>HMAC-SHA256 output size in bytes.</summary>
    private const int HmacSizeBytes = 32;

    /// <summary>Auto-save interval in seconds.</summary>
    private const int AutoSaveIntervalSeconds = 60;

    // =========================================================================
    // FIELDS
    // =========================================================================

    private readonly string _savePath;
    private readonly byte[] _encryptionKey;
    private readonly byte[] _hmacKey;
    private Timer? _autoSaveTimer;
    private PlayerSaveData _currentData = new();
    private readonly object _lock = new();

    // =========================================================================
    // CONSTRUCTOR
    // =========================================================================

    /// <summary>
    /// Create a SaveManager for the given peer ID. Derives encryption keys
    /// from the peer ID + salt.
    /// </summary>
    /// <param name="peerId">The local peer's unique ID (used for key derivation).</param>
    /// <param name="listenPort">Server port (for multi-instance support).</param>
    public SaveManager(string peerId, int listenPort)
    {
        // Save file path next to the executable
        var fileName = listenPort == 5000 ? "player-save.dat" : $"player-save-{listenPort}.dat";
        _savePath = Path.Combine(AppContext.BaseDirectory, fileName);

        // Derive encryption key and HMAC key from peer ID using PBKDF2
        // We derive 64 bytes total: first 32 for AES, second 32 for HMAC
        using var kdf = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(peerId),
            Salt,
            KeyIterations,
            HashAlgorithmName.SHA256);

        _encryptionKey = kdf.GetBytes(KeySizeBytes);
        _hmacKey = kdf.GetBytes(KeySizeBytes);
    }

    // =========================================================================
    // PUBLIC API
    // =========================================================================

    /// <summary>
    /// Load save data from disk. Returns defaults if file doesn't exist or is corrupted.
    /// </summary>
    public PlayerSaveData Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_savePath))
            {
                Console.WriteLine("[Save] No save file found — starting fresh.");
                _currentData = new PlayerSaveData();
                return _currentData;
            }

            try
            {
                var fileBytes = File.ReadAllBytes(_savePath);
                var data = Decrypt(fileBytes);
                if (data != null)
                {
                    _currentData = data;
                    Console.WriteLine($"[Save] Loaded: Level {data.Level}, {data.PaleMarks} Pale Marks, saved {data.LastSavedAt:u}");
                    return _currentData;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Save] Failed to load save: {ex.Message}. Resetting to defaults.");
            }

            _currentData = new PlayerSaveData();
            return _currentData;
        }
    }

    /// <summary>
    /// Save current data to disk (encrypted).
    /// </summary>
    public void Save(PlayerSaveData data)
    {
        lock (_lock)
        {
            _currentData = data;
            data.LastSavedAt = DateTime.UtcNow;

            try
            {
                var encrypted = Encrypt(data);
                File.WriteAllBytes(_savePath, encrypted);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Save] Failed to write save: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Start the auto-save timer (saves every 60 seconds).
    /// </summary>
    public void StartAutoSave(Func<PlayerSaveData> getCurrentData)
    {
        _autoSaveTimer = new Timer(_ =>
        {
            try
            {
                var data = getCurrentData();
                Save(data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Save] Auto-save error: {ex.Message}");
            }
        }, null, TimeSpan.FromSeconds(AutoSaveIntervalSeconds), TimeSpan.FromSeconds(AutoSaveIntervalSeconds));

        Console.WriteLine($"[Save] Auto-save enabled (every {AutoSaveIntervalSeconds}s).");
    }

    /// <summary>
    /// Stop auto-save and perform a final save.
    /// </summary>
    public void Shutdown(PlayerSaveData finalData)
    {
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = null;
        Save(finalData);
        Console.WriteLine("[Save] Final save complete on shutdown.");
    }

    /// <summary>Get the current save data reference.</summary>
    public PlayerSaveData CurrentData => _currentData;

    // =========================================================================
    // ENCRYPTION
    // =========================================================================

    /// <summary>
    /// Encrypt save data: JSON → AES-256-CBC → prepend IV → compute HMAC → prepend HMAC + version.
    /// </summary>
    private byte[] Encrypt(PlayerSaveData data)
    {
        // Serialize to JSON
        var json = JsonSerializer.Serialize(data, PlayerSaveJsonContext.Default.PlayerSaveData);
        var plaintext = Encoding.UTF8.GetBytes(json);

        // Generate random IV
        var iv = RandomNumberGenerator.GetBytes(IvSizeBytes);

        // Encrypt with AES-256-CBC
        byte[] ciphertext;
        using (var aes = Aes.Create())
        {
            aes.Key = _encryptionKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
        }

        // Build payload: IV + ciphertext
        var payload = new byte[IvSizeBytes + ciphertext.Length];
        iv.CopyTo(payload, 0);
        ciphertext.CopyTo(payload, IvSizeBytes);

        // Compute HMAC over payload
        byte[] hmac;
        using (var hmacAlg = new HMACSHA256(_hmacKey))
        {
            hmac = hmacAlg.ComputeHash(payload);
        }

        // Final file format: [version:1][hmac:32][payload:N]
        var output = new byte[1 + HmacSizeBytes + payload.Length];
        output[0] = CurrentVersion;
        hmac.CopyTo(output, 1);
        payload.CopyTo(output, 1 + HmacSizeBytes);

        return output;
    }

    /// <summary>
    /// Decrypt save data: verify version → verify HMAC → decrypt AES → parse JSON.
    /// Returns null if tampered, wrong version, or any decryption failure.
    /// </summary>
    private PlayerSaveData? Decrypt(byte[] fileBytes)
    {
        // Minimum size: version(1) + hmac(32) + iv(16) + at least 16 bytes ciphertext
        if (fileBytes.Length < 1 + HmacSizeBytes + IvSizeBytes + 16)
        {
            Console.WriteLine("[Save] File too small — corrupted.");
            return null;
        }

        // Check version
        byte version = fileBytes[0];
        if (version > CurrentVersion)
        {
            Console.WriteLine($"[Save] Unknown version {version} (current: {CurrentVersion}). Cannot load.");
            return null;
        }

        // Extract HMAC and payload
        var storedHmac = fileBytes.AsSpan(1, HmacSizeBytes).ToArray();
        var payload = fileBytes.AsSpan(1 + HmacSizeBytes).ToArray();

        // Verify HMAC (tamper detection)
        byte[] computedHmac;
        using (var hmacAlg = new HMACSHA256(_hmacKey))
        {
            computedHmac = hmacAlg.ComputeHash(payload);
        }

        if (!CryptographicOperations.FixedTimeEquals(storedHmac, computedHmac))
        {
            Console.WriteLine("[Save] HMAC verification failed — file has been tampered with!");
            return null;
        }

        // Extract IV and ciphertext from payload
        var iv = payload.AsSpan(0, IvSizeBytes).ToArray();
        var ciphertext = payload.AsSpan(IvSizeBytes).ToArray();

        // Decrypt
        byte[] plaintext;
        using (var aes = Aes.Create())
        {
            aes.Key = _encryptionKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            plaintext = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        }

        // Parse JSON
        var json = Encoding.UTF8.GetString(plaintext);
        var data = JsonSerializer.Deserialize(json, PlayerSaveJsonContext.Default.PlayerSaveData);

        return data;
    }
}

// =============================================================================
// AOT JSON Context for Player Save
// =============================================================================

[JsonSerializable(typeof(PlayerSaveData))]
[JsonSerializable(typeof(SaveInventorySlot))]
[JsonSerializable(typeof(List<SaveInventorySlot?>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
internal partial class PlayerSaveJsonContext : JsonSerializerContext { }
