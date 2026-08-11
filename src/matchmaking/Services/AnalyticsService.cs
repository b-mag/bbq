// =============================================================================
// AnalyticsService.cs — Game Analytics and Insights
// =============================================================================
//
// Tracks aggregate statistics about gameplay: class picks, match outcomes,
// scenario preferences, Cryptol economy, and player activity.
// Persists to analytics.json for simplicity. Data is appended via API calls
// from game servers when matches end.
//
// In a production system this would feed into a time-series database (InfluxDB,
// Prometheus) or analytics platform (Mixpanel, Amplitude). For learning purposes,
// we keep it as a simple in-memory + JSON file store.
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carcosa.Matchmaking.Services;

/// <summary>
/// Aggregate analytics data returned by the /api/analytics endpoint.
/// </summary>
public sealed class AnalyticsData
{
    public int TotalPlayers { get; set; }
    public int TotalMatches { get; set; }
    public ClassDistribution ClassDistribution { get; set; } = new();
    public ScenarioDistribution ScenarioDistribution { get; set; } = new();
    public float AverageWaveReached { get; set; }
    public int TotalCryptolAwarded { get; set; }
    public float WinRate { get; set; }
    public float InvaderJoinRate { get; set; }
    public int PeakPlayersToday { get; set; }
    public int MatchesLast24h { get; set; }
}

public sealed class ClassDistribution
{
    public int Gangster { get; set; }
    public int Detective { get; set; }
    public int Surgeon { get; set; }
}

public sealed class ScenarioDistribution
{
    public int Warehouse { get; set; }
    public int Temple { get; set; }
}

/// <summary>
/// Match result reported by game servers when a match ends.
/// </summary>
public sealed class MatchResult
{
    public string Scenario { get; set; } = "warehouse";
    public bool Victory { get; set; }
    public int WaveReached { get; set; }
    public int PlayerCount { get; set; }
    public bool HadInvader { get; set; }
    public int CryptolAwarded { get; set; }
    public List<string> ClassesPlayed { get; set; } = new();
    public long Timestamp { get; set; }
}

/// <summary>
/// Tracks gameplay analytics. Game servers report match results; this service
/// aggregates them into queryable statistics for the admin dashboard.
/// </summary>
public sealed class AnalyticsService
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private List<MatchResult> _matchHistory;
    private int _peakPlayersToday;
    private DateTime _peakResetDate = DateTime.UtcNow.Date;

    public AnalyticsService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(AppContext.BaseDirectory, "analytics.json");
        _matchHistory = Load();
    }

    /// <summary>
    /// Record a completed match result (called by game servers via REST).
    /// </summary>
    public void RecordMatch(MatchResult result)
    {
        lock (_lock)
        {
            result.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _matchHistory.Add(result);

            // Keep last 1000 matches in history
            if (_matchHistory.Count > 1000)
                _matchHistory = _matchHistory.Skip(_matchHistory.Count - 1000).ToList();

            Save();
            Console.WriteLine($"[Analytics] Recorded match: {result.Scenario}, wave {result.WaveReached}, {(result.Victory ? "WIN" : "LOSS")}");
        }
    }

    /// <summary>
    /// Update peak player count (called when sessions update).
    /// </summary>
    public void UpdatePeakPlayers(int currentOnline)
    {
        // Reset peak counter daily
        if (DateTime.UtcNow.Date != _peakResetDate)
        {
            _peakResetDate = DateTime.UtcNow.Date;
            _peakPlayersToday = 0;
        }
        if (currentOnline > _peakPlayersToday)
            _peakPlayersToday = currentOnline;
    }

    /// <summary>
    /// Get aggregated analytics data for the dashboard.
    /// </summary>
    public AnalyticsData GetAnalytics(int totalPlayers)
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var last24h = now - 86400000; // 24 hours in ms

            var recentMatches = _matchHistory.Where(m => m.Timestamp > last24h).ToList();
            var allMatches = _matchHistory;

            // Class distribution (all time)
            var classDist = new ClassDistribution();
            foreach (var match in allMatches)
            {
                foreach (var cls in match.ClassesPlayed)
                {
                    switch (cls)
                    {
                        case "gangster": classDist.Gangster++; break;
                        case "detective": classDist.Detective++; break;
                        case "surgeon": classDist.Surgeon++; break;
                    }
                }
            }

            // Scenario distribution
            var scenarioDist = new ScenarioDistribution
            {
                Warehouse = allMatches.Count(m => m.Scenario == "warehouse"),
                Temple = allMatches.Count(m => m.Scenario == "temple"),
            };

            // Win rate (Warehouse only — Temple has no victory)
            var warehouseMatches = allMatches.Where(m => m.Scenario == "warehouse").ToList();
            var winRate = warehouseMatches.Count > 0
                ? (float)warehouseMatches.Count(m => m.Victory) / warehouseMatches.Count
                : 0f;

            // Average wave reached
            var avgWave = allMatches.Count > 0
                ? (float)allMatches.Sum(m => m.WaveReached) / allMatches.Count
                : 0f;

            // Invader rate
            var invaderRate = allMatches.Count > 0
                ? (float)allMatches.Count(m => m.HadInvader) / allMatches.Count
                : 0f;

            return new AnalyticsData
            {
                TotalPlayers = totalPlayers,
                TotalMatches = allMatches.Count,
                ClassDistribution = classDist,
                ScenarioDistribution = scenarioDist,
                AverageWaveReached = avgWave,
                TotalCryptolAwarded = allMatches.Sum(m => m.CryptolAwarded),
                WinRate = winRate,
                InvaderJoinRate = invaderRate,
                PeakPlayersToday = _peakPlayersToday,
                MatchesLast24h = recentMatches.Count,
            };
        }
    }

    private List<MatchResult> Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize(json, AnalyticsJsonContext.Default.ListMatchResult)
                    ?? new List<MatchResult>();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Analytics] Failed to load: {ex.Message}");
        }
        return new List<MatchResult>();
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_matchHistory, AnalyticsJsonContext.Default.ListMatchResult);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Analytics] Failed to save: {ex.Message}");
        }
    }
}

[JsonSerializable(typeof(List<MatchResult>))]
[JsonSerializable(typeof(MatchResult))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class AnalyticsJsonContext : JsonSerializerContext { }
