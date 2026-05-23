namespace YouTubeDiscordBot.Models;

/// <summary>
/// Cấu hình một game trong shop, đọc từ appsettings.json > ShopGames array.
/// </summary>
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

/// <summary>
/// Lưu message ID của các shop embed đã gửi.
/// Dùng để edit message cũ thay vì gửi message mới mỗi lần refresh,
/// giúp channel không bị spam.
/// </summary>
public class ShopMessageState
{
    /// <summary>Message ID của embed overview (embed tổng quan đầu tiên).</summary>
    public ulong PinnedMessageId { get; set; }

    /// <summary>Key = game name, Value = message ID của embed đó.</summary>
    public Dictionary<string, ulong> GameMessageIds { get; set; } = new();
}

/// <summary>
/// Thông tin giảm giá của một game từ LDShop API.
/// Dùng bởi LdShopScraperService.
/// </summary>
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