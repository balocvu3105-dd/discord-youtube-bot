using Discord;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

public class PromoChangeBackgroundService : BackgroundService
{
    private readonly LdShopScraperService _scraper;
    private readonly PromoChangeDetectorService _detector;
    private readonly DiscordService _discordService;
    private readonly BotConfiguration _config;
    private readonly ILogger<PromoChangeBackgroundService> _logger;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(2);

    public PromoChangeBackgroundService(
        LdShopScraperService scraper,
        PromoChangeDetectorService detector,
        DiscordService discordService,
        IOptions<BotConfiguration> config,
        ILogger<PromoChangeBackgroundService> logger)
    {
        _scraper = scraper;
        _detector = detector;
        _discordService = discordService;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "🔍 PromoChangeBackgroundService started. Check every {Hours}h",
            CheckInterval.TotalHours);

        // Đợi Discord ready trước
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndNotifyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ PromoChangeBackgroundService error");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckAndNotifyAsync()
    {
        // 1. Scrape web
        var current = await _scraper.ScrapePromosAsync();
        if (current.Count == 0)
        {
            _logger.LogWarning("⚠️ Scrape trả về 0 kết quả, bỏ qua");
            return;
        }

        // 2. So sánh với snapshot cũ
        var changes = _detector.DetectChanges(current);
        if (!changes.HasChanges) return;

        // 3. Gửi thông báo Discord
        var embed = BuildChangeEmbed(changes);
        await _discordService.SendToChannelAsync(_config.PromoChannelName, embed);
    }
    private static Embed BuildChangeEmbed(PromoChanges changes)
    {
        var eb = new EmbedBuilder()
            .WithTitle("🔔 LDShop — Khuyến Mãi Vừa Thay Đổi!")
            .WithColor(new Color(0, 200, 100))
            .WithCurrentTimestamp()
            .WithFooter("ldshop.gg • Cập nhật tự động");

        // =====================================================
        // NEW DEALS
        // =====================================================

        if (changes.NewGames.Count > 0)
        {
            var lines = changes.NewGames
                .Take(10)
                .Select(g =>
                    $"🆕 **{g.Name}** — -{g.DiscountPercent}%");

            var text = string.Join("\n", lines);

            if (changes.NewGames.Count > 10)
            {
                text +=
                    $"\n\n...và thêm {changes.NewGames.Count - 10} deal khác";
            }

            eb.AddField(
                "✨ Deal mới xuất hiện",
                text);
        }

        // =====================================================
        // UPDATED DEALS
        // =====================================================

        if (changes.UpdatedGames.Count > 0)
        {
            var lines = changes.UpdatedGames
                .Take(10)
                .Select(u =>
                {
                    var arrow =
                        u.Promo.DiscountPercent > u.OldDiscount
                            ? "📈"
                            : "📉";

                    return
                        $"{arrow} **{u.Promo.Name}** — " +
                        $"~~-{u.OldDiscount}%~~ → " +
                        $"**-{u.Promo.DiscountPercent}%**";
                });

            var text = string.Join("\n", lines);

            if (changes.UpdatedGames.Count > 10)
            {
                text +=
                    $"\n\n...và thêm {changes.UpdatedGames.Count - 10} thay đổi";
            }

            eb.AddField(
                "🔄 Thay đổi phần trăm",
                text);
        }

        // =====================================================
        // REMOVED DEALS
        // =====================================================

        if (changes.RemovedGames.Count > 0)
        {
            var lines = changes.RemovedGames
                .Take(10)
                .Select(g =>
                    $"❌ **{g.Name}** — deal kết thúc");

            var text = string.Join("\n", lines);

            if (changes.RemovedGames.Count > 10)
            {
                text +=
                    $"\n\n...và thêm {changes.RemovedGames.Count - 10} deal hết hạn";
            }

            eb.AddField(
                "⏰ Ưu đãi kết thúc",
                text);
        }

        return eb.Build();
    }
}

