using Discord;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;
using YouTubeDiscordBot.Services;

namespace YouTubeDiscordBot.Background;

public class ShopBackgroundService : BackgroundService
{
    private readonly IDiscordService _discord;
    private readonly BotConfiguration _config;
    private readonly IShopService _shopService;
    private readonly IShopMessagePersistenceService _persistence;
    private readonly ILogger<ShopBackgroundService> _logger;

    // Chạy đúng 2 thời điểm cố định mỗi ngày (giờ Việt Nam UTC+7)
    private static readonly TimeSpan[] RefreshTimes =
    [
        TimeSpan.FromHours(0),  // 00:00
        TimeSpan.FromHours(12), // 12:00
    ];

    // UTC offset Việt Nam
    private static readonly TimeZoneInfo VnTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");

    public ShopBackgroundService(
        IDiscordService discord,
        IOptions<BotConfiguration> config,
        IShopService shopService,
        IShopMessagePersistenceService persistence,
        ILogger<ShopBackgroundService> logger)
    {
        _discord = discord;
        _config = config.Value;
        _shopService = shopService;
        _persistence = persistence;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ShopBackgroundService starting — refresh lúc 00:00 và 12:00 (giờ VN)");

        await _discord.WaitForReadyAsync();
        _logger.LogInformation("Discord ready — ShopBackgroundService running");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRefresh();
            var next = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow + delay, VnTimeZone);

            _logger.LogInformation(
                "Shop refresh tiếp theo lúc {Next:HH:mm dd/MM/yyyy} (giờ VN) — chờ {Delay:hh\\:mm}",
                next, delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await RefreshShopAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ShopBackgroundService — unhandled exception during refresh");
            }
        }
    }

    // ── Tính thời gian chờ đến 00:00 hoặc 12:00 tiếp theo ──────────────────

    private static TimeSpan GetDelayUntilNextRefresh()
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VnTimeZone);
        var todayTime = now.TimeOfDay;

        var next = RefreshTimes
            .Select(t => t > todayTime ? t : t.Add(TimeSpan.FromHours(24)))
            .Min();

        return next - todayTime;
    }

    // ── Refresh toàn bộ shop ─────────────────────────────────────────────────

    public async Task RefreshShopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Refreshing shop messages...");

        if (_discord.Client.GetChannel(_config.ShopChannelId) is not IMessageChannel channel)
        {
            _logger.LogWarning("Shop channel không tìm thấy: {ChannelId}", _config.ShopChannelId);
            return;
        }

        await _shopService.WarmDiscountCacheAsync();

        var state = await _persistence.LoadAsync();
        var changed = false;

        // ── Section LDShop ────────────────────────────────────────────────
        var (ldEmbed, ldComponents) = await _shopService.BuildLdShopEmbedAsync();
        changed |= await UpsertMessageAsync(channel, ldEmbed, ldComponents,
            ref state.LdShopMessageId, "LDShop", ct);

        await Task.Delay(1000, ct);

        // ── Section Lootbar ───────────────────────────────────────────────
        var (lbEmbed, lbComponents) = await _shopService.BuildLootbarEmbedAsync();
        changed |= await UpsertMessageAsync(channel, lbEmbed, lbComponents,
            ref state.LootbarMessageId, "Lootbar", ct);

        if (changed)
            await _persistence.SaveAsync(state);

        _logger.LogInformation("Shop refresh completed");
    }

    // ── Helper: edit nếu message tồn tại, tạo mới nếu bị xóa ───────────────

    private async Task<bool> UpsertMessageAsync(
        IMessageChannel channel,
        Embed embed,
        MessageComponent components,
        ref ulong messageId,
        string label,
        CancellationToken ct)
    {
        IUserMessage? existing = null;
        if (messageId != 0)
        {
            try { existing = await channel.GetMessageAsync(messageId) as IUserMessage; }
            catch { /* message bị xóa */ }
        }

        if (existing is not null)
        {
            await existing.ModifyAsync(m => { m.Embed = embed; m.Components = components; });
            _logger.LogInformation("[{Label}] embed updated — {MessageId}", label, existing.Id);
            return false;
        }

        var msg = await channel.SendMessageAsync(embed: embed, components: components);
        messageId = msg.Id;
        _logger.LogInformation("[{Label}] embed created — {MessageId}", label, msg.Id);
        return true;
    }
}
