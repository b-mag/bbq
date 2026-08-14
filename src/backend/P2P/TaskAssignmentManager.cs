namespace Carcosa.Server.P2P;

/// <summary>
/// Known distributed task types for Phase 2 scheduling.
/// </summary>
public static class TaskTypes
{
    public const string ShardHost = "shard_host";
    public const string EnemyAi = "enemy_ai";
    public const string LootBroadcast = "loot_broadcast";
}

/// <summary>
/// A task assignment record for distributed scheduling.
/// </summary>
public sealed record TaskAssignment(
    string TaskId,
    string TaskType,
    string AssignedPeerId,
    long AssignedAtTick);

/// <summary>
/// Minimal deterministic task assignment foundation.
/// Phase 1 keeps shard host election in ShardHostManager; this prepares Phase 2.
/// </summary>
public sealed class TaskAssignmentManager
{
    private readonly PeerMesh _mesh;
    private readonly PeerIdentity _localIdentity;
    private readonly Dictionary<string, TaskAssignment> _assignments = new();
    private readonly object _lock = new();

    public TaskAssignmentManager(PeerMesh mesh, PeerIdentity localIdentity)
    {
        _mesh = mesh;
        _localIdentity = localIdentity;
    }

    /// <summary>
    /// Assign a task to the lowest peer ID (deterministic, no negotiation).
    /// </summary>
    public TaskAssignment AssignTask(string taskId, string taskType, IEnumerable<string> candidatePeerIds, long currentTick)
    {
        var assignedPeerId = candidatePeerIds
            .OrderBy(id => id, StringComparer.Ordinal)
            .FirstOrDefault() ?? _localIdentity.PeerId;

        var assignment = new TaskAssignment(taskId, taskType, assignedPeerId, currentTick);

        lock (_lock)
        {
            _assignments[taskId] = assignment;
        }

        return assignment;
    }

    public bool IsAssignedToLocal(string taskId)
    {
        lock (_lock)
        {
            return _assignments.TryGetValue(taskId, out var assignment)
                   && assignment.AssignedPeerId == _localIdentity.PeerId;
        }
    }

    public TaskAssignment? GetAssignment(string taskId)
    {
        lock (_lock)
        {
            return _assignments.TryGetValue(taskId, out var assignment) ? assignment : null;
        }
    }

    public IEnumerable<string> GetAllPeerIds()
    {
        yield return _localIdentity.PeerId;
        foreach (var peerId in _mesh.ConnectedPeerIds)
            yield return peerId;
    }
}
