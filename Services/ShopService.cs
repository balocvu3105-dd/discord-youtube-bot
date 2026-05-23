using Discord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class ShopService : IShopService
{
    private readonly BotConfiguration _config;
    private readonly LdShopDiscountService _discountSvc;
    private readonly ILogger<ShopService> _logger;

    public ShopService(
        IOptions<BotConfiguration> config,
        LdShopDiscountService discountSvc,
        ILogger<ShopService> logger)
    {
        _config = config.Value;
        _discountSvc = discountSvc;
        _logger = logger;
    }

    // ── Cache Warm ───────────────────────────────────────────────────────────

    public async Task WarmDiscountCacheAsync()
    {
        var games = _config.ShopGames
            .Where(g => g.CommodityId > 0)
            .Select(g => (g.CommodityId, g.SkuLabelId))
            .ToList();

        if (games.Count == 0) return;

        _logger.LogInformation("Warming discount cache for {Count} games...", games.Count);
        await _discountSvc.WarmCacheAsync(games);
    }

    // ── Overview Embed ───────────────────────────────────────────────────────

    public async Task<(Embed embed, MessageComponent components)> BuildOverviewAsync()
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
            var pct = ResolveDiscount(game);
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

        return (embed.Build(), buttons.Build());
    }

    // ── Game Embed ───────────────────────────────────────────────────────────

    public async Task<(Embed embed, MessageComponent components)?> BuildGameEmbedAsync(ShopGameConfig game)
    {
        var pct = ResolveDiscount(game);

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

        return await Task.FromResult((embed, components));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private int ResolveDiscount(ShopGameConfig game)
    {
        if (game.CommodityId > 0)
        {
            var live = _discountSvc.GetDiscount(game.CommodityId);
            if (live.HasValue)
            {
                if (live.Value != game.DiscountPercent)
                    _logger.LogInformation(
                        "[{Game}] discount live={Live}% (config={Config}%)",
                        game.Name, live.Value, game.DiscountPercent);
                return live.Value;
            }

            _logger.LogWarning(
                "[{Game}] API không trả được discount — dùng config fallback {Config}%",
                game.Name, game.DiscountPercent);
        }

        return game.DiscountPercent;
    }
}