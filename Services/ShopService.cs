using Discord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class ShopService : IShopService
{
    private readonly BotConfiguration _config;
    private readonly LdShopDiscountService _discountService;
    private readonly ILogger<ShopService> _logger;

    public ShopService(
        IOptions<BotConfiguration> config,
        LdShopDiscountService discountService,
        ILogger<ShopService> logger)
    {
        _config = config.Value;
        _discountService = discountService;
        _logger = logger;
    }

    // ── Cache Warm ───────────────────────────────────────────────────────────

    /// <summary>
    /// Gọi LDShop API và cache discount % cho tất cả games đã config.
    /// Phải được gọi trước BuildOverviewAsync / BuildGameEmbedAsync.
    /// </summary>
    public async Task WarmDiscountCacheAsync()
    {
        var games = _config.ShopGames
            .Where(g => g.CommodityId > 0 && g.SkuLabelId > 0)
            .Select(g => (g.CommodityId, g.SkuLabelId));

        await _discountService.WarmCacheAsync(games);
        _logger.LogInformation("Discount cache warmed for {Count} games", _config.ShopGames.Count);
    }

    // ── Overview Embed ───────────────────────────────────────────────────────

    public Task<(Embed embed, MessageComponent components)> BuildOverviewAsync()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("💎 **Tiết kiệm khi nạp game qua LDShop!**");
        sb.AppendLine();
        sb.AppendLine("⚠️ **QUAN TRỌNG — ĐỂ GHI NHẬN HỖ TRỢ SERVER**");
        sb.AppendLine("• Hãy đăng nhập LDShop trước khi nạp");
        sb.AppendLine("• Sau đó mới bấm nút \"Nạp ngay\"");
        sb.AppendLine("• Không dùng tab ẩn danh");
        sb.AppendLine("• Không đóng trình duyệt khi thanh toán");
        sb.AppendLine();
        sb.AppendLine("💡 Nếu đăng nhập sau khi bấm link, hệ thống có thể không ghi nhận hỗ trợ.");

        // FIX: Dùng ShopNotice từ config nếu có
        if (!string.IsNullOrWhiteSpace(_config.ShopNotice))
        {
            sb.AppendLine();
            sb.AppendLine($"📢 {_config.ShopNotice}");
        }

        sb.AppendLine();
        sb.AppendLine("🎮 Chọn game bên dưới để bắt đầu nạp");

        var embed = new EmbedBuilder()
            .WithTitle("🛒 LDShop — Nạp Game Giá Tốt")
            .WithColor(new Color(255, 165, 0))
            .WithDescription(sb.ToString())
            .WithFooter($"🔄 Cập nhật mỗi {_config.ShopRefreshHours}h • LDShop")
            .WithCurrentTimestamp();

        foreach (var game in _config.ShopGames)
        {
            // FIX: Dùng live discount từ cache, fallback về appsettings nếu cache miss
            var pct = _discountService.GetDiscount(game.CommodityId) ?? game.DiscountPercent;
            embed.AddField(
                $"{game.Emoji} {game.Name}",
                pct > 0 ? $"🔥 -{pct}%" : "🔥 Ưu đãi",
                inline: true);
        }

        var buttons = new ComponentBuilder();
        foreach (var game in _config.ShopGames)
        {
            buttons.WithButton(
                label: $"{game.Emoji} {game.Name}",
                style: ButtonStyle.Link,
                url: game.AffiliateLink);
        }

        return Task.FromResult((embed.Build(), buttons.Build()));
    }

    // ── Game Embed ───────────────────────────────────────────────────────────

    public Task<(Embed embed, MessageComponent components)?> BuildGameEmbedAsync(ShopGameConfig game)
    {
        // FIX: Dùng live discount từ cache, fallback về appsettings nếu cache miss
        var pct = _discountService.GetDiscount(game.CommodityId) ?? game.DiscountPercent;

        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(game.PromoNote))
        {
            sb.AppendLine($"💬 {game.PromoNote}");
            sb.AppendLine();
        }

        if (pct > 0)
            sb.AppendLine($"🔥 Giảm {pct}% qua LDShop");

        if (!string.IsNullOrWhiteSpace(game.TopUpType))
            sb.AppendLine($"⚡ {game.TopUpType}");

        if (!string.IsNullOrWhiteSpace(game.Warning))
        {
            sb.AppendLine();
            sb.AppendLine(game.Warning);
        }

        var titleDiscount = pct > 0 ? $" — Giảm {pct}%!" : string.Empty;

        var embed = new EmbedBuilder()
            .WithTitle($"{game.Emoji} {game.Name}{titleDiscount}")
            .WithDescription(sb.ToString())
            .WithColor(new Color(255, 140, 0))
            .WithFooter($"LDShop x {game.Name}")
            .WithCurrentTimestamp()
            .Build();

        var components = new ComponentBuilder()
            .WithButton(
                label: $"🛒 Nạp {game.Name} ngay",
                style: ButtonStyle.Link,
                url: game.AffiliateLink)
            .Build();

        return Task.FromResult<(Embed, MessageComponent)?>((embed, components));
    }
}