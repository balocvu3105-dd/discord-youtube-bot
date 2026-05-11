namespace YouTubeDiscordBot.Models;

/// <summary>
/// Đại diện cho 1 game khuyến mãi lấy từ ldshop.gg
/// </summary>
public class LdShopPromo
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public int DiscountPercent { get; set; }   // VD: 24
    public string Category { get; set; } = ""; // VD: "top-up", "card"

    // So sánh để detect thay đổi
    public override bool Equals(object? obj) =>
        obj is LdShopPromo other &&
        Name == other.Name &&
        DiscountPercent == other.DiscountPercent;

    public override int GetHashCode() =>
        HashCode.Combine(Name, DiscountPercent);
}