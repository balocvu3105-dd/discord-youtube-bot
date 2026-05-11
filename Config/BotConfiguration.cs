namespace YouTubeDiscordBot.Config;

public class BotConfiguration
{
    public const string SectionName = "BotConfiguration";

    public string DiscordToken { get; set; } = string.Empty;
    public string ChannelName { get; set; } = "youtube-noti";
    public string YoutubeApiKey { get; set; } = string.Empty;
    public string YoutubeChannelId { get; set; } = string.Empty;
    public int CheckIntervalSeconds { get; set; } = 1200;
    public string StateFilePath { get; set; } = "last_video_state.json";
    public ulong DiscordChannelId { get; set; }

    // Promo cũ — giữ nguyên
    public string PromoChannelName { get; set; } = "khuyen-mai";
    public int PromoIntervalHours { get; set; } = 12;
    public List<PromoGameConfig> PromoGames { get; set; } = new();

    // ── SHOP INFO (MỚI) ──────────────────────────────────────────────────
    public string ShopInfoChannelName { get; set; } = "thong-tin-shop";
    public int ShopInfoRefreshHours { get; set; } = 24;
    public List<ShopGameConfig> ShopGames { get; set; } = new();
}

public class PromoGameConfig
{
    public string Name { get; set; } = string.Empty;
    public string AffiliateLink { get; set; } = string.Empty;
}

public class ShopGameConfig
{
    public string Name { get; set; } = string.Empty;
    public string Emoji { get; set; } = "🎮";
    public string AffiliateLink { get; set; } = string.Empty;
    public int DiscountPercent { get; set; } = 0;
    public string PromoNote { get; set; } = string.Empty;

    // ISO 8601: "2026-06-01T23:59:59" — để null nếu không giới hạn
    public string? ExpiresAt { get; set; }

    public string HowToTopUp { get; set; } =
        "1️⃣ Bấm nút **Nạp ngay** bên dưới\n" +
        "2️⃣ Đăng nhập LDShop (tài khoản mới = thêm 15%)\n" +
        "3️⃣ Chọn gói → Điền User ID → Thanh toán\n" +
        "✅ Nhận tiền tệ trong vòng 15 phút!";
}