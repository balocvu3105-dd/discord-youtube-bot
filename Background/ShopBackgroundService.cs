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

    // Mutex: ngăn timer và /refreshshop chạy đồng thời → tránh duplicate message
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    // Chạy đúng 2 thời điểm cố định mỗi ngày (giờ Việt Nam UTC+7)
    private static readonly TimeSpan[] RefreshTimes =
    [
        TimeSpan.FromHours(0),  // 00:00
        TimeSpan.FromHours(12), // 12:00
    ];

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
            var nextVn = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow + delay, VnTimeZone);

            _logger.LogInformation(
                "Shop refresh tiếp theo lúc {Next:HH:mm dd/MM/yyyy} (giờ VN) — chờ {Delay:hh\\:mm}",
                nextVn, delay);

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

    // ── Public: dùng được từ ShopCommandModule ───────────────────────────────

    public async Task RefreshShopAsync(CancellationToken ct = default)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            await RefreshShopCoreAsync(ct);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task RefreshShopCoreAsync(CancellationToken ct)
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

        // ── Section LDShop ────────────────────────────────────────────────────
        var (ldEmbed, ldComponents) = await _shopService.BuildLdShopEmbedAsync();
        var (ldChanged, ldId) = await UpsertMessageAsync(
            channel, ldEmbed, ldComponents, state.LdShopMessageId, "LDShop", ct);
        state.LdShopMessageId = ldId;
        changed |= ldChanged;

        // Delay nhỏ giữa 2 message để tránh rate limit Discord
        await Task.Delay(1000, ct);

        // ── Section Lootbar ───────────────────────────────────────────────────
        var (lbEmbed, lbComponents) = await _shopService.BuildLootbarEmbedAsync();
        var (lbChanged, lbId) = await UpsertMessageAsync(
            channel, lbEmbed, lbComponents, state.LootbarMessageId, "Lootbar", ct);
        state.LootbarMessageId = lbId;
        changed |= lbChanged;

        if (changed)
            await _persistence.SaveAsync(state);

        _logger.LogInformation("Shop refresh completed");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Tính thời gian chờ đến 00:00 hoặc 12:00 tiếp theo (giờ VN).</summary>
    private static TimeSpan GetDelayUntilNextRefresh()
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VnTimeZone);
        var todayTime = now.TimeOfDay;

        var next = RefreshTimes
            .Select(t => t > todayTime ? t : t.Add(TimeSpan.FromHours(24)))
            .Min();

        return next - todayTime;
    }

    /// <summary>
    /// Edit message nếu tồn tại; tạo mới nếu đã bị xóa.
    /// Trả về (changed, messageId) — changed=true khi tạo mới (cần save state).
    /// </summary>
    private async Task<(bool changed, ulong messageId)> UpsertMessageAsync(
        IMessageChannel channel,
        Embed embed,
        MessageComponent components,
        ulong currentId,
        string label,
        CancellationToken ct)
    {
        IUserMessage? existing = null;
        if (currentId != 0)
        {
            try
            {
                existing = await channel.GetMessageAsync(currentId) as IUserMessage;
            }
            catch (Exception ex)
            {
                // Message bị xóa hoặc không accessible — log để biết, rồi tạo mới bên dưới
                _logger.LogDebug(ex,
                    "[{Label}] GetMessageAsync({Id}) thất bại — sẽ tạo message mới",
                    label, currentId);
            }
        }

        if (existing is not null)
        {
            await existing.ModifyAsync(m =>
            {
                m.Embed = embed;
                m.Components = components;
            });
            _logger.LogInformation("[{Label}] embed updated — {MessageId}", label, existing.Id);
            return (false, existing.Id);
        }

        var msg = await channel.SendMessageAsync(embed: embed, components: components);
        _logger.LogInformation("[{Label}] embed created — {MessageId}", label, msg.Id);
        return (true, msg.Id);
    }
}
