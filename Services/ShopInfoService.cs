using Discord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;

namespace YouTubeDiscordBot.Services;

public class ShopInfoService
{
    private readonly BotConfiguration _config;
    private readonly ILogger<ShopInfoService> _logger;

    public ShopInfoService(
        IOptions<BotConfiguration> config,
        ILogger<ShopInfoService> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    // ── Tạo tin nhắn tổng quan cho #thong-tin-shop ────────────────────────

    public (Embed embed, MessageComponent components) BuildShopOverview()
    {
        var games = _config.ShopGames;

        if (games.Count == 0)
            throw new InvalidOperationException("ShopGames config trống!");

        // Tạo nội dung embed tổng quan
        var embedBuilder = new EmbedBuilder()
            .WithTitle("🛒 LDShop — Nạp Game Giá Tốt Nhất!")
            .WithColor(new Color(255, 165, 0))
            .WithDescription(
                "**Tiết kiệm khi nạp game yêu thích qua LDShop!**\n" +
                "Bấm vào game bên dưới để mở link nạp trực tiếp 👇")
            .WithFooter($"🔄 Cập nhật mỗi {_config.ShopInfoRefreshHours}h • LDShop x Bot")
            .WithCurrentTimestamp();

        // Field cho từng game
        foreach (var game in games)
        {
            string expireText = GetExpireText(game.ExpiresAt);

            embedBuilder.AddField(
                $"{game.Emoji} {game.Name}",
                $"Xem ưu đãi {expireText}",
                inline: true
            );
        }

        // Tạo buttons dạng LINK BUTTON
        var componentBuilder = new ComponentBuilder();

        foreach (var game in games)
        {
            if (string.IsNullOrWhiteSpace(game.AffiliateLink))
                continue;

            componentBuilder.WithButton(
                label: $"{game.Emoji} {game.Name}",
                style: ButtonStyle.Link,
                url: game.AffiliateLink
            );
        }

        return (embedBuilder.Build(), componentBuilder.Build());
    }

    // ── Helper: tính thời hạn còn lại ─────────────────────────────────────

    private static string GetExpireText(string? expiresAt)
    {
        if (string.IsNullOrEmpty(expiresAt))
            return string.Empty;

        if (!DateTime.TryParse(expiresAt, out var expireDate))
            return string.Empty;

        var remaining = expireDate - DateTime.UtcNow;

        if (remaining.TotalSeconds <= 0)
            return "⚠️ Ưu đãi đã hết hạn";

        if (remaining.TotalHours < 1)
            return $"⏳ Còn {(int)remaining.TotalMinutes} phút";

        if (remaining.TotalDays < 1)
            return $"⏳ Còn {(int)remaining.TotalHours} giờ";

        return $"⏳ Còn {(int)remaining.TotalDays} ngày";
    }
}