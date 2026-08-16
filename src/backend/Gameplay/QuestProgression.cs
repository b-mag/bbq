// =============================================================================
// QuestProgression.cs — Local quest flags, Key Items, Friends, See Beyond
// =============================================================================
//
// AUTHORITY: This peer's save file. Quest state is NEVER synced over the mesh.
// Other players can be on different quest steps in the same shard — that is
// intentional for MMORPG-lite. Multiplayer is additive; the solo path is complete.
//
// NECROMOMICON CHAIN (early game):
//   1. Wash ashore near Merek / the dream-ship.
//   2. Find the Old Book Husk on the sand beside Merek.
//   3. Return it — Merek binds it into a blank Necronomicon (0 functions).
//   4. Enter the Drowned Docks, defeat the boss, collect pages.
//   5. Necronomicon gains "See Beyond": a pulsing map marker for the *suggested*
//      next area. Visible through fog-of-war. Pulse stops when that area's boss
//      dies. The player may ignore the suggestion.
//   6. Each later boss grants another page/function. Re-using See Beyond after
//      an upgrade plants a new marker.
//
// FRIENDS:
//   Persisted peer IDs. Future mesh-split / shard overflow MUST prefer keeping
//   Friends in the same neighborhood. The split algorithm does not exist yet;
//   this list is the input it will consume. Do not use Friends for combat
//   authority or loot rights (party already covers that).
// =============================================================================

namespace Carcosa.Server.Gameplay;

public enum NecronomiconQuestStage
{
    None = 0,
    TalkedToMerek = 1,
    HasBookHusk = 2,
    ReturnedHusk = 3,
    SentToDocks = 4,
    SeeBeyondUnlocked = 5,
}

/// <summary>Suggested exploration target for See Beyond. Pulse until that dungeon is cleared.</summary>
public sealed record SuggestedArea(string Id, string Label, float X, float Y, string ScenarioKey);

/// <summary>Runtime quest + key-item + friend state for the local player.</summary>
public sealed class QuestProgression
{
    public const string HuskObjectId = "old_book_husk";
    public const string HuskItemId = "old_book_husk";
    public const string NecronomiconItemId = "necronomicon";
    public const string PagesItemId = "necronomicon_pages";
    public const string ShovelItemId = "obsidian_shovel";
    public const string SeeBeyondId = "see_beyond";

    /// <summary>
    /// Suggested-area chain after the Drowned Docks. Coordinates match OverworldWorldGen
    /// dungeon entrances (norm * 640). Palace Crypt currently reuses the temple map —
    /// documented as a known content gap.
    /// </summary>
    public static readonly SuggestedArea[] ExplorationChain =
    [
        new("drowned_dock", "The Drowned Dock", 0.52f * 640f, 0.90f * 640f, "drowned_dock"),
        new("temple", "Temple of Hali", 0.78f * 640f, 0.34f * 640f, "temple"),
        new("mountain_cave", "Mountain Cave", 0.50f * 640f, 0.16f * 640f, "mountain_cave"),
        new("warehouse", "Sunken Cyclopean Quay", 0.70f * 640f, 0.94f * 640f, "warehouse"),
        new("palace_crypt", "Palace Crypt", 0.82f * 640f, 0.24f * 640f, "temple"),
    ];

    private readonly object _lock = new();
    private Action? _persist;

    public NecronomiconQuestStage Stage { get; private set; }
    public List<string> KeyItemIds { get; } = new();
    public List<SavedFriend> Friends { get; } = new();
    public List<string> DefeatedDungeonIds { get; } = new();
    public List<string> NecronomiconFunctions { get; } = new();
    public List<string> CollectedWorldObjectIds { get; } = new();
    public List<string> DugSpotIds { get; } = new();
    public string? SeeBeyondAreaId { get; private set; }
    public float SeeBeyondX { get; private set; }
    public float SeeBeyondY { get; private set; }
    public string? SeeBeyondLabel { get; private set; }
    public bool SeeBeyondActive { get; private set; }
    public int NecronomiconRank { get; private set; }

    /// <summary>Optional save hook so mutations persist immediately (auto-save still runs).</summary>
    public void SetPersist(Action persist) => _persist = persist;

    public void LoadFrom(PlayerSaveData data)
    {
        lock (_lock)
        {
            Stage = (NecronomiconQuestStage)Math.Clamp(data.NecronomiconQuestStage, 0, 5);
            KeyItemIds.Clear();
            KeyItemIds.AddRange(data.KeyItemIds ?? []);
            Friends.Clear();
            Friends.AddRange(data.Friends ?? []);
            DefeatedDungeonIds.Clear();
            DefeatedDungeonIds.AddRange(data.DefeatedDungeonIds ?? []);
            NecronomiconFunctions.Clear();
            NecronomiconFunctions.AddRange(data.NecronomiconFunctions ?? []);
            CollectedWorldObjectIds.Clear();
            CollectedWorldObjectIds.AddRange(data.CollectedWorldObjectIds ?? []);
            DugSpotIds.Clear();
            DugSpotIds.AddRange(data.DugSpotIds ?? []);
            SeeBeyondAreaId = data.SeeBeyondAreaId;
            SeeBeyondX = data.SeeBeyondX;
            SeeBeyondY = data.SeeBeyondY;
            SeeBeyondLabel = data.SeeBeyondLabel;
            SeeBeyondActive = data.SeeBeyondActive;
            NecronomiconRank = data.NecronomiconRank;
            NormalizeInvariants();
        }
    }

    public void WriteTo(PlayerSaveData data)
    {
        lock (_lock)
        {
            data.NecronomiconQuestStage = (int)Stage;
            data.KeyItemIds = [.. KeyItemIds];
            data.Friends = Friends.Select(f => new SavedFriend
            {
                PeerId = f.PeerId,
                DisplayName = f.DisplayName,
            }).ToList();
            data.DefeatedDungeonIds = [.. DefeatedDungeonIds];
            data.NecronomiconFunctions = [.. NecronomiconFunctions];
            data.CollectedWorldObjectIds = [.. CollectedWorldObjectIds];
            data.DugSpotIds = [.. DugSpotIds];
            data.SeeBeyondAreaId = SeeBeyondAreaId;
            data.SeeBeyondX = SeeBeyondX;
            data.SeeBeyondY = SeeBeyondY;
            data.SeeBeyondLabel = SeeBeyondLabel;
            data.SeeBeyondActive = SeeBeyondActive;
            data.NecronomiconRank = NecronomiconRank;
        }
    }

    public bool HasKeyItem(string itemId)
    {
        lock (_lock) return KeyItemIds.Contains(itemId, StringComparer.Ordinal);
    }

    public bool IsFriend(string peerId)
    {
        lock (_lock) return Friends.Any(f => string.Equals(f.PeerId, peerId, StringComparison.Ordinal));
    }

    public bool ToggleFriend(string peerId, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(peerId)) return false;
        lock (_lock)
        {
            var existing = Friends.FindIndex(f => string.Equals(f.PeerId, peerId, StringComparison.Ordinal));
            if (existing >= 0)
            {
                Friends.RemoveAt(existing);
                Persist();
                return false;
            }

            Friends.Add(new SavedFriend
            {
                PeerId = peerId.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? peerId[..Math.Min(8, peerId.Length)] : displayName.Trim(),
            });
            Persist();
            return true;
        }
    }

    public NpcTalkResult TalkToNpc(string npcType)
    {
        lock (_lock)
        {
            if (!string.Equals(npcType, "npc_merek", StringComparison.OrdinalIgnoreCase))
                return new NpcTalkResult("Wanderer", ["..."], false, Stage.ToString());

            var hasHusk = KeyItemIds.Contains(HuskItemId, StringComparer.Ordinal);
            var hasBook = KeyItemIds.Contains(NecronomiconItemId, StringComparer.Ordinal);

            if (Stage == NecronomiconQuestStage.None && hasHusk)
            {
                Stage = NecronomiconQuestStage.HasBookHusk;
            }

            if (Stage <= NecronomiconQuestStage.TalkedToMerek && !hasHusk && !hasBook)
            {
                Stage = NecronomiconQuestStage.TalkedToMerek;
                Persist();
                return new NpcTalkResult("Merek",
                [
                    "Easy. You washed up on this shore with your mind wind-wiped. Typical. We dragged you above the tide before the docks claimed you.",
                    "That hull behind me is older than the village. A dream-ship. Beside it, in the wet sand, there is an Old Book Husk. Pages gone. Binding still hungry.",
                    "If you mean to go back wherever you came from, that husk may hold the key — or the door. Retrieve it. Bring it to me. Then we will see whether it remembers a name.",
                ], true, Stage.ToString());
            }

            if ((Stage <= NecronomiconQuestStage.HasBookHusk || hasHusk) && hasHusk && !hasBook)
            {
                KeyItemIds.RemoveAll(id => string.Equals(id, HuskItemId, StringComparison.Ordinal));
                if (!KeyItemIds.Contains(NecronomiconItemId, StringComparer.Ordinal))
                    KeyItemIds.Add(NecronomiconItemId);
                Stage = NecronomiconQuestStage.SentToDocks;
                Persist();
                return new NpcTalkResult("Merek",
                [
                    "You found it. Good. Most who wash up never look down.",
                    "Hold still. The husk is not empty — it is waiting. I will give it a spine it recognizes.",
                    "There. The Necronomicon. It has no functions yet. A book that will not open is still a book. Pages are missing. They always are.",
                    "There are rumors — deep in the labyrinth of Dagon, the Drowned Docks — a God Serpent keeps a page. The Agwan will not thank you for walking their sacred waterways.",
                    "Go. Bleed later. Bring back what the serpent hoards, and the book may learn to See Beyond.",
                ], true, Stage.ToString());
            }

            if (Stage == NecronomiconQuestStage.TalkedToMerek)
            {
                return new NpcTalkResult("Merek",
                [
                    "The husk is still in the sand by the dream-ship. South of my feet. You cannot miss it unless you want to.",
                    "Bring it. Then the docks. Then, if you live, pages.",
                ], false, Stage.ToString());
            }

            if (Stage is NecronomiconQuestStage.ReturnedHusk or NecronomiconQuestStage.SentToDocks)
            {
                Stage = NecronomiconQuestStage.SentToDocks;
                Persist();
                return new NpcTalkResult("Merek",
                [
                    "The Drowned Dock is north of the nets. Two Agwan ward the threshold. They will not move for courtesy.",
                    "The Necronomicon is mute until it eats a page. The God Serpent has one. Or something that used to be a page.",
                ], false, Stage.ToString());
            }

            if (Stage >= NecronomiconQuestStage.SeeBeyondUnlocked)
            {
                return new NpcTalkResult("Merek",
                [
                    "It opened. See Beyond will not tell you the truth — only a suggestion. The pulse on your map is a rumor with manners.",
                    "You may ignore it. Carcosa rewards the stubborn and the lost equally. Use the book again after each god you unmake; it will point elsewhere.",
                    "The Wizard of Boz can read the rest. If he still has a throat.",
                ], false, Stage.ToString());
            }

            return new NpcTalkResult("Merek",
            [
                "Listen first. Then bleed later.",
            ], false, Stage.ToString());
        }
    }

    public WorldPickupResult PickupWorldObject(string objectType)
    {
        lock (_lock)
        {
            if (!string.Equals(objectType, HuskObjectId, StringComparison.OrdinalIgnoreCase))
                return new WorldPickupResult(false, null, "Nothing to take.");

            if (CollectedWorldObjectIds.Contains(HuskObjectId, StringComparer.Ordinal)
                || KeyItemIds.Contains(HuskItemId, StringComparer.Ordinal)
                || KeyItemIds.Contains(NecronomiconItemId, StringComparer.Ordinal))
            {
                return new WorldPickupResult(false, null, "The sand is empty. You already took what it offered.");
            }

            CollectedWorldObjectIds.Add(HuskObjectId);
            KeyItemIds.Add(HuskItemId);
            if (Stage < NecronomiconQuestStage.HasBookHusk)
                Stage = NecronomiconQuestStage.HasBookHusk;
            Persist();
            return new WorldPickupResult(true, HuskItemId,
                "An Old Book Husk. The binding is salt-stiff. Merek will want this.");
        }
    }

    public UseKeyItemResult UseKeyItem(string itemId)
    {
        lock (_lock)
        {
            if (string.Equals(itemId, NecronomiconItemId, StringComparison.OrdinalIgnoreCase))
                return UseNecronomicon();
            if (string.Equals(itemId, HuskItemId, StringComparison.OrdinalIgnoreCase))
                return new UseKeyItemResult(false, "The husk will not open for you. Merek said as much.");
            if (string.Equals(itemId, ShovelItemId, StringComparison.OrdinalIgnoreCase))
                return new UseKeyItemResult(false, "Equip the thought of digging. Press G on sand, ash, or loose earth.");
            if (string.Equals(itemId, PagesItemId, StringComparison.OrdinalIgnoreCase))
                return BindPages();
            return new UseKeyItemResult(false, "It does nothing in your hands.");
        }
    }

    public string? NotifyDungeonComplete(string scenario, bool victory)
    {
        if (!victory) return null;
        var areaId = NormalizeDungeonId(scenario);
        lock (_lock)
        {
            if (!DefeatedDungeonIds.Contains(areaId, StringComparer.Ordinal))
                DefeatedDungeonIds.Add(areaId);

            if (SeeBeyondActive && SeeBeyondAreaId != null
                && DungeonMatchesMarker(areaId, SeeBeyondAreaId))
            {
                SeeBeyondActive = false;
            }

            string? message = null;
            var hasBook = KeyItemIds.Contains(NecronomiconItemId, StringComparer.Ordinal);
            if (hasBook && areaId is "drowned_dock" or "warehouse")
            {
                if (!KeyItemIds.Contains(PagesItemId, StringComparer.Ordinal)
                    && !NecronomiconFunctions.Contains(SeeBeyondId, StringComparer.Ordinal))
                {
                    KeyItemIds.Add(PagesItemId);
                    message = "Pages of the Necronomicon fall from the God Serpent like shed skin. They are yours.";
                }
                BindPagesUnlocked();
            }
            else if (hasBook && NecronomiconFunctions.Contains(SeeBeyondId, StringComparer.Ordinal))
            {
                NecronomiconRank = Math.Max(NecronomiconRank, DefeatedDungeonIds.Count);
                message = "The Necronomicon fattens. Use See Beyond again — it will point elsewhere.";
            }

            Persist();
            return message;
        }
    }

    public QuestSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new QuestSnapshot(
                Stage.ToString(),
                (int)Stage,
                [.. KeyItemIds],
                KeyItemIds.Select(ToKeyItemEntry).ToArray(),
                Friends.Select(f => new FriendEntry(f.PeerId, f.DisplayName)).ToArray(),
                [.. DefeatedDungeonIds],
                [.. NecronomiconFunctions],
                [.. CollectedWorldObjectIds],
                SeeBeyondAreaId,
                SeeBeyondX,
                SeeBeyondY,
                SeeBeyondLabel,
                SeeBeyondActive,
                NecronomiconRank,
                KeyItemIds.Contains(ShovelItemId, StringComparer.Ordinal));
        }
    }

    internal void MarkSpotDug(string spotId)
    {
        lock (_lock)
        {
            if (!DugSpotIds.Contains(spotId, StringComparer.Ordinal))
                DugSpotIds.Add(spotId);
            Persist();
        }
    }

    public bool GrantKeyItem(string itemId)
    {
        lock (_lock)
        {
            if (KeyItemIds.Contains(itemId, StringComparer.Ordinal)) return false;
            KeyItemIds.Add(itemId);
            Persist();
            return true;
        }
    }

    internal bool IsSpotDug(string spotId)
    {
        lock (_lock) return DugSpotIds.Contains(spotId, StringComparer.Ordinal);
    }

    private UseKeyItemResult UseNecronomicon()
    {
        if (!KeyItemIds.Contains(NecronomiconItemId, StringComparer.Ordinal))
            return new UseKeyItemResult(false, "You do not carry the book.");

        if (KeyItemIds.Contains(PagesItemId, StringComparer.Ordinal))
            return BindPages();

        if (!NecronomiconFunctions.Contains(SeeBeyondId, StringComparer.Ordinal))
            return new UseKeyItemResult(false, "The Necronomicon is mute. It has no pages. No functions. Merek sent you to the Drowned Docks.");

        var next = NextSuggestedArea();
        if (next == null)
        {
            SeeBeyondActive = false;
            Persist();
            return new UseKeyItemResult(true, "See Beyond shows only black stars. There is no suggested path left — or the book is lying.");
        }

        SeeBeyondAreaId = next.Id;
        SeeBeyondX = next.X;
        SeeBeyondY = next.Y;
        SeeBeyondLabel = next.Label;
        SeeBeyondActive = true;
        Persist();
        return new UseKeyItemResult(true,
            $"See Beyond: a pulse on the map. Suggested — not required: {next.Label}.");
    }

    private UseKeyItemResult BindPages()
    {
        BindPagesUnlocked();
        Persist();
        return new UseKeyItemResult(true,
            "The pages crawl into the binding. The Necronomicon learns its first function: See Beyond. Use it from Key Items.");
    }

    private void BindPagesUnlocked()
    {
        KeyItemIds.RemoveAll(id => string.Equals(id, PagesItemId, StringComparison.Ordinal));
        if (!KeyItemIds.Contains(NecronomiconItemId, StringComparer.Ordinal))
            KeyItemIds.Add(NecronomiconItemId);
        if (!NecronomiconFunctions.Contains(SeeBeyondId, StringComparer.Ordinal))
            NecronomiconFunctions.Add(SeeBeyondId);
        if (Stage < NecronomiconQuestStage.SeeBeyondUnlocked)
            Stage = NecronomiconQuestStage.SeeBeyondUnlocked;
        NecronomiconRank = Math.Max(NecronomiconRank, 1);
    }

    private SuggestedArea? NextSuggestedArea()
    {
        foreach (var area in ExplorationChain)
        {
            if (DefeatedDungeonIds.Any(d => DungeonMatchesMarker(d, area.Id)))
                continue;
            return area;
        }
        return null;
    }

    private void NormalizeInvariants()
    {
        if (NecronomiconFunctions.Contains(SeeBeyondId, StringComparer.Ordinal)
            && Stage < NecronomiconQuestStage.SeeBeyondUnlocked)
            Stage = NecronomiconQuestStage.SeeBeyondUnlocked;
        if (KeyItemIds.Contains(NecronomiconItemId, StringComparer.Ordinal)
            && Stage < NecronomiconQuestStage.ReturnedHusk)
            Stage = NecronomiconQuestStage.ReturnedHusk;
    }

    private void Persist() => _persist?.Invoke();

    private static KeyItemEntry ToKeyItemEntry(string id)
    {
        var def = ItemRegistry.GetItem(id);
        var usable = id is NecronomiconItemId or PagesItemId or ShovelItemId;
        return new KeyItemEntry(
            id,
            def?.Name ?? id,
            def?.Description ?? "",
            def?.Rarity.ToString() ?? "Rare",
            usable);
    }

    public static string NormalizeDungeonId(string scenario)
    {
        var s = (scenario ?? "").Trim().ToLowerInvariant().Replace("-", "_");
        return s switch
        {
            "drowneddock" or "drowned_docks" or "drowned_dock" or "warehouse" => "drowned_dock",
            "pallid_sanctum" or "pallidsanctum" or "temple" => "temple",
            "mountaincave" or "cave" or "mountain_cave" => "mountain_cave",
            "palace_crypt" or "palacecrypt" => "palace_crypt",
            "hollow" => "hollow",
            _ => s,
        };
    }

    private static bool DungeonMatchesMarker(string defeatedId, string markerId)
    {
        var a = NormalizeDungeonId(defeatedId);
        var b = NormalizeDungeonId(markerId);
        if (string.Equals(a, b, StringComparison.Ordinal)) return true;
        if (a == "drowned_dock" && b is "drowned_dock" or "warehouse") return true;
        if (a == "temple" && b is "temple" or "palace_crypt") return string.Equals(b, "temple", StringComparison.Ordinal);
        return false;
    }
}

public sealed class SavedFriend
{
    public string PeerId { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed record NpcTalkResult(string Name, string[] Lines, bool Advanced, string Stage);
public sealed record WorldPickupResult(bool Success, string? ItemId, string Message);
public sealed record UseKeyItemResult(bool Success, string Message);
public sealed record FriendEntry(string PeerId, string DisplayName);
public sealed record KeyItemEntry(string ItemId, string Name, string Description, string Rarity, bool Usable);
public sealed record QuestSnapshot(
    string Stage,
    int StageValue,
    string[] KeyItemIds,
    KeyItemEntry[] KeyItems,
    FriendEntry[] Friends,
    string[] DefeatedDungeonIds,
    string[] NecronomiconFunctions,
    string[] CollectedWorldObjectIds,
    string? SeeBeyondAreaId,
    float SeeBeyondX,
    float SeeBeyondY,
    string? SeeBeyondLabel,
    bool SeeBeyondActive,
    int NecronomiconRank,
    bool HasShovel);
