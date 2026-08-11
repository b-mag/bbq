// =============================================================================
// PartyManager.cs — Party System for the Overworld
// =============================================================================
//
// Manages player parties in the persistent overworld. Parties allow players to:
//   - Group up and see each other's status
//   - Enter instanced dungeons together
//   - Use party chat
//
// A party is created implicitly when a player sends their first invite.
// The inviter becomes the party leader. Max 8 members.
// If the leader disconnects, leadership passes to the next member.
// If all members leave, the party is dissolved.
//
// Thread safety: All methods are called from the WebSocket message handler
// which runs on thread pool threads. A lock serializes party mutations.
// =============================================================================

namespace Carcosa.Matchmaking.Overworld;

public sealed class Party
{
    public required string Id { get; init; }
    public string LeaderId { get; set; } = "";
    public List<string> MemberIds { get; } = new();
    public HashSet<string> PendingInvites { get; } = new(); // Player IDs with pending invites
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Manages party lifecycle: creation, invites, joins, leaves, and disbanding.
/// </summary>
public sealed class PartyManager
{
    private const int MaxPartySize = 8;
    private readonly Dictionary<string, Party> _parties = new(); // partyId -> Party
    private readonly Dictionary<string, string> _playerParty = new(); // playerId -> partyId
    private readonly Dictionary<string, string> _pendingInvites = new(); // targetPlayerId -> partyId
    private readonly object _lock = new();

    /// <summary>
    /// Get the party a player is in (or null).
    /// </summary>
    public Party? GetPlayerParty(string playerId)
    {
        lock (_lock)
        {
            if (_playerParty.TryGetValue(playerId, out var partyId))
            {
                return _parties.GetValueOrDefault(partyId);
            }
            return null;
        }
    }

    /// <summary>
    /// Get a party by ID.
    /// </summary>
    public Party? GetParty(string partyId)
    {
        lock (_lock)
        {
            return _parties.GetValueOrDefault(partyId);
        }
    }

    /// <summary>
    /// Invite a player to the inviter's party. Creates a party if inviter has none.
    /// Returns the party ID and whether the invite was sent successfully.
    /// </summary>
    public (string? partyId, bool success, string? error) InvitePlayer(string inviterId, string targetId)
    {
        lock (_lock)
        {
            // Can't invite yourself
            if (inviterId == targetId)
                return (null, false, "Cannot invite yourself");

            // Target already in a party?
            if (_playerParty.ContainsKey(targetId))
                return (null, false, "Player is already in a party");

            // Target already has a pending invite?
            if (_pendingInvites.ContainsKey(targetId))
                return (null, false, "Player already has a pending invite");

            // Get or create party for the inviter
            Party party;
            if (_playerParty.TryGetValue(inviterId, out var existingPartyId))
            {
                party = _parties[existingPartyId];
                // Only leader can invite
                if (party.LeaderId != inviterId)
                    return (null, false, "Only the party leader can invite");
            }
            else
            {
                // Create new party
                var partyId = Guid.NewGuid().ToString("N")[..8];
                party = new Party
                {
                    Id = partyId,
                    LeaderId = inviterId,
                };
                party.MemberIds.Add(inviterId);
                _parties[partyId] = party;
                _playerParty[inviterId] = partyId;
                Console.WriteLine($"[Party] Created party {partyId} with leader {inviterId}");
            }

            // Check party size
            if (party.MemberIds.Count >= MaxPartySize)
                return (null, false, "Party is full");

            // Register pending invite
            party.PendingInvites.Add(targetId);
            _pendingInvites[targetId] = party.Id;

            Console.WriteLine($"[Party] {inviterId} invited {targetId} to party {party.Id}");
            return (party.Id, true, null);
        }
    }

    /// <summary>
    /// Accept a party invite. Returns the party if successful.
    /// </summary>
    public (Party? party, bool success, string? error) AcceptInvite(string playerId, string partyId)
    {
        lock (_lock)
        {
            if (!_pendingInvites.TryGetValue(playerId, out var invitedPartyId) || invitedPartyId != partyId)
                return (null, false, "No pending invite for this party");

            if (!_parties.TryGetValue(partyId, out var party))
                return (null, false, "Party no longer exists");

            if (party.MemberIds.Count >= MaxPartySize)
                return (null, false, "Party is full");

            // Join the party
            _pendingInvites.Remove(playerId);
            party.PendingInvites.Remove(playerId);
            party.MemberIds.Add(playerId);
            _playerParty[playerId] = partyId;

            Console.WriteLine($"[Party] {playerId} joined party {partyId} ({party.MemberIds.Count} members)");
            return (party, true, null);
        }
    }

    /// <summary>
    /// Decline a party invite.
    /// </summary>
    public void DeclineInvite(string playerId, string partyId)
    {
        lock (_lock)
        {
            if (_pendingInvites.TryGetValue(playerId, out var invitedPartyId) && invitedPartyId == partyId)
            {
                _pendingInvites.Remove(playerId);
                if (_parties.TryGetValue(partyId, out var party))
                {
                    party.PendingInvites.Remove(playerId);
                }
            }
        }
    }

    /// <summary>
    /// Remove a player from their party. If they're the leader, promote next member.
    /// If the party becomes empty, dissolve it.
    /// Returns the affected party (for broadcasting updates) and whether it was disbanded.
    /// </summary>
    public (Party? party, bool disbanded) LeaveParty(string playerId)
    {
        lock (_lock)
        {
            if (!_playerParty.TryGetValue(playerId, out var partyId))
                return (null, false);

            if (!_parties.TryGetValue(partyId, out var party))
            {
                _playerParty.Remove(playerId);
                return (null, false);
            }

            party.MemberIds.Remove(playerId);
            _playerParty.Remove(playerId);

            // If party is now empty, dissolve
            if (party.MemberIds.Count == 0)
            {
                _parties.Remove(partyId);
                // Clear any pending invites for this party
                var invitesToRemove = _pendingInvites.Where(kv => kv.Value == partyId).Select(kv => kv.Key).ToList();
                foreach (var inv in invitesToRemove) _pendingInvites.Remove(inv);
                Console.WriteLine($"[Party] Party {partyId} disbanded (empty)");
                return (party, true);
            }

            // If leader left, promote next member
            if (party.LeaderId == playerId)
            {
                party.LeaderId = party.MemberIds[0];
                Console.WriteLine($"[Party] New leader for {partyId}: {party.LeaderId}");
            }

            // If only 1 member left, disband (a party of 1 makes no sense after someone leaves)
            if (party.MemberIds.Count == 1)
            {
                var lastMember = party.MemberIds[0];
                party.MemberIds.Clear();
                _playerParty.Remove(lastMember);
                _parties.Remove(partyId);
                var invitesToRemove = _pendingInvites.Where(kv => kv.Value == partyId).Select(kv => kv.Key).ToList();
                foreach (var inv in invitesToRemove) _pendingInvites.Remove(inv);
                Console.WriteLine($"[Party] Party {partyId} disbanded (last member left)");
                return (party, true);
            }

            Console.WriteLine($"[Party] {playerId} left party {partyId} ({party.MemberIds.Count} remaining)");
            return (party, false);
        }
    }

    /// <summary>
    /// Handle player disconnect — remove from party.
    /// </summary>
    public (Party? party, bool disbanded) HandleDisconnect(string playerId)
    {
        lock (_lock)
        {
            // Remove any pending invites TO this player
            _pendingInvites.Remove(playerId);

            // Remove pending invites FROM this player (if they had any outstanding)
            var partiesToClean = _parties.Values.Where(p => p.PendingInvites.Contains(playerId)).ToList();
            foreach (var p in partiesToClean) p.PendingInvites.Remove(playerId);

            // Leave their party if in one
            return LeaveParty(playerId);
        }
    }

    /// <summary>
    /// Get all active parties (for dashboard/metrics).
    /// </summary>
    public List<Party> GetAllParties()
    {
        lock (_lock)
        {
            return _parties.Values.ToList();
        }
    }

    /// <summary>
    /// Check if a player is the leader of their party.
    /// </summary>
    public bool IsLeader(string playerId)
    {
        lock (_lock)
        {
            if (!_playerParty.TryGetValue(playerId, out var partyId)) return false;
            if (!_parties.TryGetValue(partyId, out var party)) return false;
            return party.LeaderId == playerId;
        }
    }
}
