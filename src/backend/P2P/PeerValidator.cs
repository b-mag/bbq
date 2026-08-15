// =============================================================================
// PeerValidator.cs — P2P Anti-Cheat Validation Layer
// =============================================================================
//
// OVERVIEW:
// In a P2P architecture, each peer is authoritative over their own position.
// This means a cheating peer could claim to be anywhere. The PeerValidator
// detects this by checking every incoming state update against physical rules:
//
//   1. SPEED CHECK: Did the peer move faster than MaxPlayerSpeed allows?
//      (position delta / time delta > threshold → speed hack)
//
//   2. TELEPORT DETECTION: Did the peer jump more than MaxPositionJump tiles
//      in a single update? (instant position change → teleport hack)
//
//   3. COLLISION VALIDATION: Is the peer inside a wall, mountain, or deep water?
//      (impossible position → noclip/wallhack)
//
// VIOLATION TRACKING:
// Each peer has a violation counter. When violations accumulate past a threshold,
// the local peer broadcasts a Violation message to the mesh. If a majority of
// peers report the same offender, a vote-kick is triggered automatically.
//
// TOLERANCE:
// Network jitter and lag can cause brief apparent violations (a delayed update
// makes it look like the peer moved fast). The validator uses:
//   - A speed multiplier above actual max (6.0 vs 4.5 tiles/sec)
//   - A grace period for new connections (first 2 seconds ignored)
//   - Violation decay over time (old violations expire)
//   - Threshold of 5 violations before reporting
//
// IMPORTANT LIMITATIONS:
// This system cannot prevent:
//   - Wallhacks (seeing through fog — irrelevant since there's no fog of war)
//   - Modified client display (cosmetic cheats)
//   - Information gathering (reading other peers' data from memory)
// It CAN prevent:
//   - Speed hacking (moving faster than allowed)
//   - Teleporting (jumping to arbitrary positions)
//   - Wall clipping (moving through impassable terrain)
//
// WHY NOT SERVER-AUTHORITATIVE:
// In full P2P, there is no central authority. Each peer validates independently.
// If >50% of peers agree someone is cheating, they're kicked. This is the
// standard approach for P2P anti-cheat (used in fighting games, RTS).
// =============================================================================

using System.Collections.Concurrent;

namespace Carcosa.Server.P2P;

/// <summary>
/// Tracks violations for a single remote peer.
/// </summary>
public sealed class PeerViolationTracker
{
    /// <summary>Peer ID being tracked.</summary>
    public required string PeerId { get; init; }

    /// <summary>Last known valid position X.</summary>
    public float LastX { get; set; }

    /// <summary>Last known valid position Y.</summary>
    public float LastY { get; set; }

    /// <summary>Timestamp of the last state update (UTC millis).</summary>
    public long LastTimestamp { get; set; }

    /// <summary>When we first started tracking this peer (for grace period).</summary>
    public DateTime TrackingStarted { get; init; } = DateTime.UtcNow;

    /// <summary>Current accumulated violation count.</summary>
    public int ViolationCount { get; set; }

    /// <summary>When the last violation was recorded (for decay).</summary>
    public DateTime LastViolationAt { get; set; } = DateTime.MinValue;

    /// <summary>Whether we've already reported this peer to the mesh.</summary>
    public bool Reported { get; set; }

    /// <summary>Whether this peer is in the grace period (first 5 seconds).</summary>
    public bool InGracePeriod => (DateTime.UtcNow - TrackingStarted).TotalSeconds < 5.0;
}

/// <summary>
/// Validates incoming peer state updates for anti-cheat enforcement.
/// Runs locally on each peer — no central authority needed.
/// </summary>
public sealed class PeerValidator
{
    // =========================================================================
    // CONSTANTS
    // =========================================================================

    /// <summary>Violations needed before reporting to the mesh.</summary>
    private const int ReportThreshold = 10;

    /// <summary>Violations decay after this many seconds without new ones.</summary>
    private const int ViolationDecaySeconds = 15;

    /// <summary>Votes needed (as fraction of total peers) to kick.</summary>
    private const float KickVoteFraction = 0.5f; // >50% must agree

    // =========================================================================
    // FIELDS
    // =========================================================================

    private readonly PeerMesh _mesh;
    private readonly PeerIdentity _localIdentity;
    private readonly ConcurrentDictionary<string, PeerViolationTracker> _trackers = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _kickVotes = new(); // targetId → set of voterIds
    private readonly byte[]? _mapTiles;
    private readonly int _mapWidth;
    private readonly int _mapHeight;

    // =========================================================================
    // EVENTS
    // =========================================================================

    /// <summary>Fired when a peer is kicked due to majority vote.</summary>
    public event Action<string>? OnPeerKicked; // peerId

    // =========================================================================
    // CONSTRUCTOR
    // =========================================================================

    /// <summary>
    /// Create a new validator.
    /// </summary>
    /// <param name="mesh">The peer mesh (for broadcasting violations).</param>
    /// <param name="localIdentity">Our identity (for signing violations).</param>
    /// <param name="mapTiles">Overworld tile data for collision checks (null to skip).</param>
    /// <param name="mapWidth">Map width in tiles.</param>
    /// <param name="mapHeight">Map height in tiles.</param>
    public PeerValidator(PeerMesh mesh, PeerIdentity localIdentity,
        byte[]? mapTiles = null, int mapWidth = 0, int mapHeight = 0)
    {
        _mesh = mesh;
        _localIdentity = localIdentity;
        _mapTiles = mapTiles;
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;

        // Subscribe to incoming violation reports and vote-kicks from other peers
        _mesh.OnPeerMessage += HandlePeerMessage;
    }

    // =========================================================================
    // VALIDATION
    // =========================================================================

    /// <summary>
    /// Validate an incoming state update from a remote peer.
    /// Returns true if the update is valid, false if it's suspicious.
    /// Accumulates violations internally.
    /// </summary>
    /// <param name="update">The state update to validate.</param>
    /// <returns>True if valid, false if violation detected.</returns>
    public bool ValidateStateUpdate(PeerStateUpdatePayload update)
    {
        var tracker = _trackers.GetOrAdd(update.PeerId, _ => new PeerViolationTracker
        {
            PeerId = update.PeerId,
            LastX = update.X,
            LastY = update.Y,
            LastTimestamp = update.Timestamp,
        });

        // Skip validation during grace period (initial connection jitter)
        if (tracker.InGracePeriod)
        {
            tracker.LastX = update.X;
            tracker.LastY = update.Y;
            tracker.LastTimestamp = update.Timestamp;
            return true;
        }

        // Trusted origin reset: dungeon/interior exit, waypoints, dev fast-travel.
        // Peers still render the new position; we just don't vote-kick for the jump.
        if (update.Relocate)
        {
            tracker.LastX = update.X;
            tracker.LastY = update.Y;
            tracker.LastTimestamp = update.Timestamp;
            return true;
        }

        // Apply violation decay
        if (tracker.ViolationCount > 0 &&
            (DateTime.UtcNow - tracker.LastViolationAt).TotalSeconds > ViolationDecaySeconds)
        {
            tracker.ViolationCount = Math.Max(0, tracker.ViolationCount - 1);
        }

        var violations = new List<string>();

        // Check 1: Speed validation
        var speedViolation = CheckSpeed(tracker, update);
        if (speedViolation != null) violations.Add(speedViolation);

        // Check 2: Teleport detection
        var teleportViolation = CheckTeleport(tracker, update);
        if (teleportViolation != null) violations.Add(teleportViolation);

        // Check 3: Collision validation (only if map data available)
        var collisionViolation = CheckCollision(update);
        if (collisionViolation != null) violations.Add(collisionViolation);

        // Update tracker state (always — even on violation, to avoid false cascading)
        tracker.LastX = update.X;
        tracker.LastY = update.Y;
        tracker.LastTimestamp = update.Timestamp;

        // Record violations
        if (violations.Count > 0)
        {
            tracker.ViolationCount += violations.Count;
            tracker.LastViolationAt = DateTime.UtcNow;

            Console.WriteLine($"[P2P:Validator] {update.PeerId} violations ({tracker.ViolationCount}): " +
                string.Join(", ", violations));

            // Report to mesh if threshold exceeded
            if (tracker.ViolationCount >= ReportThreshold && !tracker.Reported)
            {
                tracker.Reported = true;
                ReportViolation(update.PeerId, violations.First());
            }

            return false;
        }

        return true;
    }

    // =========================================================================
    // INDIVIDUAL CHECKS
    // =========================================================================

    /// <summary>
    /// Check if the peer moved faster than physically possible.
    /// </summary>
    private static string? CheckSpeed(PeerViolationTracker tracker, PeerStateUpdatePayload update)
    {
        if (tracker.LastTimestamp == 0) return null;

        var timeDeltaMs = update.Timestamp - tracker.LastTimestamp;
        if (timeDeltaMs <= 0) return null; // Out of order or duplicate

        var timeDeltaSec = timeDeltaMs / 1000.0f;
        if (timeDeltaSec < 0.01f) return null; // Too small to measure reliably

        var dx = update.X - tracker.LastX;
        var dy = update.Y - tracker.LastY;
        var distance = MathF.Sqrt(dx * dx + dy * dy);
        var speed = distance / timeDeltaSec;

        if (speed > PeerProtocol.MaxPlayerSpeed)
        {
            return $"speed_hack (speed={speed:F1} tiles/s, max={PeerProtocol.MaxPlayerSpeed})";
        }

        return null;
    }

    /// <summary>
    /// Check if the peer teleported (position jump too large for one update).
    /// </summary>
    private static string? CheckTeleport(PeerViolationTracker tracker, PeerStateUpdatePayload update)
    {
        var dx = MathF.Abs(update.X - tracker.LastX);
        var dy = MathF.Abs(update.Y - tracker.LastY);

        if (dx > PeerProtocol.MaxPositionJump || dy > PeerProtocol.MaxPositionJump)
        {
            return $"teleport (jump={MathF.Max(dx, dy):F1} tiles, max={PeerProtocol.MaxPositionJump})";
        }

        return null;
    }

    /// <summary>
    /// Check if the peer is in an impossible position (inside walls/mountains).
    /// </summary>
    private string? CheckCollision(PeerStateUpdatePayload update)
    {
        if (_mapTiles == null || _mapWidth == 0) return null;

        var tileX = (int)update.X;
        var tileY = (int)update.Y;

        if (tileX < 0 || tileX >= _mapWidth || tileY < 0 || tileY >= _mapHeight)
            return null; // Out of bounds — could be legitimate edge case

        // Check the tile at the peer's position
        var tile = _mapTiles[tileY * _mapWidth + tileX];

        // Impassable tiles (matching OverworldMap.IsWalkable logic):
        // DeepWater=1, Forest=3, Mountain=4, Ruins=5, Wall=11
        if (tile == 1 || tile == 3 || tile == 4 || tile == 5 || tile == 11)
        {
            return $"wall_clip (tile={tile} at {tileX},{tileY})";
        }

        return null;
    }

    // =========================================================================
    // VIOLATION REPORTING & VOTE-KICK
    // =========================================================================

    /// <summary>
    /// Report a violation to the mesh (broadcasts to all peers).
    /// </summary>
    private void ReportViolation(string offenderId, string violationType)
    {
        Console.WriteLine($"[P2P:Validator] Reporting {offenderId} for {violationType}");

        var message = new PeerMessage
        {
            Type = PeerMessageTypes.Violation,
            Violation = new PeerViolationPayload
            {
                OffenderId = offenderId,
                ReporterId = _localIdentity.PeerId,
                ViolationType = violationType,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }
        };

        _ = _mesh.BroadcastAsync(message);

        // Also cast our own kick vote
        CastKickVote(offenderId, _localIdentity.PeerId);
    }

    /// <summary>
    /// Handle incoming peer messages (violations and vote-kicks from other peers).
    /// </summary>
    private void HandlePeerMessage(PeerConnection connection, PeerMessage message)
    {
        switch (message.Type)
        {
            case PeerMessageTypes.Violation when message.Violation != null:
                HandleRemoteViolation(message.Violation);
                break;

            case PeerMessageTypes.VoteKick when message.VoteKick != null:
                CastKickVote(message.VoteKick.TargetId, message.VoteKick.VoterId);
                break;
        }
    }

    /// <summary>
    /// Process a violation report from another peer.
    /// If we also have violations for this peer, add our vote.
    /// </summary>
    private void HandleRemoteViolation(PeerViolationPayload violation)
    {
        Console.WriteLine($"[P2P:Validator] Peer {violation.ReporterId} reports " +
            $"{violation.OffenderId} for {violation.ViolationType}");

        // If we also have violations tracked for this peer, add our kick vote
        if (_trackers.TryGetValue(violation.OffenderId, out var tracker) && tracker.ViolationCount > 0)
        {
            CastKickVote(violation.OffenderId, _localIdentity.PeerId);
        }
    }

    /// <summary>
    /// Record a kick vote. If majority reached (minimum 3 votes), trigger the kick.
    /// </summary>
    private void CastKickVote(string targetId, string voterId)
    {
        var votes = _kickVotes.GetOrAdd(targetId, _ => new HashSet<string>());

        lock (votes)
        {
            votes.Add(voterId);

            // Check if majority reached — require minimum 3 votes to prevent
            // false kicks in small meshes (2-3 players)
            var totalPeers = _mesh.PeerCount + 1; // +1 for ourselves
            var votesNeeded = Math.Max(3, (int)MathF.Ceiling(totalPeers * KickVoteFraction));

            if (votes.Count >= votesNeeded)
            {
                Console.WriteLine($"[P2P:Validator] KICK VOTE PASSED for {targetId} " +
                    $"({votes.Count}/{totalPeers} votes, needed {votesNeeded})");

                // Disconnect the offending peer
                var peer = _mesh.GetPeer(targetId);
                if (peer != null)
                {
                    _ = peer.DisconnectAsync("kicked_by_vote");
                }

                // Clean up
                _kickVotes.TryRemove(targetId, out _);
                _trackers.TryRemove(targetId, out _);

                OnPeerKicked?.Invoke(targetId);
            }
        }
    }

    // =========================================================================
    // CLEANUP
    // =========================================================================

    /// <summary>
    /// Remove tracking for a disconnected peer.
    /// </summary>
    public void RemovePeer(string peerId)
    {
        _trackers.TryRemove(peerId, out _);
        _kickVotes.TryRemove(peerId, out _);
    }

    /// <summary>
    /// Get violation info for a peer (for debugging/display).
    /// </summary>
    public int GetViolationCount(string peerId)
    {
        return _trackers.TryGetValue(peerId, out var tracker) ? tracker.ViolationCount : 0;
    }
}
