namespace YouTubeDiscordBot.Models;

public class ShopGameConfig
{
    public string Name { get; set; } = string.Empty;
    public string Emoji { get; set; } = "🎮";
    public string AffiliateLink { get; set; } = string.Empty;
    public int DiscountPercent { get; set; }
    public string PromoNote { get; set; } = string.Empty;
    public string TopUpType { get; set; } = string.Empty;
    public string Warning { get; set; } = string.Empty;
}

public class ShopMessageState
{
    public ulong PinnedMessageId { get; set; }
    public Dictionary<string, ulong> GameMessageIds { get; set; } = new();
}

public class LdShopPromo
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int DiscountPercent { get; set; }
    public string Category { get; set; } = string.Empty;

    public override bool Equals(object? obj) =>
        obj is LdShopPromo other &&
        Name == other.Name &&
        DiscountPercent == other.DiscountPercent;

    public override int GetHashCode() =>
        HashCode.Combine(Name, DiscountPercent);
}