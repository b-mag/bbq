// =============================================================================
// MeshPartyManager.cs — Party invites/joins on the mesh (no matchmaking)
// =============================================================================

namespace Carcosa.Server.P2P;

/// <summary>
/// Leader-authoritative party membership synced via party_update on /ws/peer.
/// </summary>
public sealed class MeshPartyManager
{
    private readonly PeerMesh _mesh;
    private readonly PeerIdentity _localIdentity;
    private readonly OverworldSync _overworldSync;
    private readonly object _lock = new();

    private string? _partyId;
    private string? _leaderPeerId;
    private readonly HashSet<string> _memberPeerIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pendingInvites = new(StringComparer.Ordinal); // target → inviter

    public const int MaxPartySize = 8;

    public MeshPartyManager(PeerMesh mesh, PeerIdentity localIdentity, OverworldSync overworldSync)
    {
        _mesh = mesh;
        _localIdentity = localIdentity;
        _overworldSync = overworldSync;
        _mesh.OnPeerMessage += HandlePeerMessage;
    }

    public bool IsInParty
    {
        get { lock (_lock) return _partyId != null && _memberPeerIds.Count > 0; }
    }

    public bool IsLeader
    {
        get { lock (_lock) return _leaderPeerId == _localIdentity.PeerId; }
    }

    public string? PartyId
    {
        get { lock (_lock) return _partyId; }
    }

    public string? LeaderPeerId
    {
        get { lock (_lock) return _leaderPeerId; }
    }

    public IReadOnlyList<string> MemberPeerIds
    {
        get { lock (_lock) return _memberPeerIds.ToList(); }
    }

    /// <summary>Party members for a peer, or null if solo / unknown.</summary>
    public IReadOnlyList<string>? GetPartyMembersForPeer(string? peerId)
    {
        if (string.IsNullOrEmpty(peerId)) return null;
        lock (_lock)
        {
            if (_partyId == null || !_memberPeerIds.Contains(peerId))
                return null;
            if (_memberPeerIds.Count <= 1)
                return null;
            return _memberPeerIds.ToList();
        }
    }

    public bool Invite(string targetPeerId)
    {
        if (string.IsNullOrWhiteSpace(targetPeerId) || targetPeerId == _localIdentity.PeerId)
            return false;

        lock (_lock)
        {
            if (_partyId != null && _leaderPeerId != _localIdentity.PeerId)
                return false; // only leader invites

            if (_partyId == null)
            {
                _partyId = Guid.NewGuid().ToString("N")[..12];
                _leaderPeerId = _localIdentity.PeerId;
                _memberPeerIds.Clear();
                _memberPeerIds.Add(_localIdentity.PeerId);
            }

            if (_memberPeerIds.Count >= MaxPartySize)
                return false;

            _pendingInvites[targetPeerId] = _localIdentity.PeerId;
        }

        _ = BroadcastPartyAsync("invite", targetPeerId);
        return true;
    }

    public bool AcceptInvite(string fromPeerId)
    {
        lock (_lock)
        {
            if (!_pendingInvites.TryGetValue(_localIdentity.PeerId, out var inviter) &&
                !_pendingInvites.ContainsValue(fromPeerId))
            {
                // Accept even if we only saw the invite message locally
            }
            _pendingInvites.Remove(_localIdentity.PeerId);
        }

        _ = BroadcastPartyAsync("accept", fromPeerId);
        return true;
    }

    public void Leave()
    {
        string? partyId;
        lock (_lock)
        {
            partyId = _partyId;
            if (partyId == null) return;
        }

        _ = BroadcastPartyAsync("leave", null);
        ApplyLocalLeave();
    }

    public MeshPartySnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new MeshPartySnapshot(
                _partyId,
                _leaderPeerId,
                _memberPeerIds.ToArray(),
                _pendingInvites.Keys.ToArray());
        }
    }

    private void ApplyLocalLeave()
    {
        lock (_lock)
        {
            _partyId = null;
            _leaderPeerId = null;
            _memberPeerIds.Clear();
        }
        _overworldSync.UpdateLocalStatus("exploring", null, false);
    }

    private async Task BroadcastPartyAsync(string action, string? targetPeerId)
    {
        PeerPartyUpdatePayload payload;
        lock (_lock)
        {
            payload = new PeerPartyUpdatePayload
            {
                PartyId = _partyId ?? "",
                LeaderId = _leaderPeerId ?? _localIdentity.PeerId,
                MemberIds = _memberPeerIds.ToArray(),
                MemberNames = Array.Empty<string>(),
                Action = action,
                TargetPeerId = targetPeerId,
                SenderPeerId = _localIdentity.PeerId,
            };
        }

        var msg = new PeerMessage
        {
            Type = PeerMessageTypes.PartyUpdate,
            PartyUpdate = payload,
        };
        await _mesh.BroadcastAsync(msg);

        UpdateLocalStatusFromState();
    }

    private void UpdateLocalStatusFromState()
    {
        lock (_lock)
        {
            if (_partyId != null && _memberPeerIds.Contains(_localIdentity.PeerId))
            {
                _overworldSync.UpdateLocalStatus(
                    "in_party",
                    _partyId,
                    _leaderPeerId == _localIdentity.PeerId);
            }
            else
            {
                _overworldSync.UpdateLocalStatus("exploring", null, false);
            }
        }
    }

    private void HandlePeerMessage(PeerConnection connection, PeerMessage message)
    {
        if (message.Type != PeerMessageTypes.PartyUpdate || message.PartyUpdate == null)
            return;

        var p = message.PartyUpdate;
        switch (p.Action)
        {
            case "invite":
                if (p.TargetPeerId == _localIdentity.PeerId)
                {
                    lock (_lock)
                        _pendingInvites[_localIdentity.PeerId] = p.SenderPeerId ?? p.LeaderPeerId;
                    Console.WriteLine($"[Party] Invite from {p.SenderPeerId ?? p.LeaderPeerId}");
                }
                break;

            case "accept":
                lock (_lock)
                {
                    if (_partyId == null || _leaderPeerId != _localIdentity.PeerId)
                    {
                        // Joining someone else's party
                        if (p.SenderPeerId == _localIdentity.PeerId || p.TargetPeerId == _localIdentity.PeerId
                            || p.MemberPeerIds.Contains(_localIdentity.PeerId))
                        {
                            _partyId = p.PartyId;
                            _leaderPeerId = p.LeaderPeerId;
                            _memberPeerIds.Clear();
                            foreach (var id in p.MemberPeerIds)
                                _memberPeerIds.Add(id);
                            _memberPeerIds.Add(_localIdentity.PeerId);
                        }
                    }
                    else
                    {
                        // We are leader — add acceptor
                        var joiner = p.SenderPeerId ?? p.TargetPeerId;
                        if (!string.IsNullOrEmpty(joiner) && _memberPeerIds.Count < MaxPartySize)
                            _memberPeerIds.Add(joiner);
                    }
                }
                _ = BroadcastPartyAsync("sync", null);
                break;

            case "sync":
            case "update":
                lock (_lock)
                {
                    if (string.IsNullOrEmpty(p.PartyId)) break;
                    if (p.MemberPeerIds.Contains(_localIdentity.PeerId) || _partyId == p.PartyId)
                    {
                        _partyId = p.PartyId;
                        _leaderPeerId = p.LeaderPeerId;
                        _memberPeerIds.Clear();
                        foreach (var id in p.MemberPeerIds)
                            _memberPeerIds.Add(id);
                    }
                }
                UpdateLocalStatusFromState();
                break;

            case "leave":
                lock (_lock)
                {
                    var leaver = p.SenderPeerId;
                    if (leaver == _localIdentity.PeerId)
                    {
                        ApplyLocalLeave();
                        return;
                    }
                    if (_partyId == p.PartyId && leaver != null)
                        _memberPeerIds.Remove(leaver);

                    if (_memberPeerIds.Count <= 1)
                    {
                        ApplyLocalLeave();
                        return;
                    }

                    if (_leaderPeerId == leaver)
                        _leaderPeerId = _memberPeerIds.OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
                }
                UpdateLocalStatusFromState();
                break;
        }
    }
}

public sealed record MeshPartySnapshot(
    string? PartyId,
    string? LeaderPeerId,
    string[] MemberPeerIds,
    string[] PendingInvitePeerIds);
