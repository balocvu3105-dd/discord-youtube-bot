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

    public async Task WarmDiscountCacheAsync()
    {
        var games = _config.ShopGames
            .Where(g => g.CommodityId > 0 && g.SkuLabelId > 0)
            .Select(g => (g.CommodityId, g.SkuLabelId));

        await _discountService.WarmCacheAsync(games);
        _logger.LogInformation("Discount cache warmed for {Count} games", _config.ShopGames.Count);
    }

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
}