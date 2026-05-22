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