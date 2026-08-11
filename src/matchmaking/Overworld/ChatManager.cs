// =============================================================================
// ChatManager.cs — Free-Text Chat with Channel Routing & Profanity Filter
// =============================================================================
//
// Supports three chat channels:
//   - Global: Broadcast to all connected players
//   - Nearby: Only players within 10 tiles of the sender
//   - Party: Only members of the sender's party
//
// Profanity filter: Simple word-list replacement. Matched words get replaced
// with asterisks. Case-insensitive matching with whole-word boundaries.
// The word list is intentionally basic — extend as needed.
// =============================================================================

using System.Text.RegularExpressions;

namespace Carcosa.Matchmaking.Overworld;

/// <summary>
/// Manages chat message routing and profanity filtering.
/// </summary>
public sealed class ChatManager
{
    private const float NearbyRadius = 10f;
    private const int MaxMessageLength = 200;

    private readonly OverworldConnectionManager _connections;
    private readonly OverworldLoop _loop;
    private readonly PartyManager _partyManager;
    private readonly HashSet<string> _badWords;
    private readonly Regex _filterRegex;

    public ChatManager(
        OverworldConnectionManager connections,
        OverworldLoop loop,
        PartyManager partyManager)
    {
        _connections = connections;
        _loop = loop;
        _partyManager = partyManager;
        _badWords = LoadBadWords();
        _filterRegex = BuildFilterRegex(_badWords);
    }

    /// <summary>
    /// Process and route a chat message from a player.
    /// </summary>
    public async Task HandleChatMessage(string senderId, OwChatMessagePayload chatMsg)
    {
        var player = _loop.GetPlayer(senderId);
        if (player == null) return;

        // Truncate long messages
        var text = chatMsg.Text;
        if (text.Length > MaxMessageLength)
            text = text[..MaxMessageLength];

        // Apply profanity filter
        text = FilterProfanity(text);

        var outgoing = new OverworldMessage
        {
            Type = OwMessageTypes.ChatMessage,
            ChatMessage = new OwChatMessagePayload
            {
                Channel = chatMsg.Channel,
                SenderId = senderId,
                SenderName = player.Name,
                Text = text,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }
        };

        switch (chatMsg.Channel)
        {
            case "global":
                await _connections.BroadcastAsync(outgoing);
                break;

            case "nearby":
                await SendToNearbyPlayers(senderId, player.X, player.Y, outgoing);
                break;

            case "party":
                await SendToPartyMembers(senderId, outgoing);
                break;

            default:
                // Unknown channel — treat as global
                await _connections.BroadcastAsync(outgoing);
                break;
        }
    }

    /// <summary>
    /// Send a message only to players within NearbyRadius tiles.
    /// </summary>
    private async Task SendToNearbyPlayers(string senderId, float senderX, float senderY, OverworldMessage message)
    {
        var nearbyIds = new List<string>();

        foreach (var (id, player) in _loop.Players)
        {
            if (player.Status == "in_dungeon") continue;
            var dx = player.X - senderX;
            var dy = player.Y - senderY;
            var dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist <= NearbyRadius)
            {
                nearbyIds.Add(id);
            }
        }

        await _connections.SendToMultipleAsync(nearbyIds, message);
    }

    /// <summary>
    /// Send a message only to the sender's party members.
    /// </summary>
    private async Task SendToPartyMembers(string senderId, OverworldMessage message)
    {
        var party = _partyManager.GetPlayerParty(senderId);
        if (party == null)
        {
            // Not in a party — send only to themselves with an error
            await _connections.SendToAsync(senderId, new OverworldMessage
            {
                Type = OwMessageTypes.Error,
                Error = new OwErrorPayload { Code = "not_in_party", Message = "You are not in a party" }
            });
            return;
        }

        await _connections.SendToMultipleAsync(party.MemberIds, message);
    }

    /// <summary>
    /// Replace profane words with asterisks.
    /// </summary>
    public string FilterProfanity(string text)
    {
        return _filterRegex.Replace(text, match =>
        {
            return new string('*', match.Length);
        });
    }

    /// <summary>
    /// Build a regex that matches any bad word with word boundaries.
    /// Case-insensitive.
    /// </summary>
    private static Regex BuildFilterRegex(HashSet<string> words)
    {
        if (words.Count == 0)
            return new Regex("(?!)", RegexOptions.Compiled); // Matches nothing

        var escaped = words.Select(Regex.Escape);
        var pattern = @"\b(" + string.Join("|", escaped) + @")\b";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    /// <summary>
    /// Load the profanity word list. Basic set — extend as needed.
    /// </summary>
    private static HashSet<string> LoadBadWords()
    {
        // A minimal set of common profanity. In production, load from a file.
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "fuck", "shit", "ass", "bitch", "damn", "crap",
            "dick", "cock", "pussy", "bastard", "whore",
            "slut", "cunt", "fag", "nigger", "nigga",
            "retard", "retarded",
        };
    }
}
