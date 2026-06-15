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

    // ── Overview ─────────────────────────────────────────────────────────

    public Task<(Embed embed, MessageComponent components)> BuildOverviewAsync()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("💎 **Tiết kiệm khi nạp game qua nhiều sàn!**");
        sb.AppendLine();
        sb.AppendLine("⚠️ **QUAN TRỌNG — ĐỂ GHI NHẬN HỖ TRỢ SERVER**");
        sb.AppendLine("• Đăng nhập tài khoản trước khi bấm link nạp");
        sb.AppendLine("• Không dùng tab ẩn danh khi thanh toán");
        sb.AppendLine();
        sb.AppendLine("🎮 Chọn game bên dưới để xem so sánh giá chi tiết");

        var embed = new EmbedBuilder()
            .WithTitle("🛒 So Sánh Giá Nạp Game")
            .WithColor(new Color(255, 165, 0))
            .WithDescription(sb.ToString())
            .WithFooter("🔄 Cập nhật lúc 00:00 & 12:00 (giờ VN)")
            .WithCurrentTimestamp();

        foreach (var game in _config.ShopGames)
        {
            var discounts = _aggregator.GetDiscounts(game);
            string fieldValue;

            if (discounts.Count == 0)
            {
                fieldValue = "🔥 Ưu đãi";
            }
            else if (discounts.Count == 1)
            {
                var d = discounts[0];
                fieldValue = d.Percent > 0 ? $"🔥 -{d.Percent}% ({d.ProviderName})" : "🔥 Ưu đãi";
            }
            else
            {
                // Hiển thị bên tốt nhất + số bên còn lại
                var best = discounts[0];
                fieldValue = $"🏆 -{best.Percent}% ({best.ProviderName})\n" +
                             string.Join(" | ", discounts.Skip(1).Select(d => $"-{d.Percent}% {d.ProviderName}"));
            }

            embed.AddField($"{game.Emoji} {game.Name}", fieldValue, inline: true);
        }

        // Buttons: link đến game đầu tiên có affiliate (tối đa 5 button/row)
        var buttons = new ComponentBuilder();
        foreach (var game in _config.ShopGames)
        {
            var discounts = _aggregator.GetDiscounts(game);
            if (discounts.Count == 0) continue;
            buttons.WithButton(
                label: $"{game.Emoji} {game.Name}",
                style: ButtonStyle.Link,
                url: discounts[0].AffiliateLink); // link của bên rẻ nhất
        }

        return Task.FromResult((embed.Build(), buttons.Build()));
    }

    // ── Game Embed ───────────────────────────────────────────────────────

    public Task<(Embed embed, MessageComponent components)?> BuildGameEmbedAsync(ShopGameConfig game)
    {
        var discounts = _aggregator.GetDiscounts(game);

        if (discounts.Count == 0)
        {
            _logger.LogWarning("[{Game}] không có provider nào có discount — bỏ qua embed", game.Name);
            return Task.FromResult<(Embed, MessageComponent)?>(null);
        }

        var sb = new System.Text.StringBuilder();

        // PromoNote
        if (!string.IsNullOrWhiteSpace(game.PromoNote))
        {
            sb.AppendLine($"💬 {game.PromoNote}");
            sb.AppendLine();
        }

        // So sánh giá
        if (discounts.Count == 1)
        {
            var d = discounts[0];
            if (d.Percent > 0)
                sb.AppendLine($"🔥 Giảm **{d.Percent}%** qua {d.ProviderName}");
        }
        else
        {
            sb.AppendLine("📊 **So sánh giá:**");
            for (var i = 0; i < discounts.Count; i++)
            {
                var d = discounts[i];
                var medal = i == 0 ? "🏆" : "  ";
                var pctStr = d.Percent > 0 ? $"-**{d.Percent}%**" : "Ưu đãi";
                sb.AppendLine($"{medal} {d.ProviderName}: {pctStr}");
            }
        }

        // TopUpType
        if (!string.IsNullOrWhiteSpace(game.TopUpType))
        {
            sb.AppendLine();
            sb.AppendLine($"⚡ {game.TopUpType}");
        }

        // Warning
        if (!string.IsNullOrWhiteSpace(game.Warning))
        {
            sb.AppendLine();
            sb.AppendLine(game.Warning);
        }

        // Tiêu đề embed: hiển thị discount tốt nhất
        var best = discounts[0];
        var titleDiscount = best.Percent > 0 ? $" — Giảm {best.Percent}%!" : string.Empty;

        var embed = new EmbedBuilder()
            .WithTitle($"{game.Emoji} {game.Name}{titleDiscount}")
            .WithDescription(sb.ToString())
            .WithColor(new Color(255, 140, 0))
            .WithFooter(discounts.Count > 1
                ? $"So sánh từ {discounts.Count} sàn • {string.Join(" vs ", discounts.Select(d => d.ProviderName))}"
                : $"{best.ProviderName} x {game.Name}")
            .WithCurrentTimestamp()
            .Build();

        // Buttons: 1 button per provider (tối đa 5)
        var buttons = new ComponentBuilder();
        foreach (var d in discounts.Take(5))
        {
            var label = d.Percent > 0
                ? $"🛒 {d.ProviderName} (-{d.Percent}%)"
                : $"🛒 Nạp qua {d.ProviderName}";
            buttons.WithButton(label: label, style: ButtonStyle.Link, url: d.AffiliateLink);
        }

        return Task.FromResult<(Embed, MessageComponent)?>((embed, buttons.Build()));
    }
}
