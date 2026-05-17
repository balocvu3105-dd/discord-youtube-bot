namespace YouTubeDiscordBot.Config;

public class BotConfiguration
{
    public const string SectionName = "BotConfiguration";

    // =====================================================
    // DISCORD
    // =====================================================

    public string DiscordToken { get; set; } =
        string.Empty;

    // =====================================================
    // DISCORD CHANNEL IDs
    // Lấy bằng cách: Discord → Developer Mode ON
    // → Chuột phải channel → Copy Channel ID
    // =====================================================

    public ulong LiveChannelId { get; set; }
    public ulong VideoChannelId { get; set; }
    public ulong PromoChannelId { get; set; }
    public ulong ShopInfoChannelId { get; set; }

    // =====================================================
    // DISCORD ROLE IDs
    // Lấy bằng cách: Discord → Developer Mode ON
    // → Server Settings → Roles → Chuột phải role → Copy Role ID
    // Để 0 = không tag role (bot sẽ bỏ qua mention)
    // =====================================================

    public ulong LiveRoleId { get; set; } = 0;
    public ulong VideoRoleId { get; set; } = 0;

    // =====================================================
    // YOUTUBE
    // =====================================================

    public string YoutubeApiKey { get; set; } =
        string.Empty;

    public string YoutubeChannelId { get; set; } =
        string.Empty;

    public int CheckIntervalSeconds { get; set; } = 120;

    public string StateFilePath { get; set; } =
        "last_video_state.json";

    // =====================================================
    // PROMO
    // =====================================================

    public int PromoIntervalHours { get; set; } = 12;

    public List<PromoGameConfig> PromoGames { get; set; } =
        new();

    // =====================================================
    // SHOP INFO
    // =====================================================

    public int ShopInfoRefreshHours { get; set; } = 24;

    public List<ShopGameConfig> ShopGames { get; set; } =
        new();
}

// =========================================================
// PROMO GAME CONFIG
// =========================================================

public class PromoGameConfig
{
    public string Name { get; set; } =
        string.Empty;

    public string AffiliateLink { get; set; } =
        string.Empty;
}

// =========================================================
// SHOP GAME CONFIG
// =========================================================

public class ShopGameConfig
{
    public string Name { get; set; } =
        string.Empty;

    public string Emoji { get; set; } = "🎮";

    public string AffiliateLink { get; set; } =
        string.Empty;

    public int DiscountPercent { get; set; } = 0;

    public string PromoNote { get; set; } =
        string.Empty;

    public string? ExpiresAt { get; set; }

    public string HowToTopUp { get; set; } =
        "1️⃣ Bấm nút **Nạp ngay** bên dưới\n" +
        "2️⃣ Đăng nhập LDShop\n" +
        "3️⃣ Chọn gói → Tự Động Nạp Hoặc Điền User ID → Thanh toán\n" +
        "✅ Nhận vật phẩm nhanh chóng!";
}