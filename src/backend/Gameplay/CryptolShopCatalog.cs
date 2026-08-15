namespace Carcosa.Server.Gameplay;

internal sealed record CryptolShopListing(string ItemId, int Price);

internal static class CryptolShopCatalog
{
    public static readonly CryptolShopListing[] Items =
    [
        new("dim_shore_blade", 40),
        new("tattered_hide", 35),
        new("worn_leather_boots", 30),
        new("raw_gronk_meat", 8),
        new("gronk_bone_charm", 90),
    ];
}
