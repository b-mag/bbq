// =============================================================================
// OverworldMapStore.cs — Overworld Map Persistence (JSON File)
// =============================================================================
//
// Handles saving and loading the persistent overworld map to/from a JSON file.
// On first boot, generates the map using OverworldGenerator and saves it.
// On subsequent boots, loads the existing map from disk.
//
// The JSON file is human-editable: you can move landmarks, add dungeon entrances,
// reposition world objects, etc. Changes take effect on next server restart.
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carcosa.Matchmaking.Overworld;

/// <summary>
/// Manages persistence of the overworld map to a JSON file.
/// Thread-safe for concurrent reads (map is immutable after load).
/// </summary>
public sealed class OverworldMapStore
{
    private readonly string _filePath;
    private OverworldMap? _map;

    public OverworldMapStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(AppContext.BaseDirectory, "overworld.json");
    }

    /// <summary>
    /// Get the current overworld map. Loads from disk or generates if needed.
    /// </summary>
    public OverworldMap GetMap()
    {
        if (_map != null) return _map;

        if (File.Exists(_filePath))
        {
            _map = Load();
            if (_map != null)
            {
                Console.WriteLine($"[Overworld] Loaded map from {_filePath} ({_map.Width}x{_map.Height}, seed {_map.Seed})");
                return _map;
            }
        }

        // Generate new map
        Console.WriteLine("[Overworld] No existing map found. Generating new overworld...");
        _map = OverworldGenerator.Generate();
        Save(_map);
        Console.WriteLine($"[Overworld] Generated and saved map (seed {_map.Seed}) to {_filePath}");
        return _map;
    }

    /// <summary>
    /// Force regeneration of the overworld map with a new seed.
    /// </summary>
    public OverworldMap Regenerate(int? seed = null)
    {
        _map = OverworldGenerator.Generate(seed: seed);
        Save(_map);
        Console.WriteLine($"[Overworld] Regenerated map (seed {_map.Seed})");
        return _map;
    }

    /// <summary>
    /// Reload the map from disk (picks up manual JSON edits).
    /// </summary>
    public OverworldMap? Reload()
    {
        var loaded = Load();
        if (loaded != null)
        {
            _map = loaded;
            Console.WriteLine("[Overworld] Reloaded map from disk");
        }
        return _map;
    }

    private OverworldMap? Load()
    {
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize(json, OverworldMapJsonContext.Default.OverworldMap);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Overworld] Failed to load map: {ex.Message}");
            return null;
        }
    }

    private void Save(OverworldMap map)
    {
        try
        {
            var json = JsonSerializer.Serialize(map, OverworldMapJsonContext.Default.OverworldMap);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Overworld] Failed to save map: {ex.Message}");
        }
    }
}
