using Discord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class ShopService : IShopService
{
    private readonly BotConfiguration _config;
    private readonly ShopDiscountAggregator _aggregator;
    private readonly ILogger<ShopService> _logger;

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

    // ── LDShop Section ───────────────────────────────────────────────────

    public Task<(Embed embed, MessageComponent components)> BuildLdShopEmbedAsync()
    {
        var desc = new System.Text.StringBuilder();
        desc.AppendLine("⚠️ **ĐỂ GHI NHẬN HỖ TRỢ SERVER:**");
        desc.AppendLine("• Đăng nhập tài khoản LDShop trước khi bấm link");
        desc.AppendLine("• Không dùng tab ẩn danh khi thanh toán");

        var embed = new EmbedBuilder()
            .WithTitle("🛒 LDShop — Nạp Game Giá Rẻ")
            .WithColor(new Color(255, 140, 0))
            .WithDescription(desc.ToString())
            .WithFooter("🔄 Cập nhật lúc 00:00 & 12:00 (giờ VN)")
            .WithCurrentTimestamp();

        var buttons = new ComponentBuilder();
        var btnCount = 0;

        foreach (var game in _config.ShopGames)
        {
            if (string.IsNullOrWhiteSpace(game.AffiliateLink)) continue;

            var discounts = _aggregator.GetDiscounts(game);
            var ldShop = discounts.FirstOrDefault(d => d.ProviderName == "LDShop");

            // Field giá trị
            var fieldLines = new System.Text.StringBuilder();
            if (ldShop is not null && ldShop.Percent > 0)
                fieldLines.Append($"🔥 **-{ldShop.Percent}%**");
            else
                fieldLines.Append("🔥 Ưu đãi");

            if (!string.IsNullOrWhiteSpace(game.TopUpType))
                fieldLines.Append($"\n⚡ {game.TopUpType}");

            if (!string.IsNullOrWhiteSpace(game.Warning))
                fieldLines.Append($"\n{game.Warning}");

            embed.AddField($"{game.Emoji} {game.Name}", fieldLines.ToString(), inline: true);

            // Button (Discord giới hạn 25 button / 5 hàng)
            if (btnCount < 25)
            {
                var label = ldShop is not null && ldShop.Percent > 0
                    ? $"{game.Emoji} {game.Name} (-{ldShop.Percent}%)"
                    : $"{game.Emoji} {game.Name}";
                buttons.WithButton(label: label, style: ButtonStyle.Link, url: game.AffiliateLink);
                btnCount++;
            }
        }

        return Task.FromResult((embed.Build(), buttons.Build()));
    }

    // ── Lootbar Section ──────────────────────────────────────────────────

    public Task<(Embed embed, MessageComponent components)> BuildLootbarEmbedAsync()
    {
        var shopLink = _config.LootbarShopLink;
        if (string.IsNullOrWhiteSpace(shopLink))
            shopLink = "https://www.lootbar.com";

        var desc = new System.Text.StringBuilder();
        desc.AppendLine("⚠️ **ĐỂ GHI NHẬN HỖ TRỢ SERVER:**");
        desc.AppendLine("• Đăng nhập tài khoản Lootbar trước khi bấm link");
        desc.AppendLine("• Không dùng tab ẩn danh khi thanh toán");

        var embed = new EmbedBuilder()
            .WithTitle("🏪 Lootbar — Cửa Hàng CataWuwa")
            .WithColor(new Color(88, 101, 242))  // màu tím/xanh riêng để phân biệt với LDShop
            .WithDescription(desc.ToString())
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
                // Lootbar không có data cho game này → bỏ qua field
                _logger.LogDebug("[Lootbar section] {Game} — không có data, bỏ qua", game.Name);
                continue;
            }

            var fieldValue = lootbar.Percent > 0
                ? $"🔥 **-{lootbar.Percent}%**"
                : "🔥 Ưu đãi";

            embed.AddField($"{game.Emoji} {game.Name}", fieldValue, inline: true);

            if (btnCount < 25)
            {
                var label = lootbar.Percent > 0
                    ? $"{game.Emoji} {game.Name} (-{lootbar.Percent}%)"
                    : $"{game.Emoji} {game.Name}";
                buttons.WithButton(label: label, style: ButtonStyle.Link, url: lootbar.AffiliateLink);
                btnCount++;
            }
        }

        return Task.FromResult((embed.Build(), buttons.Build()));
    }
}
