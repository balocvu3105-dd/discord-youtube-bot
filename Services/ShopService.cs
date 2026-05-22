using Discord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class ShopService
{
    private readonly BotConfiguration _config;
    private readonly ILogger _logger;

    public ShopService(
        IOptions<BotConfiguration> config,
        ILogger<ShopService> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    // =====================================================
    // OVERVIEW EMBED
    // =====================================================

    public (Embed embed, MessageComponent components)
        BuildOverview()
    {
        var games = _config.ShopGames;

        var descBuilder =
            new System.Text.StringBuilder();

        descBuilder.AppendLine(
            "💎 **Tiết kiệm khi nạp game qua LDShop!**");

        descBuilder.AppendLine();

        descBuilder.AppendLine(
            "⚠️ **QUAN TRỌNG — ĐỂ GHI NHẬN HỖ TRỢ SERVER**");

        descBuilder.AppendLine(
            "• Hãy đăng nhập LDShop trước khi nạp");

        descBuilder.AppendLine(
            "• Sau đó mới bấm nút \"Nạp ngay\"");

        descBuilder.AppendLine(
            "• Không dùng tab ẩn danh");

        descBuilder.AppendLine(
            "• Không đóng trình duyệt khi thanh toán");

        descBuilder.AppendLine();

        descBuilder.AppendLine(
            "💡 Nếu đăng nhập sau khi bấm link, hệ thống có thể không ghi nhận hỗ trợ.");

        descBuilder.AppendLine();

        descBuilder.AppendLine(
            "🎮 Chọn game bên dưới để bắt đầu nạp");

        var embedBuilder =
            new EmbedBuilder()
                .WithTitle(
                    "🛒 LDShop — Nạp Game Giá Tốt")
                .WithColor(
                    new Color(255, 165, 0))
                .WithDescription(
                    descBuilder.ToString())
                .WithFooter(
                    $"🔄 Cập nhật mỗi {_config.ShopRefreshHours}h • LDShop")
                .WithCurrentTimestamp();

        foreach (var game in games)
        {
            var value =
                game.DiscountPercent > 0
                    ? $"🔥 -{game.DiscountPercent}%"
                    : "🔥 Ưu đãi";

            embedBuilder.AddField(
                $"{game.Emoji} {game.Name}",
                value,
                inline: true);
        }

        var componentBuilder =
            new ComponentBuilder();

        foreach (var game in games)
        {
            componentBuilder.WithButton(
                label: $"{game.Emoji} {game.Name}",
                style: ButtonStyle.Link,
                url: game.AffiliateLink);
        }

        return (
            embedBuilder.Build(),
            componentBuilder.Build());
    }

    // =====================================================
    // GAME EMBED
    // =====================================================

    public (Embed embed, MessageComponent components)?
        BuildGameEmbed(ShopGameConfig game)
    {
        var descBuilder =
            new System.Text.StringBuilder();

        // Promo
        if (!string.IsNullOrWhiteSpace(
                game.PromoNote))
        {
            descBuilder.AppendLine(
                $"💬 {game.PromoNote}");

            descBuilder.AppendLine();
        }

        // Discount
        if (game.DiscountPercent > 0)
        {
            descBuilder.AppendLine(
                $"🔥 Giảm {game.DiscountPercent}% qua LDShop");
        }

        // Topup type
        if (!string.IsNullOrWhiteSpace(
                game.TopUpType))
        {
            descBuilder.AppendLine(
                $"⚡ {game.TopUpType}");
        }

        // Warning
        if (!string.IsNullOrWhiteSpace(
                game.Warning))
        {
            descBuilder.AppendLine();

            descBuilder.AppendLine(
                game.Warning);
        }

        var embed =
            new EmbedBuilder()
                .WithTitle(
                    $"{game.Emoji} {game.Name} — Giảm {game.DiscountPercent}%!")
                .WithDescription(
                    descBuilder.ToString())
                .WithColor(
                    new Color(255, 140, 0))
                .WithFooter(
                    $"LDShop x {game.Name}")
                .WithCurrentTimestamp()
                .Build();

        var components =
            new ComponentBuilder()
                .WithButton(
                    label: $"🛒 Nạp {game.Name} ngay",
                    style: ButtonStyle.Link,
                    url: game.AffiliateLink)
                .Build();

        return (embed, components);
    }

}