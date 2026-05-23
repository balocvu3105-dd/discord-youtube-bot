using Discord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class ShopService : IShopService
{
    private readonly BotConfiguration _config;
    private readonly ILogger<ShopService> _logger;

    public ShopService(
        IOptions<BotConfiguration> config,
        ILogger<ShopService> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    // ── Overview Embed ───────────────────────────────────────────────────────

    public (Embed embed, MessageComponent components) BuildOverview()
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
            embed.AddField(
                $"{game.Emoji} {game.Name}",
                game.DiscountPercent > 0 ? $"🔥 -{game.DiscountPercent}%" : "🔥 Ưu đãi",
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

    public (Embed embed, MessageComponent components)? BuildGameEmbed(ShopGameConfig game)
    {
        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(game.PromoNote))
        {
            sb.AppendLine($"💬 {game.PromoNote}");
            sb.AppendLine();
        }

        if (game.DiscountPercent > 0)
            sb.AppendLine($"🔥 Giảm {game.DiscountPercent}% qua LDShop");

        if (!string.IsNullOrWhiteSpace(game.TopUpType))
            sb.AppendLine($"⚡ {game.TopUpType}");

        if (!string.IsNullOrWhiteSpace(game.Warning))
        {
            sb.AppendLine();
            sb.AppendLine(game.Warning);
        }

        var embed = new EmbedBuilder()
            .WithTitle($"{game.Emoji} {game.Name} — Giảm {game.DiscountPercent}%!")
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

        return (embed, components);
    }
}