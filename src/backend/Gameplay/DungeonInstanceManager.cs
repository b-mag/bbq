// =============================================================================
// DungeonInstanceManager.cs — Mesh-native dungeon instances (no matchmaking)
// =============================================================================
//
// OVERVIEW:
// Party leader (or solo player) starts a dungeon on the mesh: roll a seed,
// elect a host, broadcast dungeon_start, generate the same map on every peer.
// Members who are not leader wait for dungeon_start. Completing broadcasts
// dungeon_complete and returns everyone to the overworld.
// =============================================================================

using Carcosa.Server.Game;
using Carcosa.Server.P2P;

namespace Carcosa.Server.Gameplay;

/// <summary>
/// Coordinates entering and leaving mesh dungeon instances.
/// </summary>
public sealed class DungeonInstanceManager
{
    public const int CaveWidth = 60;
    public const int CaveHeight = 50;

    private readonly PeerMesh _mesh;
    private readonly PeerIdentity _localIdentity;
    private readonly OverworldCombatSync _combat;
    private readonly MeshPartyManager _party;
    private readonly MetricsCollector _metrics;
    private readonly object _lock = new();

    private ActiveDungeon? _active;

    public DungeonInstanceManager(
        PeerMesh mesh,
        PeerIdentity localIdentity,
        OverworldCombatSync combat,
        MeshPartyManager party,
        MetricsCollector metrics)
    {
        _mesh = mesh;
        _localIdentity = localIdentity;
        _combat = combat;
        _party = party;
        _metrics = metrics;
        _mesh.OnPeerMessage += HandlePeerMessage;
    }

    /// <summary>Generated map for the active instance, if any.</summary>
    public TileMap? ActiveMap
    {
        get { lock (_lock) return _active?.Map; }
    }

    /// <summary>REST snapshot of the current dungeon instance, or null if none.</summary>
    public DungeonInstanceSnapshot? GetActiveInstance()
    {
        lock (_lock)
        {
            if (_active == null) return null;
            return new DungeonInstanceSnapshot(
                _active.InstanceId,
                _active.Seed,
                _active.Scenario,
                _active.HostPeerId,
                _active.AvgLevel,
                _active.Phase,
                _active.HostPeerId == _localIdentity.PeerId);
        }
    }

    /// <summary>
    /// Start a dungeon as solo or party leader. Non-leaders in a party wait
    /// for dungeon_start from the host and return false.
    /// </summary>
    public async Task<bool> EnterDungeonAsync(string scenario, float entranceX, float entranceY)
    {
        lock (_lock)
        {
            if (_active != null) return false;
        }

        var members = GetPartyMemberIds();
        var inParty = members.Count > 1;
        if (inParty && !_party.IsLeader)
            return false; // wait for dungeon_start

        var seed = Random.Shared.Next();
        var avgLevel = ComputeAvgLevel(members);
        var hostPeerId = inParty
            ? ElectHost(members)
            : _localIdentity.PeerId;
        var instanceId = Guid.NewGuid().ToString("N")[..12];
        var scenarioKey = ToScenarioWireName(ParseScenario(scenario));
        var partyIds = members.Count > 0
            ? members.ToArray()
            : [_localIdentity.PeerId];

        var payload = new PeerDungeonStartPayload
        {
            InstanceId = instanceId,
            HostPeerId = hostPeerId,
            Scenario = scenarioKey,
            Seed = seed,
            AvgLevel = avgLevel,
            PartyMemberIds = partyIds,
            EntranceX = entranceX,
            EntranceY = entranceY,
        };

        ApplyDungeonStart(payload, isLocalOrigin: true);

        await _mesh.BroadcastAsync(new PeerMessage
        {
            Type = PeerMessageTypes.DungeonStart,
            DungeonStart = payload,
        });

        return true;
    }

    /// <summary>End the active dungeon, notify the mesh, and return to overworld.</summary>
    public async Task CompleteDungeonAsync(bool victory = true, int xpBonus = 0)
    {
        string? instanceId;
        lock (_lock)
        {
            instanceId = _active?.InstanceId;
            if (instanceId == null) return;
        }

        await _mesh.BroadcastAsync(new PeerMessage
        {
            Type = PeerMessageTypes.DungeonComplete,
            DungeonComplete = new PeerDungeonCompletePayload
            {
                InstanceId = instanceId,
                Victory = victory,
                XpBonus = xpBonus,
            },
        });

        ApplyDungeonComplete(instanceId);
    }

    private void HandlePeerMessage(PeerConnection connection, PeerMessage message)
    {
        if (message.Type == PeerMessageTypes.DungeonStart && message.DungeonStart != null)
        {
            ApplyDungeonStart(message.DungeonStart, isLocalOrigin: false);
            return;
        }

        if (message.Type == PeerMessageTypes.DungeonComplete && message.DungeonComplete != null)
        {
            ApplyDungeonComplete(message.DungeonComplete.InstanceId);
        }
    }

    private void ApplyDungeonStart(PeerDungeonStartPayload payload, bool isLocalOrigin)
    {
        var map = GenerateMap(payload.Scenario, payload.Seed);

        lock (_lock)
        {
            _active = new ActiveDungeon
            {
                InstanceId = payload.InstanceId,
                HostPeerId = payload.HostPeerId,
                Scenario = payload.Scenario,
                Seed = payload.Seed,
                AvgLevel = payload.AvgLevel,
                Phase = "playing",
                Map = map,
                EntranceX = payload.EntranceX,
                EntranceY = payload.EntranceY,
            };
        }

        _combat.MarkEnteredDungeon(payload.EntranceX, payload.EntranceY);
        Console.WriteLine(
            $"[Dungeon] {(isLocalOrigin ? "Started" : "Joined")} {payload.Scenario} " +
            $"instance={payload.InstanceId} seed={payload.Seed} host={payload.HostPeerId}");
    }

    private void ApplyDungeonComplete(string instanceId)
    {
        lock (_lock)
        {
            if (_active == null) return;
            if (!string.Equals(_active.InstanceId, instanceId, StringComparison.Ordinal))
                return;
            _active = null;
        }

        _combat.MarkLeftDungeon();
        Console.WriteLine($"[Dungeon] Left instance {instanceId}");
    }

    private static TileMap GenerateMap(string scenario, int seed)
    {
        return ParseScenario(scenario) switch
        {
            MapScenario.MountainCave => MapGenerator.GenerateCave(CaveWidth, CaveHeight, seed),
            MapScenario.PallidSanctum => MapGenerator.GenerateTemple(100, 100, seed),
            _ => MapGenerator.Generate(80, 60, seed),
        };
    }

    private int ComputeAvgLevel(IReadOnlyList<string> memberIds)
    {
        var localLevel = Math.Max(1, _combat.LocalPlayer.Level);
        if (memberIds.Count == 0)
            return localLevel;

        var sum = 0;
        foreach (var _ in memberIds)
            sum += localLevel; // remote levels default to local until synced
        return sum / memberIds.Count;
    }

    private string ElectHost(IReadOnlyList<string> partyMemberIds)
    {
        var connected = new HashSet<string>(_mesh.ConnectedPeerIds, StringComparer.Ordinal)
        {
            _localIdentity.PeerId
        };

        var candidates = partyMemberIds.Where(connected.Contains).ToList();
        if (candidates.Count == 0)
            candidates.Add(_localIdentity.PeerId);

        try
        {
            _metrics.UpdateLocalMetrics();
            var remote = _metrics.GetRemoteMetrics();

            string? bestId = null;
            var bestScore = float.MinValue;

            foreach (var id in candidates)
            {
                PeerMetrics? metrics = null;
                if (id == _localIdentity.PeerId)
                    metrics = _metrics.LocalMetrics;
                else if (remote.TryGetValue(id, out var remoteMetrics))
                    metrics = remoteMetrics;

                if (metrics == null)
                    continue;

                var score = MetricsCalculator.FromMetrics(metrics).CalculateScore();
                if (bestId == null
                    || score > bestScore
                    || (Math.Abs(score - bestScore) < 0.0001f
                        && string.CompareOrdinal(id, bestId) < 0))
                {
                    bestScore = score;
                    bestId = id;
                }
            }

            if (bestId != null)
                return bestId;
        }
        catch (Exception)
        {
            // CalculateScore unavailable or metrics incomplete — PeerId fallback.
        }

        return candidates.OrderBy(id => id, StringComparer.Ordinal).First();
    }

    private List<string> GetPartyMemberIds()
    {
        var members = _party.MemberPeerIds;
        if (!_party.IsInParty || members.Count <= 1)
            return [_localIdentity.PeerId];

        if (!members.Contains(_localIdentity.PeerId))
        {
            var withLocal = members.ToList();
            withLocal.Add(_localIdentity.PeerId);
            return withLocal;
        }

        return members.ToList();
    }

    private static MapScenario ParseScenario(string scenario)
    {
        return (scenario ?? "").Trim().ToLowerInvariant().Replace("-", "_") switch
        {
            "mountain_cave" or "mountaincave" or "cave" => MapScenario.MountainCave,
            "pallid_sanctum" or "pallidsanctum" or "temple" => MapScenario.PallidSanctum,
            "hollow" => MapScenario.Hollow,
            "drowned_dock" or "drowneddock" or "warehouse" => MapScenario.DrownedDock,
            _ => MapScenario.MountainCave,
        };
    }

    private static string ToScenarioWireName(MapScenario scenario) => scenario switch
    {
        MapScenario.MountainCave => "mountain_cave",
        MapScenario.PallidSanctum => "pallid_sanctum",
        MapScenario.Hollow => "hollow",
        _ => "drowned_dock",
    };

    private sealed class ActiveDungeon
    {
        public required string InstanceId { get; init; }
        public required string HostPeerId { get; init; }
        public required string Scenario { get; init; }
        public int Seed { get; init; }
        public int AvgLevel { get; init; }
        public required string Phase { get; init; }
        public TileMap? Map { get; init; }
        public float EntranceX { get; init; }
        public float EntranceY { get; init; }
    }
}

/// <summary>REST snapshot of the active mesh dungeon instance.</summary>
public sealed record DungeonInstanceSnapshot(
    string InstanceId,
    int Seed,
    string Scenario,
    string HostPeerId,
    int AvgLevel,
    string Phase,
    bool IsLocalHost);
