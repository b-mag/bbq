// =============================================================================
// DigSystem.cs — A Link to the Past-style digging (Obsidian Shovel)
// =============================================================================
//
// RULES:
//   - Digging requires the Obsidian Shovel key item (not granted at the start;
//     later content / a later dungeon will award it).
//   - The player may dig on sand, ash, desert, path, and dark earth.
//   - Most tiles yield nothing or a minor material.
//   - A handful of *clue spots* are deterministic world coordinates. Those
//     hide the named artifacts below. Secret spots are far from roads.
//   - Each productive spot can be dug once per save.
//
// Mesh: digging is local. Loot is private. No P2P broadcast.
// =============================================================================

namespace Carcosa.Server.Gameplay;

public sealed record DigArtifact(
    string Id,
    string Name,
    string Flavor,
    string Effect,
    bool Secret,
    bool Passive,
    float X,
    float Y,
    float Radius = 1.6f);

public sealed record DigResult(bool Success, string Message, string? ItemId, bool KeyItem);

public static class DigSystem
{
    /// <summary>
    /// 12 Carcosa / Dagon / King-in-Yellow artifacts. Majority are passives.
    /// Secret entries are intentionally off the suggested path.
    /// </summary>
    public static readonly DigArtifact[] Artifacts =
    [
        new("pallid_mask_shard", "Pallid Mask Shard",
            "A porcelain sliver that will not take a fingerprint. It is warmer than the sand.",
            "Passive: nearby Agwan and cultists hesitate 0.4s longer before aggro (not yet wired to AI).",
            Secret: false, Passive: true, X: 0.48f * 640f, Y: 0.93f * 640f),

        new("hali_tide_glass", "Hali Tide-Glass",
            "Lake water trapped in a lens that never quite stills. Twin suns swim inside it.",
            "Passive: shallow water no longer slows you. Deep water still kills the unwise.",
            Secret: false, Passive: true, X: 0.34f * 640f, Y: 0.40f * 640f),

        new("yhtill_reed_whistle", "Yhtill Reed-Whistle",
            "A swamp reed bored with seven holes. Six play. The seventh inhales.",
            "Passive: swamp tiles do not drain stamina. Bubbles still listen.",
            Secret: false, Passive: true, X: 0.30f * 640f, Y: 0.60f * 640f),

        new("waste_cinder_compass", "Waste Cinder Compass",
            "The needle points at heat, not north. In the Waste it spins until it finds a grave.",
            "Passive: See Beyond pulses 20% faster while you stand on ash or desert.",
            Secret: false, Passive: true, X: 0.18f * 640f, Y: 0.42f * 640f),

        new("hyades_black_star_nail", "Black-Star Nail",
            "A climbing piton hammered from a star that does not move. Cold enough to bite leather.",
            "Passive: ladders and mountain paths cost no stamina.",
            Secret: false, Passive: true, X: 0.50f * 640f, Y: 0.12f * 640f),

        new("cassilda_song_coin", "Cassilda's Song-Coin",
            "One face is a mouth. The other is a mouth. Spending it would be a kind of singing.",
            "Passive: Cryptol shop prices reduced by 1 (minimum 1). The merchant does not comment.",
            Secret: false, Passive: true, X: 0.51f * 640f, Y: 0.86f * 640f),

        new("dagon_scale_spade", "Dagon Scale (false shovel-tip)",
            "Not the Obsidian Shovel — a scale that wants to be one. It fits no haft.",
            "Passive: dig radius +0.5 tiles. Productive spots hum when you stand on them.",
            Secret: false, Passive: true, X: 0.70f * 640f, Y: 0.94f * 640f),

        new("king_yellow_playbill", "Torn Playbill of the King",
            "Act II is missing. Act I is written in a hand you almost recognize as your own.",
            "Passive: fog of war reveal radius +2 tiles. Reading it is how the King enters — so do not.",
            Secret: false, Passive: true, X: 0.82f * 640f, Y: 0.26f * 640f),

        new("court_dragon_ash_heart", "Ash-Heart of the Court",
            "A coal that never finishes dying. The Court of the Dragon coughs in time with it.",
            "Passive: +5 max HP. Fire-themed enemies deal 1 less.",
            Secret: false, Passive: true, X: 0.14f * 640f, Y: 0.22f * 640f),

        // --- secrets (far from roads / easy landmarks) ---
        new("alhazred_ink_tooth", "Alhazred's Ink-Tooth",
            "A canine stained black. When you bite down, you taste a language.",
            "Passive: Necronomicon See Beyond remains visible even if you close the map. Secret.",
            Secret: true, Passive: true, X: 0.08f * 640f, Y: 0.08f * 640f, Radius: 1.1f),

        new("carcosa_second_sun_lens", "Second-Sun Lens",
            "The twin you were not using. Through it the village is a ruin and the ruin is a village.",
            "Active (later): swap day/night palette for 20s. For now: +0.3 move speed at dusk-colored tiles. Secret.",
            Secret: true, Passive: false, X: 0.92f * 640f, Y: 0.08f * 640f, Radius: 1.1f),

        new("nameless_city_key", "Key to a Nameless City",
            "It opens no door on this map. The teeth are arranged like a street plan you have not walked.",
            "Secret key item. No function yet. Many players will never stand on this dune.",
            Secret: true, Passive: false, X: 0.06f * 640f, Y: 0.72f * 640f, Radius: 0.9f),
    ];

    public static bool CanDigTile(byte tileType) => tileType is
        7 or  // Sand
        6 or  // Path
        16 or // Desert
        20 or // Ash
        14 or // DarkGrass
        0;    // Grass (weak; mostly nothing)

    public static DigResult TryDig(QuestProgression quest, PlayerInventory inventory, float x, float y, byte tileType)
    {
        if (!quest.HasKeyItem(QuestProgression.ShovelItemId))
            return new DigResult(false, "You have nothing that will bite the earth. The Obsidian Shovel is a later mercy.", null, false);

        if (!CanDigTile(tileType))
            return new DigResult(false, "The ground here refuses. Stone, water, and palace floors keep their dead.", null, false);

        foreach (var art in Artifacts)
        {
            var dx = x - art.X;
            var dy = y - art.Y;
            if (dx * dx + dy * dy > art.Radius * art.Radius) continue;

            if (quest.IsSpotDug(art.Id))
                return new DigResult(true, "You have already emptied this grave. The hole remembers you.", null, false);

            quest.MarkSpotDug(art.Id);
            quest.GrantKeyItem(art.Id);
            var hush = art.Secret ? " Few feet were meant to find this." : "";
            return new DigResult(true, $"You unearth {art.Name}. {art.Flavor}{hush}", art.Id, true);
        }

        // Minor junk / nothing. Deterministic-ish from tile coords so two players
        // digging the same mundane tile don't both invent treasure.
        var h = Hash(x, y);
        if (h % 11 == 0)
        {
            if (inventory.AddItem("dark_feathers", 1))
                return new DigResult(true, "A handful of dark feathers, buried as if they grew here.", "dark_feathers", false);
        }
        if (h % 17 == 0)
        {
            if (inventory.AddItem("raw_gronk_meat", 1))
                return new DigResult(true, "Something stored meat against a famine that never ended.", "raw_gronk_meat", false);
        }

        return new DigResult(true, "Sand. Ash. A tooth that is not yours. Nothing worth keeping.", null, false);
    }

    private static int Hash(float x, float y)
    {
        unchecked
        {
            var ix = (int)MathF.Floor(x * 4f);
            var iy = (int)MathF.Floor(y * 4f);
            var h = 17;
            h = h * 31 + ix;
            h = h * 31 + iy;
            return h & int.MaxValue;
        }
    }
}
