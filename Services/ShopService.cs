using Discord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class ShopService : IShopService
{
    private readonly BotConfiguration _config;
    private readonly ShopDiscountAggregator _aggregator;
    private readonly ILogger<ShopService> _logger;

    // Discord giới hạn 25 button / 5 hàng
    private const int MaxButtons = 25;

    public ShopService(
        IOptions<BotConfiguration> config,
        ShopDiscountAggregator aggregator,
        ILogger<ShopService> logger)
    {
        _config = config.Value;
        _aggregator = aggregator;
        _logger = logger;
    }

    public async Task WarmDiscountCacheAsync()
        => await _aggregator.WarmAllAsync(_config.ShopGames);

    // ── LDShop Section ───────────────────────────────────────────────────────

    public Task<(Embed embed, MessageComponent components)> BuildLdShopEmbedAsync()
    {
        var embed = new EmbedBuilder()
            .WithTitle("🛒 LDShop — Nạp Game Giá Rẻ")
            .WithColor(new Color(255, 140, 0))
            .WithDescription(BuildShopWarningDescription("LDShop"))
            .WithFooter("🔄 Cập nhật lúc 00:00 & 12:00 (giờ VN)")
            .WithCurrentTimestamp();

        var buttons = new ComponentBuilder();
        var btnCount = 0;

        foreach (var game in _config.ShopGames)
        {
            if (!IsValidUrl(game.AffiliateLink))
            {
                if (!string.IsNullOrWhiteSpace(game.AffiliateLink))
                    _logger.LogWarning("[LDShop] AffiliateLink không hợp lệ cho {Game}: {Url}",
                        game.Name, game.AffiliateLink);
                continue;
            }

            var discounts = _aggregator.GetDiscounts(game);
            var ldShop = discounts.FirstOrDefault(d => d.ProviderName == "LDShop");

            embed.AddField(
                $"{game.Emoji} {game.Name}",
                BuildLdShopFieldValue(game, ldShop),
                inline: true);

            AddButton(buttons, ref btnCount,
                label: ldShop is { Percent: > 0 }
                    ? $"{game.Emoji} {game.Name} (-{ldShop.Percent}%)"
                    : $"{game.Emoji} {game.Name}",
                url: game.AffiliateLink);
        }

        return Task.FromResult((embed.Build(), buttons.Build()));
    }

    // ── Lootbar Section ──────────────────────────────────────────────────────

    public Task<(Embed embed, MessageComponent components)> BuildLootbarEmbedAsync()
    {
        var embed = new EmbedBuilder()
            .WithTitle("🏪 Lootbar — Cửa Hàng CataWuwa")
            .WithColor(new Color(88, 101, 242))
            .WithDescription(BuildShopWarningDescription("Lootbar"))
            .WithFooter("🔄 Cập nhật lúc 00:00 & 12:00 (giờ VN)")
            .WithCurrentTimestamp();

        var buttons = new ComponentBuilder();
        var btnCount = 0;

        foreach (var game in _config.ShopGames)
        {
            if (string.IsNullOrWhiteSpace(game.LootbarGameSeo)) continue;

            var discounts = _aggregator.GetDiscounts(game);
            var lootbar = discounts.FirstOrDefault(d => d.ProviderName == "Lootbar");

            if (lootbar is null)
            {
                _logger.LogDebug("[Lootbar section] {Game} — không có data, bỏ qua", game.Name);
                continue;
            }

            if (!IsValidUrl(lootbar.AffiliateLink))
            {
                _logger.LogWarning("[Lootbar] AffiliateLink không hợp lệ cho {Game}: {Url}",
                    game.Name, lootbar.AffiliateLink);
                continue;
            }

            var fieldValue = lootbar.Percent > 0 ? $"🔥 **-{lootbar.Percent}%**" : "🔥 Ưu đãi";
            embed.AddField($"{game.Emoji} {game.Name}", fieldValue, inline: true);

            AddButton(buttons, ref btnCount,
                label: lootbar.Percent > 0
                    ? $"{game.Emoji} {game.Name} (-{lootbar.Percent}%)"
                    : $"{game.Emoji} {game.Name}",
                url: lootbar.AffiliateLink);
        }

        return Task.FromResult((embed.Build(), buttons.Build()));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Tạo phần mô tả cảnh báo chung cho mọi shop embed.</summary>
    private static string BuildShopWarningDescription(string platformName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("⚠️ **ĐỂ GHI NHẬN HỖ TRỢ SERVER:**");
        sb.AppendLine($"• Đăng nhập tài khoản {platformName} trước khi bấm link");
        sb.AppendLine("• Không dùng tab ẩn danh khi thanh toán");
        return sb.ToString();
    }

    /// <summary>Build field value cho LDShop — gồm discount, top-up type, warning.</summary>
    private static string BuildLdShopFieldValue(ShopGameConfig game, ProviderDiscount? ldShop)
    {
        var sb = new StringBuilder();

        sb.Append(ldShop is { Percent: > 0 }
            ? $"🔥 **-{ldShop.Percent}%**"
            : "🔥 Ưu đãi");

        if (!string.IsNullOrWhiteSpace(game.TopUpType))
            sb.Append($"\n⚡ {game.TopUpType}");

        if (!string.IsNullOrWhiteSpace(game.Warning))
            sb.Append($"\n{game.Warning}");

        return sb.ToString();
    }

    /// <summary>Thêm button link nếu chưa đạt giới hạn Discord (25 buttons).</summary>
    private static void AddButton(
        ComponentBuilder builder,
        ref int count,
        string label,
        string url)
    {
        if (count >= MaxButtons) return;
        builder.WithButton(label: label, style: ButtonStyle.Link, url: url);
        count++;
    }

    /// <summary>Kiểm tra URL hợp lệ (phải là http hoặc https).</summary>
    private static bool IsValidUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
