namespace YouTubeDiscordBot.Models;

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
