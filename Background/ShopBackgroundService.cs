using Discord;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouTubeDiscordBot.Config;
using YouTubeDiscordBot.Models;
using YouTubeDiscordBot.Services;

namespace YouTubeDiscordBot.Background;

/// <summary>
/// Background worker tự động refresh shop embeds theo lịch.
/// </summary>
public class ShopBackgroundService : BackgroundService
{
    private readonly IDiscordService _discord;
    private readonly DiscordService _discordImpl;
    private readonly BotConfiguration _config;
    private readonly IShopService _shopService;
    private readonly IShopMessagePersistenceService _persistence;
    private readonly ILogger<ShopBackgroundService> _logger;

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
            "ShopBackgroundService starting — Channel={ChannelId}, Refresh={Hours}h",
            _config.ShopChannelId, _config.ShopRefreshHours);

        await _discordImpl.WaitForReadyAsync();
        _logger.LogInformation("Discord ready — ShopBackgroundService running");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshShopAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ShopBackgroundService — unhandled exception during refresh");
            }

            _logger.LogInformation("Next shop refresh in {Hours}h", _config.ShopRefreshHours);
            await Task.Delay(TimeSpan.FromHours(_config.ShopRefreshHours), stoppingToken);
        }
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

        // Fetch discount mới nhất từ LDShop API (kết quả được cache)
        // Gọi 1 lần duy nhất trước khi build tất cả embeds
        await _shopService.WarmDiscountCacheAsync();

        var state = await _persistence.LoadAsync();
        var stateChanged = false;

        // 1. Overview message
        var overviewChanged = await RefreshOverviewAsync(channel, state);
        stateChanged |= overviewChanged;

        await Task.Delay(1500, ct); // Rate limit buffer

        // 2. Game embeds
        foreach (var game in _config.ShopGames)
        {
            var gameChanged = await RefreshGameEmbedAsync(channel, game, state);
            stateChanged |= gameChanged;
            await Task.Delay(2500, ct); // Rate limit buffer
        }

        if (stateChanged)
            await _persistence.SaveAsync(state);

        _logger.LogInformation("Shop refresh completed");
    }

    // ── Overview Message ─────────────────────────────────────────────────────

    /// <returns>true nếu state đã thay đổi (message ID mới)</returns>
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

    /// <returns>true nếu state đã thay đổi (message ID mới)</returns>
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