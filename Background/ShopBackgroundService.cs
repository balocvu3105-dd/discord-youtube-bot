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
    private readonly DiscordService _discordImpl;
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
        DiscordService discordImpl,
        IOptions<BotConfiguration> config,
        IShopService shopService,
        IShopMessagePersistenceService persistence,
        ILogger<ShopBackgroundService> logger)
    {
        _discord = discord;
        _discordImpl = discordImpl;
        _config = config.Value;
        _shopService = shopService;
        _persistence = persistence;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ShopBackgroundService starting — refresh lúc 00:00 và 12:00 (giờ VN)");

        await _discordImpl.WaitForReadyAsync();
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

        // Tìm thời điểm gần nhất trong tương lai
        var next = RefreshTimes
            .Select(t => t > todayTime ? t : t.Add(TimeSpan.FromHours(24)))
            .Min();

        return next - todayTime;
    }

    // ── Refresh All Shop Messages ────────────────────────────────────────────

    private async Task RefreshShopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Refreshing shop messages...");

        if (_discord.Client.GetChannel(_config.ShopChannelId) is not IMessageChannel channel)
        {
            _logger.LogWarning("Shop channel không tìm thấy: {ChannelId}", _config.ShopChannelId);
            return;
        }

        await _shopService.WarmDiscountCacheAsync();

        var state = await _persistence.LoadAsync();
        var stateChanged = false;

        var overviewChanged = await RefreshOverviewAsync(channel, state);
        stateChanged |= overviewChanged;

        await Task.Delay(1500, ct);

        foreach (var game in _config.ShopGames)
        {
            var gameChanged = await RefreshGameEmbedAsync(channel, game, state);
            stateChanged |= gameChanged;
            await Task.Delay(2500, ct);
        }

        if (stateChanged)
            await _persistence.SaveAsync(state);

        _logger.LogInformation("Shop refresh completed");
    }

    // ── Overview Message ─────────────────────────────────────────────────────

    private async Task<bool> RefreshOverviewAsync(IMessageChannel channel, ShopMessageState state)
    {
        var (embed, components) = await _shopService.BuildOverviewAsync();

        IUserMessage? existing = null;
        if (state.PinnedMessageId != 0)
        {
            try { existing = await channel.GetMessageAsync(state.PinnedMessageId) as IUserMessage; }
            catch { /* message bị xóa */ }
        }

        if (existing is null)
        {
            var msg = await channel.SendMessageAsync(embed: embed, components: components);
            state.PinnedMessageId = msg.Id;
            _logger.LogInformation("Overview message created — {MessageId}", msg.Id);
            return true;
        }

        await existing.ModifyAsync(m => { m.Embed = embed; m.Components = components; });
        _logger.LogInformation("Overview message updated — {MessageId}", existing.Id);
        return false;
    }

    // ── Game Embed ───────────────────────────────────────────────────────────

    private async Task<bool> RefreshGameEmbedAsync(
        IMessageChannel channel, ShopGameConfig game, ShopMessageState state)
    {
        var result = await _shopService.BuildGameEmbedAsync(game);
        if (result is null) return false;

        var (embed, components) = result.Value;

        IUserMessage? existing = null;
        if (state.GameMessageIds.TryGetValue(game.Name, out var existingId))
        {
            try { existing = await channel.GetMessageAsync(existingId) as IUserMessage; }
            catch { /* message bị xóa */ }
        }

        if (existing is null)
        {
            var msg = await channel.SendMessageAsync(embed: embed, components: components);
            state.GameMessageIds[game.Name] = msg.Id;
            _logger.LogInformation("[{Game}] embed created — {MessageId}", game.Name, msg.Id);
            return true;
        }

        await existing.ModifyAsync(m => { m.Embed = embed; m.Components = components; });
        _logger.LogInformation("[{Game}] embed updated — {MessageId}", game.Name, existing.Id);
        return false;
    }
}